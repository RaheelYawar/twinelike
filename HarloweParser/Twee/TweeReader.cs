using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Tokens;

namespace Harlowe.Twee
{
  /// <summary>
  /// Parses Twee 3 source into a <see cref="Harlowe"/> story. Twee is the
  /// plain-text counterpart to the HTML export Twine 2 emits — passages are
  /// separated by <c>::</c> headers at column 0, and a special
  /// <c>:: StoryData</c> passage carries the metadata that <c>tw-storydata</c>
  /// attributes hold in the HTML form.
  ///
  /// <para><b>On the <c>Harlowe.Twee</c> namespace.</b> Twee and Harlowe are
  /// formally orthogonal: Twee is a serialization format, Harlowe is one of
  /// several story formats (alongside SugarCube, Chapbook, Snowman) that can
  /// live inside Twee. This reader is in <c>Harlowe.Twee</c> because passage
  /// bodies route through <see cref="HarloweTokenizer"/> +
  /// <see cref="HarloweBodyParser"/>, so it only handles Twee files whose
  /// <c>:: StoryData</c> declares Harlowe as the format. A future
  /// generalization would lift this into a top-level <c>Twee</c> namespace
  /// with a pluggable body adapter, but until that's needed the nesting
  /// honestly reflects what the code does.</para>
  ///
  /// <para>Body parsing routes through the existing
  /// <see cref="HarloweTokenizer"/> + <see cref="HarloweBodyParser"/>, so the
  /// AST shape is identical regardless of which front-end loaded the story.
  /// HTML-only metadata (<c>creator</c>, <c>creator-version</c>) is left empty
  /// for Twee-loaded stories because the Twee 3 spec doesn't carry it.</para>
  ///
  /// <para>Twee passages don't have pids — Twine assigns them only at HTML
  /// export time. To keep <see cref="Harlowe.GetStartPassage"/> and
  /// <see cref="HarlowePassage.Pid"/> usable, the reader synthesizes
  /// sequential pids ("1", "2", …) in source order and resolves
  /// StoryData's <c>start</c> name to the matching synthesized pid.</para>
  /// </summary>
  public class TweeReader
  {
    /// <summary>
    /// Parse <paramref name="source"/> as Twee 3 text and return a populated
    /// <see cref="Harlowe"/>. A null or empty input yields an empty story
    /// (no passages, default metadata) rather than throwing — same forgiving
    /// shape as <see cref="Harlowe.GetPassage"/>.
    /// </summary>
    public Harlowe Read(string source)
    {
      var story = new Harlowe();
      if (string.IsNullOrEmpty(source)) return story;

      var tokenizer = new HarloweTokenizer();
      var bodyParser = new HarloweBodyParser();
      string pendingStartName = null;
      int nextPid = 1;

      foreach (var block in SplitPassages(source))
      {
        if (block.Name == "StoryTitle")
        {
          story.StoryName = block.Body.TrimEnd('\r', '\n');
          continue;
        }
        if (block.Name == "StoryData")
        {
          pendingStartName = ApplyStoryData(story, block.Body);
          continue;
        }

        PassageBody ast;
        try
        {
          var tokens = tokenizer.Tokenize(block.Body);
          ast = bodyParser.Parse(tokens, block.Body);
        }
        catch (HarloweParseException ex)
        {
          // Per-passage recovery: a broken body doesn't abort the whole Twee
          // load. The synthetic AST renders the parse error in place of the
          // passage's prose at render time. RawBody is preserved verbatim so
          // a subsequent TweeWriter round-trip emits the original source.
          ast = Harlowe.MakeParseErrorAst(block.Name, ex, block.Body);
        }
        // The body parser also recovers per-node and may leave ParseErrorNodes
        // alongside successfully-parsed children — decorate those with the
        // passage name so the rendered error names the broken passage.
        Harlowe.DecorateParseErrors(ast, block.Name);
        Harlowe.EnsureWholeStubOriginalSource(ast, block.Body);

        var passage = new HarlowePassage
        {
          Pid = nextPid.ToString(CultureInfo.InvariantCulture),
          Name = block.Name,
          Tags = block.Tags,
          Position = block.Position,
          Ast = ast,
          Branches = BranchCollector.Collect(ast),
          // Body holds the raw author source verbatim, matching the HTML
          // loader and AddPassage. The previous "macro-stripped prose" shape
          // diverged from those paths and returned an empty string for
          // parse-error-recovered passages (indistinguishable from missing).
          Body = block.Body,
          RawBody = block.Body,
        };
        try { story.AddPassage(passage); }
        catch (System.ArgumentException ex)
        {
          // Dictionary.Add throws ArgumentException on duplicate name. Convert
          // to the loader's canonical bad-input exception so consumers can
          // catch a single type for "this Twee file is broken."
          throw new HarloweParseException(
            $"duplicate passage name '{passage.Name}'", -1, -1, passage.Name, ex);
        }
        nextPid++;
      }

      if (!string.IsNullOrEmpty(pendingStartName))
      {
        var start = story.GetPassage(pendingStartName);
        if (start != null) story.StartNode = start.Pid;
      }

      return story;
    }

