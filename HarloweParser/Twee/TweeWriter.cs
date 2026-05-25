using System.Collections.Generic;
using System.Text;

namespace Harlowe.Twee
{
  /// <summary>
  /// Serializes a <see cref="Harlowe"/> story back to Twee 3 source text.
  /// Counterpart to <see cref="TweeReader"/>; together they support
  /// **interchangeable editing** with Twine 2 — a story round-tripped through
  /// this library produces a Twee file Twine 2 can re-open without losing
  /// metadata.
  ///
  /// <para><b>Lazy reserialization.</b> Each passage carries an
  /// <see cref="HarlowePassage.IsDirty"/> bit. Clean passages (the default)
  /// emit their <see cref="HarlowePassage.RawBody"/> verbatim — no
  /// reformatting, passage-body content preserved as it was read. (Trailing
  /// newlines on a body are normalised away by the reader so the writer can
  /// own inter-passage separation consistently — see
  /// <see cref="Twee.TweeReader"/>; the body proper is left unchanged.)
  /// Dirty passages run their AST through <see cref="MarkupPrinter"/> in
  /// canonical form. Mirrors Twine 2's approach (passage bodies are stored as
  /// opaque strings) so cross-tool diffs are scoped to actually-edited
  /// passages instead of every passage every save.
  /// </para>
  ///
  /// <para><b>StoryData round-trip.</b> Typed fields (<c>ifid</c>,
  /// <c>format</c>, <c>format-version</c>, <c>start</c>) are overlaid onto a
  /// copy of <see cref="Harlowe.StoryDataExtras"/> before emit, so any keys
  /// the reader didn't surface as typed properties (<c>tag-colors</c>,
  /// <c>zoom</c>, future Twine fields) pass through unchanged.</para>
  ///
  /// <para><b>Header escape.</b> Body lines that start with <c>::</c> are
  /// re-escaped to <c>\::</c> on emit so they don't read as passage headers
  /// when re-parsed. The reader strips the leading backslash.</para>
  /// </summary>
  public class TweeWriter
  {
    /// <summary>
    /// Serialize <paramref name="story"/> as Twee 3 source. Emits in this
    /// order: <c>:: StoryTitle</c> (when set), <c>:: StoryData</c> (when
    /// metadata is non-empty), then each passage in load order. Blocks are
    /// separated by a blank line. The output ends with a single trailing
    /// newline.
    /// </summary>
    public string Write(Harlowe story)
    {
      if (story == null) return string.Empty;

      var blocks = new List<string>();

      if (!string.IsNullOrEmpty(story.StoryName))
        blocks.Add(BuildStoryTitleBlock(story.StoryName));

      string storyDataBlock = BuildStoryDataBlock(story);
      if (storyDataBlock != null) blocks.Add(storyDataBlock);

      foreach (var passage in story.Passages)
        blocks.Add(BuildPassageBlock(passage));

      var sb = new StringBuilder();
      for (int i = 0; i < blocks.Count; i++)
      {
        if (i > 0) sb.Append("\n\n");
        sb.Append(blocks[i]);
      }
      if (blocks.Count > 0) sb.Append('\n');
      return sb.ToString();
    }

    private static string BuildStoryTitleBlock(string title)
      => ":: StoryTitle\n" + title;

    /// <summary>
    /// Builds the <c>:: StoryData</c> block by overlaying typed fields onto a
    /// shallow copy of <see cref="Harlowe.StoryDataExtras"/>. Typed fields are
    /// authoritative: assigning to a key in place preserves its original
    /// position in the dictionary, while an empty/null typed value
    /// <em>removes</em> the key from the emitted JSON — so clearing
    /// <see cref="Harlowe.Ifid"/> or removing the start passage actually
    /// drops the stale value from output instead of letting the original
    /// stashed-extras copy survive. Returns <c>null</c> when there is
    /// genuinely nothing to emit so the writer can skip the block entirely.
    /// </summary>
    private static string BuildStoryDataBlock(Harlowe story)
    {
      var dict = story.StoryDataExtras != null
        ? new Dictionary<string, object>(story.StoryDataExtras)
        : new Dictionary<string, object>();

      ApplyTypedField(dict, "ifid", story.Ifid);
      ApplyTypedField(dict, "format", story.Format);
      ApplyTypedField(dict, "format-version", story.FormatVersion);

      var startPassage = story.GetStartPassage();
      ApplyTypedField(dict, "start", startPassage != null ? startPassage.Name : null);

      if (dict.Count == 0) return null;

      string json = new JsonWriter().Write(dict);
      return ":: StoryData\n" + json;
    }

    /// <summary>
    /// Indexer-assign when <paramref name="value"/> is set (preserves enumeration
    /// position for keys that originally came from <see cref="Harlowe.StoryDataExtras"/>);
    /// <see cref="Dictionary{TKey, TValue}.Remove(TKey)"/> when it's empty so a
    /// cleared typed field actually disappears from the JSON.
    /// </summary>
    private static void ApplyTypedField(Dictionary<string, object> dict, string key, string value)
    {
      if (string.IsNullOrEmpty(value)) dict.Remove(key);
      else dict[key] = value;
    }

