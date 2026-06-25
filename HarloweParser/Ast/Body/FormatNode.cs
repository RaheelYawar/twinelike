using System.Collections.Generic;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// Which inline text-formatting markup produced a <see cref="FormatNode"/>.
  /// Each maps to a primitive flag on the runtime <c>StyleSpec</c> — the same
  /// flags <c>(text-style: "bold"/"italic")</c> sets — so inline markup and the
  /// styling macro render through one style channel.
  /// </summary>
  public enum InlineFormat
  {
    /// <summary><c>''text''</c> — boldface.</summary>
    Bold,
    /// <summary><c>//text//</c> — italics.</summary>
    Italic,
    /// <summary><c>~~text~~</c> — strikethrough.</summary>
    Strike,
    /// <summary><c>^^text^^</c> — superscript.</summary>
    Superscript
  }

  /// <summary>
  /// An inline-formatted span of body content — <c>''bold''</c> or
  /// <c>//italic//</c>. Produced by the body parser when it folds a matching
  /// pair of <see cref="Tokens.TokenType.FormatDelimiter"/> tokens; the span's
  /// content (which may contain more markup, including nested formatting) is
  /// <see cref="Children"/>.
  ///
  /// <para>These mirror reference Harlowe's <c>bold</c>/<c>italic</c> markup
  /// tokens. They nest, but only symmetrically — <c>''//text//''</c> works,
  /// <c>''//text''//</c> does not (the parser's fold degrades the crossed
  /// delimiters to literal text). The renderer maps each to the corresponding
  /// <c>StyleSpec</c> flag and emits it through
  /// <c>IRenderOutput.PushStyle</c>/<c>PopStyle</c>, exactly as
  /// <c>(text-style:)</c> does.</para>
  /// </summary>
  public class FormatNode : IBodyNode
  {
    /// <summary>Which formatting this span applies.</summary>
    public InlineFormat Format;

    /// <summary>The body nodes inside the delimiters.</summary>
    public List<IBodyNode> Children;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }

  /// <summary>
  /// The single home for the <see cref="InlineFormat"/> ↔ markup-delimiter
  /// mapping, shared by the body parser (fold) and the Twee
  /// <c>MarkupPrinter</c>. The runtime <c>StyleSpec</c> mapping lives separately
  /// in the renderer (it can't depend on <c>Harlowe.Runtime</c> from here). Both
  /// switches are exhaustive with a throwing default, so adding the next markup
  /// type (<c>~~strike~~</c>, …) fails loudly at the first unmapped use rather
  /// than silently collapsing to italic.
  /// </summary>
  public static class InlineFormats
  {
    /// <summary>The symmetric markup delimiter that opens and closes this format (<c>''</c> / <c>//</c>).</summary>
    public static string Delimiter(InlineFormat format)
    {
      switch (format)
      {
        case InlineFormat.Bold: return "''";
        case InlineFormat.Italic: return "//";
        case InlineFormat.Strike: return "~~";
        case InlineFormat.Superscript: return "^^";
        default:
          throw new System.ArgumentOutOfRangeException(
            nameof(format), format, "no markup delimiter mapped for this InlineFormat");
      }
    }

    /// <summary>
    /// Maps a delimiter literal (<c>''</c> / <c>//</c>) to its format. Throws on
    /// an unrecognised delimiter — the tokenizer only ever emits mapped ones, so
    /// this is a developer invariant guard (unreachable from author input), not
    /// an in-prose error path.
    /// </summary>
    public static InlineFormat FromDelimiter(string delimiter)
    {
      switch (delimiter)
      {
        case "''": return InlineFormat.Bold;
        case "//": return InlineFormat.Italic;
        case "~~": return InlineFormat.Strike;
        case "^^": return InlineFormat.Superscript;
        default:
          throw new System.ArgumentException(
            $"no InlineFormat mapped for delimiter '{delimiter}'", nameof(delimiter));
      }
    }
  }
}