    /// <summary>
    /// Walks the source one line at a time, emitting one
    /// <see cref="PassageBlock"/> per <c>::</c> header. A header line is any
    /// line whose first two characters are <c>::</c>; lines starting with
    /// <c>\::</c> are body content with the leading backslash stripped (Twee
    /// 3 escape rule). Content before the first header is silently discarded
    /// — Twee files don't carry preamble.
    /// </summary>
    private static IEnumerable<PassageBlock> SplitPassages(string source)
    {
      var lines = SplitLines(source);
      int i = 0;
      while (i < lines.Count && !IsHeaderLine(lines[i])) i++;

      while (i < lines.Count)
      {
        var header = ParseHeader(lines[i]);
        i++;
        var body = new StringBuilder();
        bool first = true;
        while (i < lines.Count && !IsHeaderLine(lines[i]))
        {
          if (!first) body.Append('\n');
          string line = lines[i];
          if (line.Length >= 3 && line[0] == '\\' && line[1] == ':' && line[2] == ':')
            line = line.Substring(1);
          body.Append(line);
          first = false;
          i++;
        }
        header.Body = body.ToString().TrimEnd('\r', '\n');
        yield return header;
      }
    }

    /// <summary>
    /// Splits <paramref name="source"/> on <c>\n</c> with line endings stripped
    /// (both <c>\r\n</c> and lone <c>\n</c> are normalized away). A trailing
    /// blank line from a final newline is dropped so the iteration count
    /// reflects content lines.
    /// </summary>
    private static List<string> SplitLines(string source)
    {
      var result = new List<string>();
      int start = 0;
      for (int i = 0; i < source.Length; i++)
      {
        if (source[i] == '\n')
        {
          int end = i;
          if (end > start && source[end - 1] == '\r') end--;
          result.Add(source.Substring(start, end - start));
          start = i + 1;
        }
      }
      if (start < source.Length) result.Add(source.Substring(start));
      return result;
    }

    private static bool IsHeaderLine(string line)
      => line.Length >= 2 && line[0] == ':' && line[1] == ':';

    /// <summary>
    /// Parses a Twee 3 header line into name, tags, and an optional position
    /// blob. Format: <c>:: Name [tag1 tag2] {position-json}</c>. Tags and
    /// position metadata are both optional and may appear in either order
    /// after the name. The name runs until the first <c>[</c>, <c>{</c>, or
    /// end-of-line.
    /// </summary>
    private static PassageBlock ParseHeader(string line)
    {
      var block = new PassageBlock();
      int p = 2; // skip ::
      while (p < line.Length && line[p] == ' ') p++;

      int nameEnd = p;
      while (nameEnd < line.Length && line[nameEnd] != '[' && line[nameEnd] != '{') nameEnd++;
      block.Name = line.Substring(p, nameEnd - p).TrimEnd();
      p = nameEnd;

      block.Tags = new List<string>();
      while (p < line.Length)
      {
        if (line[p] == ' ') { p++; continue; }
        if (line[p] == '[')
        {
          int close = FindUnescapedTagBlockClose(line, p + 1);
          if (close < 0) break;
          ParseTagBlockInto(line, p + 1, close, block.Tags);
          p = close + 1;
          continue;
        }
        if (line[p] == '{')
        {
          int close = line.IndexOf('}', p);
          if (close < 0) break;
          block.Position = line.Substring(p, close - p + 1);
          p = close + 1;
          continue;
        }
        break;
      }
      return block;
    }

