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
  /// reformatting, byte-for-byte preservation of the source as it was read.
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
    /// shallow copy of <see cref="Harlowe.StoryDataExtras"/>. Returns
    /// <c>null</c> when there is genuinely nothing to emit (no extras, no
    /// typed fields, no resolvable start passage) so the writer can skip the
    /// block entirely.
    /// </summary>
    private static string BuildStoryDataBlock(Harlowe story)
    {
      var dict = story.StoryDataExtras != null
        ? new Dictionary<string, object>(story.StoryDataExtras)
        : new Dictionary<string, object>();

      if (!string.IsNullOrEmpty(story.Ifid)) dict["ifid"] = story.Ifid;
      if (!string.IsNullOrEmpty(story.Format)) dict["format"] = story.Format;
      if (!string.IsNullOrEmpty(story.FormatVersion)) dict["format-version"] = story.FormatVersion;

      var startPassage = story.GetStartPassage();
      if (startPassage != null) dict["start"] = startPassage.Name;

      if (dict.Count == 0) return null;

      string json = new JsonWriter().Write(dict);
      return ":: StoryData\n" + json;
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
          sb.Append(passage.Tags[i]);
        }
        sb.Append(']');
      }
      if (!string.IsNullOrEmpty(passage.Position))
      {
        sb.Append(' ').Append(passage.Position);
      }
    }

    /// <summary>
    /// Picks the body source for <paramref name="passage"/>: clean passages
    /// reuse <see cref="HarlowePassage.RawBody"/> for byte-preserving output;
    /// dirty (or RawBody-less) passages run through <see cref="MarkupPrinter"/>
    /// in canonical form.
    /// </summary>
    private static string ResolveBody(HarlowePassage passage)
    {
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
