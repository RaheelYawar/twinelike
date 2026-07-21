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
    private readonly HarloweProfile _profileOverride;

    /// <summary>Reads stories under the compatibility profile each one's <c>:: StoryData</c> declares.</summary>
    public TweeReader() : this(null) { }

    /// <summary>
    /// Reads stories under an explicit compatibility profile, overriding the
    /// major their <c>:: StoryData</c> declares. Null follows the declaration.
    ///
    /// <para>The override belongs on the reader rather than on
    /// <see cref="Harlowe.Profile"/> because some switches are lexical: by the
    /// time <see cref="Read"/> returns, every passage body has been tokenized.
    /// This is the only entry point that can affect them.</para>
    /// </summary>
    public TweeReader(HarloweProfile profile)
    {
      _profileOverride = profile;
    }

    /// <summary>
    /// Parse <paramref name="source"/> as Twee 3 text and return a populated
    /// <see cref="Harlowe"/>. A null or empty input yields an empty story
    /// (no passages, default metadata) rather than throwing — same forgiving
    /// shape as <see cref="Harlowe.GetPassage"/>. A leading UTF-8 BOM
    /// (U+FEFF) is stripped: callers that decode bytes with
    /// <c>Encoding.UTF8.GetString</c> (rather than BOM-aware
    /// <c>File.ReadAllText</c>) leave it on the string, where it would
    /// otherwise hide the first <c>::</c> header and silently drop the first
    /// passage (or the whole single-passage story).
    ///
    /// <para><b>Metadata is applied in a pre-pass</b>, before any passage body
    /// is lexed. <c>:: StoryData</c> is legal anywhere in the file, and its
    /// <c>format-version</c> selects the compatibility profile every body is
    /// tokenized under — read in source order, a trailing metadata block would
    /// arrive after the passages it governs.</para>
    /// </summary>
    public Harlowe Read(string source)
    {
      var story = new Harlowe();
      if (string.IsNullOrEmpty(source)) return story;
      if (source[0] == '\uFEFF') source = source.Substring(1);

      // Null leaves the story following its own declared format-version.
      story.Profile = _profileOverride;

      // Materialised once: SplitPassages is a lazy iterator that re-walks every
      // line and rebuilds every body string on each enumeration, so the
      // metadata pre-pass below would otherwise double all Twee string work.
      var blocks = new List<PassageBlock>(SplitPassages(source));

      string pendingStartName = null;
      foreach (var block in blocks)
      {
        if (block.Name != "StoryData") continue;
        // Every StoryData block is applied, in source order, preserving the
        // last-wins result the old single-pass read produced. Applying only
        // the first would leave the profile resolved from one block while the
        // story's metadata came from another.
        //
        // A malformed StoryData must not abort the load. The Twee 3 spec's
        // recommendation for a decoding error is to "emit a warning, discard
        // the metadata, and continue processing the file" — we have no warning
        // sink, so we discard and continue, matching the per-passage recovery
        // below and the loader's never-throw policy. Recovery stays per block:
        // ApplyStoryData throws before it writes anything, so a broken block
        // leaves an earlier block's metadata standing.
        try { pendingStartName = ApplyStoryData(story, block.Body); }
        catch (HarloweParseException) { }
      }

      // Constructed after the pre-pass, not before it: story.Profile is only
      // now final, and a tokenizer's profile is fixed at construction.
      var tokenizer = new HarloweTokenizer(story.Profile);
      var bodyParser = new HarloweBodyParser();
      int nextPid = 1;

      foreach (var block in blocks)
      {
        if (block.Name == "StoryTitle")
        {
          story.StoryName = block.Body.TrimEnd('\r', '\n');
          continue;
        }
        if (block.Name == "StoryData") continue;   // applied in the pre-pass above

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
          // RawBody holds the raw author source verbatim (Body is its alias),
          // matching the HTML loader and AddPassage.
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
    ///
    /// <para>LF-only by design. Bare <c>\r</c> (classic-Mac line endings,
    /// effectively extinct), Unicode line separators (U+2028 / U+2029),
    /// and NEL (U+0085) are not recognised as line breaks — a file using
    /// any of those exclusively would collapse to a single line and only
    /// the leading header would parse. This matches reference Twee 3
    /// tooling: Tweego splits on <c>\n::</c> via byte scan, Extwee splits
    /// on <c>\n::</c> via <c>String.split</c> — neither recognises CR-only
    /// or Unicode separators either. <see cref="TweeWriter.AppendEscapedBody"/>
    /// uses the same LF-only line-start detection so the writer/reader pair
    /// is symmetric.</para>
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
      while (nameEnd < line.Length)
      {
        char c = line[nameEnd];
        // A backslash escapes the next character (including the [ and { that
        // would otherwise end the name), so consume the pair — mirrors
        // FindUnescapedTagBlockClose. The backslash itself is removed by
        // UnescapeName below.
        if (c == '\\' && nameEnd + 1 < line.Length) { nameEnd += 2; continue; }
        if (c == '[' || c == '{') break;
        nameEnd++;
      }
      // TrimEnd drops the separator whitespace before [tags]/{position};
      // unescape AFTER trimming so an escaped trailing character isn't trimmed.
      block.Name = UnescapeName(line.Substring(p, nameEnd - p).TrimEnd());
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
          int close = FindMetadataBlockClose(line, p);
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
    /// Scans forward from the opening <c>{</c> at <paramref name="open"/> for
    /// its matching <c>}</c>, tracking nested braces and JSON strings (a brace
    /// inside a quoted value is content, and an escaped <c>\"</c> inside a
    /// string doesn't end it). Returns -1 when the block never closes. A plain
    /// first-<c>}</c> scan truncated metadata carrying a nested object —
    /// <c>{"position":"1,2","meta":{"a":1}}</c> lost its final brace, and since
    /// the blob is stored opaque and re-emitted verbatim, the writer then
    /// round-tripped <em>invalid</em> JSON. Twine's flat
    /// <c>{"position":…,"size":…}</c> never nests; custom metadata can.
    /// </summary>
    private static int FindMetadataBlockClose(string line, int open)
    {
      int depth = 0;
      bool inString = false;
      for (int i = open; i < line.Length; i++)
      {
        char c = line[i];
        if (inString)
        {
          if (c == '\\' && i + 1 < line.Length) { i++; continue; }
          if (c == '"') inString = false;
          continue;
        }
        if (c == '"') { inString = true; continue; }
        if (c == '{') depth++;
        else if (c == '}')
        {
          depth--;
          if (depth == 0) return i;
        }
      }
      return -1;
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
    /// Strips Twee 3 name escapes: <c>\X</c> yields <c>X</c> for any <c>X</c>
    /// (tolerant decoding per the spec — "any escaped character within a chunk
    /// of encoded text must yield the character minus the backslash"). Mirrors
    /// the tag-block unescape in <see cref="ParseTagBlockInto"/> without the
    /// whitespace-splitting, since passage names may contain spaces. A trailing
    /// lone backslash (malformed encoding) is passed through literally.
    /// </summary>
    private static string UnescapeName(string raw)
    {
      if (raw.IndexOf('\\') < 0) return raw;
      var sb = new StringBuilder(raw.Length);
      for (int i = 0; i < raw.Length; i++)
      {
        char c = raw[i];
        if (c == '\\' && i + 1 < raw.Length) { sb.Append(raw[i + 1]); i++; continue; }
        sb.Append(c);
      }
      return sb.ToString();
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
      return StartName(dict);
    }

    private static string AsString(Dictionary<string, object> dict, string key, string fallback)
    {
      if (!dict.TryGetValue(key, out object v) || v == null) return fallback;
      return v as string ?? fallback;
    }

    /// <summary>
    /// Reads the <c>start</c> field, coercing a scalar to its JSON text. The
    /// spec types <c>start</c> as a string, but a tool that knows the start
    /// passage's name is numeric may emit <c>{"start": 123}</c> — a string-only
    /// read drops that silently, and the story loads unable to start with no
    /// diagnostic anywhere. Coerced names resolve through the same
    /// passage-lookup gate as spec-shaped ones (an unresolvable name leaves
    /// <see cref="Harlowe.StartNode"/> untouched either way); a structured
    /// value (object/array) can't be a passage name and stays unresolved.
    /// Number formatting mirrors <see cref="JsonWriter"/>'s: whole values
    /// render without a trailing <c>.0</c>.
    /// </summary>
    private static string StartName(Dictionary<string, object> dict)
    {
      if (!dict.TryGetValue("start", out object v) || v == null) return null;
      if (v is string s) return s;
      if (v is bool b) return b ? "true" : "false";
      if (v is double d)
      {
        if (double.IsNaN(d) || double.IsInfinity(d)) return null;
        if (d == System.Math.Floor(d) && d >= long.MinValue && d <= long.MaxValue)
          return ((long)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture);
      }
      return null;
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