    /// <summary>
    /// Scans forward from <paramref name="from"/> for the first unescaped
    /// <c>]</c>. A <c>]</c> preceded by an odd number of backslashes is escaped
    /// per the Twee 3 spec — <c>\\]</c> is "literal backslash, end of block",
    /// <c>\]</c> is "literal closing bracket". Returns -1 if none is found.
    /// </summary>
    private static int FindUnescapedTagBlockClose(string line, int from)
    {
      int i = from;
      while (i < line.Length)
      {
        if (line[i] == '\\' && i + 1 < line.Length) { i += 2; continue; }
        if (line[i] == ']') return i;
        i++;
      }
      return -1;
    }

    /// <summary>
    /// Walk a tag-block body, treating <c>\X</c> as a literal <c>X</c> (Twee 3
    /// tag escape — covers <c>\[</c>, <c>\]</c>, <c>\\</c>, plus tolerant
    /// passthrough for anything else after a backslash) and splitting on
    /// unescaped whitespace. Empty tokens are dropped so <c>[ a   b ]</c>
    /// produces just <c>a</c> and <c>b</c>.
    /// </summary>
    private static void ParseTagBlockInto(string line, int from, int toExclusive, List<string> tags)
    {
      var sb = new StringBuilder();
      for (int i = from; i < toExclusive; i++)
      {
        char c = line[i];
        if (c == '\\' && i + 1 < toExclusive)
        {
          sb.Append(line[i + 1]);
          i++;
          continue;
        }
        if (c == ' ' || c == '\t')
        {
          if (sb.Length > 0) { tags.Add(sb.ToString()); sb.Length = 0; }
          continue;
        }
        sb.Append(c);
      }
      if (sb.Length > 0) tags.Add(sb.ToString());
    }

    /// <summary>
    /// Reads the JSON body of a <c>:: StoryData</c> passage and copies the
    /// recognised fields onto <paramref name="story"/>. The full parsed
    /// dictionary is also stashed on <see cref="Harlowe.StoryDataExtras"/> so
    /// keys we don't surface as typed properties (<c>tag-colors</c>,
    /// <c>zoom</c>, anything Twine adds later) round-trip through the writer
    /// without code changes. Returns the value of the <c>start</c> field so
    /// the caller can resolve it to a synthesized pid once all passages are
    /// loaded.
    /// </summary>
    private static string ApplyStoryData(Harlowe story, string json)
    {
      if (string.IsNullOrWhiteSpace(json)) return null;
      var parsed = new JsonReader().Read(json);
      if (!(parsed is Dictionary<string, object> dict))
        throw new HarloweParseException("StoryData must be a JSON object", -1, -1, "StoryData");

      story.StoryDataExtras = dict;
      story.Ifid = AsString(dict, "ifid", string.Empty);
      story.Format = AsString(dict, "format", string.Empty);
      story.FormatVersion = AsString(dict, "format-version", string.Empty);
      return AsString(dict, "start", null);
    }

    private static string AsString(Dictionary<string, object> dict, string key, string fallback)
    {
      if (!dict.TryGetValue(key, out object v) || v == null) return fallback;
      return v as string ?? fallback;
    }

    private class PassageBlock
    {
      public string Name;
      public List<string> Tags = new List<string>();
      public string Position;
      public string Body;
    }
  }
}
