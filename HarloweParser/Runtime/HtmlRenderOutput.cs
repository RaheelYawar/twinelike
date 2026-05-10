using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// <see cref="IRenderOutput"/> adapter that translates semantic
  /// <see cref="StyleSpec"/> events back to HTML. Wrap an inner output to
  /// produce browser-renderable markup; non-style channels (Text/Html/Link/
  /// Error) pass through unchanged so an inner buffer can capture the result.
  ///
  /// <para>Mapping rules:
  /// <list type="bullet">
  /// <item>Bold/Italic/Underline/Strikethrough emit the canonical inline tags
  /// (<c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/<c>&lt;u&gt;</c>/<c>&lt;s&gt;</c>) when
  /// no value fields are set on the same spec — concise output for the
  /// common case.</item>
  /// <item>Any value field present, or multiple flags set, collapses to a
  /// single <c>&lt;span style="..."&gt;</c> with all CSS properties packed in.</item>
  /// <item><see cref="PopStyle"/> closes the tags emitted by the matching
  /// <see cref="PushStyle"/>, in reverse order — so a stack of pushes nests
  /// correctly.</item>
  /// </list>
  /// </para>
  ///
  /// <para>User-supplied values (Color/BackgroundColor/FontFamily/FontSize)
  /// are HTML-attribute-escaped via <see cref="EscapeAttribute"/> before
  /// being embedded in the <c>style="..."</c> attribute, so a story variable
  /// holding <c>"red"; --></c> can't break out of the attribute.</para>
  /// </summary>
  public class HtmlRenderOutput : IRenderOutput
  {
    private readonly IRenderOutput _inner;
    private readonly Stack<string[]> _openTags = new Stack<string[]>();

    public HtmlRenderOutput(IRenderOutput inner)
    {
      _inner = inner;
    }

    public void Text(string content) => _inner.Text(content);
    public void Html(string rawHtml) => _inner.Html(rawHtml);
    public void Link(string text, string target) => _inner.Link(text, target);
    public void Error(string message) => _inner.Error(message);

    public void PushStyle(StyleSpec style)
    {
      var tags = TagsFor(style);
      _openTags.Push(tags);
      for (int i = 0; i < tags.Length; i++)
        _inner.Html(OpenTag(tags[i]));
    }

    public void PopStyle()
    {
      if (_openTags.Count == 0) return;
      var tags = _openTags.Pop();
      for (int i = tags.Length - 1; i >= 0; i--)
        _inner.Html(CloseTag(tags[i]));
    }

    /// <summary>
    /// Decide which HTML elements to emit for a spec. A flag-only spec uses
    /// the canonical short tags; anything involving a value field collapses
    /// to a single <c>span</c> carrying inline CSS. Combining multiple flags
    /// also routes through <c>span</c> for compactness when there's no value.
    /// </summary>
    private static string[] TagsFor(StyleSpec style)
    {
      if (style == null || style.IsEmpty) return new string[0];

      bool hasValue = style.Color != null || style.BackgroundColor != null
                   || style.FontFamily != null || style.FontSize != null;

      if (!hasValue)
      {
        var list = new List<string>(4);
        if (style.Bold) list.Add("b");
        if (style.Italic) list.Add("i");
        if (style.Underline) list.Add("u");
        if (style.Strikethrough) list.Add("s");
        return list.ToArray();
      }

      return new[] { BuildSpanTag(style) };
    }

    /// <summary>
    /// Build a span open tag with both font/decoration flags and value fields
    /// folded into a single <c>style="..."</c> attribute. Used when at least
    /// one value field is set; flags alone take the short-tag path.
    /// </summary>
    private static string BuildSpanTag(StyleSpec style)
    {
      var sb = new StringBuilder("span style=\"");
      bool first = true;
      void Append(string prop, string value)
      {
        if (!first) sb.Append(' ');
        sb.Append(prop).Append(": ").Append(EscapeAttribute(value)).Append(';');
        first = false;
      }
      if (style.Color != null) Append("color", style.Color);
      if (style.BackgroundColor != null) Append("background-color", style.BackgroundColor);
      if (style.FontFamily != null) Append("font-family", style.FontFamily);
      if (style.FontSize != null) Append("font-size", style.FontSize);
      if (style.Bold) Append("font-weight", "bold");
      if (style.Italic) Append("font-style", "italic");
      if (style.Underline || style.Strikethrough)
      {
        var dec = new StringBuilder();
        if (style.Underline) dec.Append("underline");
        if (style.Underline && style.Strikethrough) dec.Append(' ');
        if (style.Strikethrough) dec.Append("line-through");
        Append("text-decoration", dec.ToString());
      }
      sb.Append("\"");
      return sb.ToString();
    }

    private static string OpenTag(string tagDescriptor) => "<" + tagDescriptor + ">";

    private static string CloseTag(string tagDescriptor)
    {
      // For span tags the descriptor includes attributes; the close tag uses
      // just the element name.
      int sp = tagDescriptor.IndexOf(' ');
      string name = sp < 0 ? tagDescriptor : tagDescriptor.Substring(0, sp);
      return "</" + name + ">";
    }

    /// <summary>
    /// HTML-escape a user-supplied attribute value before embedding it in a
    /// <c>style="..."</c> attribute. Defensive against accidental injection
    /// when an author passes a story variable into a styling macro
    /// (<c>(text-color: $userInput)</c>). Escapes the five characters HTML
    /// recognises in attribute context.
    /// </summary>
    public static string EscapeAttribute(string value)
    {
      if (string.IsNullOrEmpty(value)) return string.Empty;
      var sb = new StringBuilder(value.Length);
      for (int i = 0; i < value.Length; i++)
      {
        char c = value[i];
        switch (c)
        {
          case '&': sb.Append("&amp;"); break;
          case '<': sb.Append("&lt;"); break;
          case '>': sb.Append("&gt;"); break;
          case '"': sb.Append("&quot;"); break;
          case '\'': sb.Append("&#39;"); break;
          default: sb.Append(c); break;
        }
      }
      return sb.ToString();
    }
  }
}