    private static string BuildPassageBlock(HarlowePassage passage)
    {
      var sb = new StringBuilder();
      AppendHeader(sb, passage);
      string body = ResolveBody(passage);
      if (!string.IsNullOrEmpty(body))
      {
        sb.Append('\n');
        AppendEscapedBody(sb, body);
      }
      return sb.ToString();
    }

    /// <summary>
    /// Emits <c>:: Name</c> with optional <c>[tag1 tag2]</c> tag block (omitted
    /// when empty) and optional <c>{position-json}</c> block (preserved
    /// verbatim from the reader). Trailing whitespace inside the source name
    /// would have been trimmed by the reader, so we trust <see cref="HarlowePassage.Name"/>.
    /// </summary>
    private static void AppendHeader(StringBuilder sb, HarlowePassage passage)
    {
      sb.Append(":: ").Append(passage.Name);
      if (passage.Tags != null && passage.Tags.Count > 0)
      {
        sb.Append(" [");
        for (int i = 0; i < passage.Tags.Count; i++)
        {
          if (i > 0) sb.Append(' ');
          AppendEscapedTag(sb, passage.Tags[i], passage.Name);
        }
        sb.Append(']');
      }
      if (!string.IsNullOrEmpty(passage.Position))
      {
        sb.Append(' ').Append(passage.Position);
      }
    }

    /// <summary>
    /// Append a single Twee 3 tag, escaping <c>[</c>/<c>]</c>/<c>\</c> with a
    /// leading backslash so the reader can round-trip it. Whitespace inside a
    /// tag name is not expressible in the Twee 3 grammar (tags are
    /// whitespace-separated and the spec offers no escape for whitespace
    /// inside a name); we throw rather than silently corrupt the tag block.
    /// </summary>
    private static void AppendEscapedTag(StringBuilder sb, string tag, string passageName)
    {
      if (string.IsNullOrEmpty(tag)) return;
      for (int i = 0; i < tag.Length; i++)
      {
        char c = tag[i];
        if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
          throw new HarloweParseException(
            $"tag '{tag}' contains whitespace, which Twee 3 cannot represent",
            -1, -1, passageName);
        if (c == '\\' || c == '[' || c == ']') sb.Append('\\');
        sb.Append(c);
      }
    }

    /// <summary>
    /// Picks the body source for <paramref name="passage"/>: clean passages
    /// reuse <see cref="HarlowePassage.RawBody"/> (preserved verbatim except
    /// for the trailing-newline normalisation the reader applies, so inter-
    /// passage separation stays the writer's responsibility); dirty (or
    /// RawBody-less) passages run through <see cref="MarkupPrinter"/> in
    /// canonical form.
    ///
    /// <para>Loader-recovered passages whose entire AST is the synthetic
    /// <see cref="Ast.Body.ParseErrorNode"/> stub prefer
    /// <see cref="HarlowePassage.RawBody"/> regardless of <see cref="HarlowePassage.IsDirty"/>
    /// — the stub AST has no representable structure and re-canonicalizing
    /// would silently destroy the broken-but-recoverable original. When
    /// RawBody is missing (programmatic construction), the printer falls back
    /// to the source stashed on the ParseErrorNode itself.</para>
    ///
    /// <para>Passages with only <em>partial</em> parse-error recovery (a real
    /// AST that contains a ParseErrorNode among valid siblings) follow the
    /// normal IsDirty path so consumer-driven AST edits are emitted; the
    /// inline error node prints as its captured source (or empty, for
    /// per-node parser recoveries that have no source text).</para>
    /// </summary>
    private static string ResolveBody(HarlowePassage passage)
    {
      bool wholeStub = passage.Ast != null && Ast.Body.ParseErrorNode.IsWhollyParseError(passage.Ast);
      if (wholeStub && passage.RawBody != null) return passage.RawBody;
      if (!passage.IsDirty && passage.RawBody != null) return passage.RawBody;
      if (passage.Ast != null) return new MarkupPrinter().Print(passage.Ast);
      return passage.RawBody ?? string.Empty;
    }

    /// <summary>
    /// Walks <paramref name="body"/> a character at a time, prepending
    /// <c>\</c> to any <c>::</c> sequence that sits at the start of a line.
    /// The reader strips the leading <c>\</c> on parse, so this is a pure
    /// escape — the body content the user sees is unchanged.
    /// </summary>
    private static void AppendEscapedBody(StringBuilder sb, string body)
    {
      if (string.IsNullOrEmpty(body)) return;
      bool atLineStart = true;
      for (int i = 0; i < body.Length; i++)
      {
        char c = body[i];
        if (atLineStart && c == ':' && i + 1 < body.Length && body[i + 1] == ':')
        {
          sb.Append("\\::");
          i++;
          atLineStart = false;
          continue;
        }
        sb.Append(c);
        atLineStart = c == '\n';
      }
    }
  }
}
