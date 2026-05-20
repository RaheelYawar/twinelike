using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// <see cref="IRenderOutput"/> adapter that translates semantic
  /// <see cref="StyleSpec"/> events back to HTML. Wrap an inner output to
  /// produce browser-renderable markup. <see cref="Text"/> HTML-escapes its
  /// content before forwarding because the surrounding stream is HTML — raw
  /// <c>&lt;</c>/<c>&gt;</c>/<c>&amp;</c> in story prose would otherwise be
  /// eaten or interpreted by the browser, and a value-bearing variable could
  /// inject markup. <see cref="Html"/> is the raw-passthrough channel for
  /// author-embedded HTML and stays unescaped. <see cref="Link"/> and
  /// <see cref="Error"/> are typed entries on the inner output (not HTML
  /// fragments) and forward unchanged.
  ///
  /// <para>Mapping rules:
  /// <list type="bullet">
  /// <item>Bold/Italic/Underline/Strikethrough emit the canonical inline tags
  /// (<c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/<c>&lt;u&gt;</c>/<c>&lt;s&gt;</c>) when
  /// they are the only fields set on the spec — concise output for the
  /// common case.</item>
  /// <item>Any value field, opacity, alignment, or effect present collapses to
  /// a single <c>&lt;span style="..."&gt;</c> with all CSS properties packed
  /// in; primitive flags fold into the same span when this happens.</item>
  /// <item><see cref="PopStyle"/> closes the tags emitted by the matching
  /// <see cref="PushStyle"/>, in reverse order — so a stack of pushes nests
  /// correctly.</item>
  /// </list>
  /// </para>
  ///
  /// <para>User-supplied values (Color/BackgroundColor/BackgroundImage/
  /// FontFamily/FontSize) are HTML-attribute-escaped via
  /// <see cref="EscapeAttribute"/> before being embedded into the
  /// <c>style="..."</c> attribute, so a story variable holding
  /// <c>"red"; --&gt;</c> can't break out of the attribute.</para>
  ///
  /// <para><b>Animation effects</b> (<c>blink</c>, <c>fade-in-out</c>,
  /// <c>shudder</c>, <c>rumble</c>, <c>sway</c>, <c>buoy</c>, <c>fidget</c>)
  /// emit <c>animation: harlowe-&lt;name&gt; ...;</c> declarations. The web
  /// consumer is expected to define matching <c>@keyframes</c> rules
  /// (<c>harlowe-blink</c>, <c>harlowe-shudder</c>, etc.) in their own
  /// stylesheet; this adapter does not inject them.</para>
  /// </summary>
  public class HtmlRenderOutput : IRenderOutput
  {
    private readonly IRenderOutput _inner;
    private readonly Stack<string[]> _openTags = new Stack<string[]>();

    public HtmlRenderOutput(IRenderOutput inner)
    {
      _inner = inner;
    }

    public void Text(string content) => _inner.Text(EscapeText(content));
    public void Html(string rawHtml) => _inner.Html(rawHtml);
    public void Link(string text, string target) => _inner.Link(text, target);
    public void Error(string message) => _inner.Error(message);

    /// <summary>
    /// Map an interactive region to an HTML <c>&lt;a&gt;</c> anchor with the
    /// region id stamped onto <c>data-region-id</c> and the interaction kind
    /// on <c>data-interaction</c>. The kind is exposed verbatim so a script
    /// can dispatch the right session event. The default <c>href</c> of
    /// <c>"#"</c> keeps the anchor clickable without a configured target.
    /// </summary>
    public void BeginInteractive(InteractiveRegion region)
    {
      if (region == null) { _inner.Html("<a>"); return; }
      var id = EscapeAttribute(region.Id ?? string.Empty);
      var kind = EscapeAttribute(region.Kind.ToString());
      _inner.Html("<a href=\"#\" data-region-id=\"" + id + "\" data-interaction=\"" + kind + "\">");
    }

    public void EndInteractive() => _inner.Html("</a>");

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
    /// the canonical short tags; anything else (value field, opacity,
    /// alignment, effect) collapses to a single <c>span</c> carrying inline
    /// CSS — primitive flags fold into the same span in that case.
    /// </summary>
    private static string[] TagsFor(StyleSpec style)
    {
      if (style == null || style.IsEmpty) return new string[0];

      bool hasNonFlag =
           style.Color != null || style.BackgroundColor != null
        || style.BackgroundImage != null || style.FontFamily != null
        || style.FontSize != null || style.Opacity != null
        || style.Alignment != null
        || (style.Effects != null && style.Effects.Count > 0);

      if (!hasNonFlag)
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
    /// Build a span open tag with every flag, value field, opacity, alignment,
    /// and effect folded into a single <c>style="..."</c> attribute. Used when
    /// at least one non-flag is set; flags-only specs take the short-tag path.
    /// Effect CSS recipes are inlined here — keep them in sync with the
    /// <see cref="TextEffect"/> set.
    /// </summary>
    private static string BuildSpanTag(StyleSpec style)
    {
      var sb = new StringBuilder("span style=\"");
      bool first = true;

      if (style.Color != null) AppendCss(sb, ref first, "color", style.Color, preEscaped: false);
      if (style.BackgroundColor != null) AppendCss(sb, ref first, "background-color", style.BackgroundColor, preEscaped: false);
      if (style.BackgroundImage != null)
      {
        AppendCss(sb, ref first, "background-image", "url(" + EscapeAttribute(style.BackgroundImage) + ")", preEscaped: true);
        AppendCss(sb, ref first, "background-size", "cover", preEscaped: true);
      }
      if (style.FontFamily != null) AppendCss(sb, ref first, "font-family", style.FontFamily, preEscaped: false);
      if (style.FontSize != null) AppendCss(sb, ref first, "font-size", style.FontSize, preEscaped: false);
      if (style.Opacity != null) AppendCss(sb, ref first, "opacity", style.Opacity.Value.ToString("R", CultureInfo.InvariantCulture), preEscaped: true);
      if (style.Alignment != null) AppendCss(sb, ref first, "text-align", CssAlignment(style.Alignment.Value), preEscaped: true);
      if (style.Bold) AppendCss(sb, ref first, "font-weight", "bold", preEscaped: true);
      if (style.Italic) AppendCss(sb, ref first, "font-style", "italic", preEscaped: true);
      if (style.Underline || style.Strikethrough)
      {
        var dec = new StringBuilder();
        if (style.Underline) dec.Append("underline");
        if (style.Underline && style.Strikethrough) dec.Append(' ');
        if (style.Strikethrough) dec.Append("line-through");
        AppendCss(sb, ref first, "text-decoration", dec.ToString(), preEscaped: true);
      }
      if (style.Effects != null)
      {
        for (int i = 0; i < style.Effects.Count; i++)
          AppendEffect(sb, ref first, style.Effects[i]);
      }
      sb.Append("\"");
      return sb.ToString();
    }

    private static void AppendCss(StringBuilder sb, ref bool first, string prop, string value, bool preEscaped)
    {
      if (!first) sb.Append(' ');
      sb.Append(prop).Append(": ").Append(preEscaped ? value : EscapeAttribute(value)).Append(';');
      first = false;
    }

    private static string CssAlignment(TextAlignment a)
    {
      switch (a)
      {
        case TextAlignment.Left: return "left";
        case TextAlignment.Center: return "center";
        case TextAlignment.Right: return "right";
        case TextAlignment.Justify: return "justify";
      }
      return "left";
    }

    /// <summary>
    /// Inline CSS recipe for one <see cref="TextEffect"/>. Recipes match the
    /// reference Harlowe rendering where reasonable; animation effects assume
    /// the consumer has defined matching <c>@keyframes harlowe-*</c> rules.
    /// Each recipe is its own helper rather than a table to keep the
    /// per-effect notes legible — these recipes will get tuned as authors
    /// hit edge cases.
    /// </summary>
    private static void AppendEffect(StringBuilder sb, ref bool first, TextEffect effect)
    {
      switch (effect)
      {
        case TextEffect.Mark:
          AppendCss(sb, ref first, "background-color", "hsla(50, 100%, 50%, 0.5)", preEscaped: true);
          break;
        case TextEffect.Superscript:
          AppendCss(sb, ref first, "vertical-align", "super", preEscaped: true);
          AppendCss(sb, ref first, "font-size", "0.83em", preEscaped: true);
          break;
        case TextEffect.Subscript:
          AppendCss(sb, ref first, "vertical-align", "sub", preEscaped: true);
          AppendCss(sb, ref first, "font-size", "0.83em", preEscaped: true);
          break;
        case TextEffect.Condense:
          AppendCss(sb, ref first, "letter-spacing", "-0.08em", preEscaped: true);
          break;
        case TextEffect.Expand:
          AppendCss(sb, ref first, "letter-spacing", "0.08em", preEscaped: true);
          break;
        case TextEffect.Outline:
          AppendCss(sb, ref first, "text-shadow",
              "-1px -1px 0 #fff, 1px -1px 0 #fff, -1px 1px 0 #fff, 1px 1px 0 #fff", preEscaped: true);
          break;
        case TextEffect.Shadow:
          AppendCss(sb, ref first, "text-shadow", "0.08em 0.08em 0.08em #000", preEscaped: true);
          break;
        case TextEffect.Emboss:
          AppendCss(sb, ref first, "text-shadow", "0 1px 0 #fff, 0 -1px 0 #000", preEscaped: true);
          break;
        case TextEffect.Smear:
          AppendCss(sb, ref first, "text-shadow", "0 0 0.02em #000, 0 0 0.08em currentColor", preEscaped: true);
          AppendCss(sb, ref first, "color", "transparent", preEscaped: true);
          break;
        case TextEffect.Blur:
          AppendCss(sb, ref first, "color", "transparent", preEscaped: true);
          AppendCss(sb, ref first, "text-shadow", "0 0 0.08em currentColor", preEscaped: true);
          break;
        case TextEffect.Blurrier:
          AppendCss(sb, ref first, "color", "transparent", preEscaped: true);
          AppendCss(sb, ref first, "text-shadow", "0 0 0.2em currentColor", preEscaped: true);
          AppendCss(sb, ref first, "user-select", "none", preEscaped: true);
          break;
        case TextEffect.Mirror:
          AppendCss(sb, ref first, "display", "inline-block", preEscaped: true);
          AppendCss(sb, ref first, "transform", "scaleX(-1)", preEscaped: true);
          break;
        case TextEffect.UpsideDown:
          AppendCss(sb, ref first, "display", "inline-block", preEscaped: true);
          AppendCss(sb, ref first, "transform", "scaleY(-1)", preEscaped: true);
          break;
        case TextEffect.Blink:
          AppendCss(sb, ref first, "animation", "harlowe-blink 1s steps(2) infinite", preEscaped: true);
          break;
        case TextEffect.FadeInOut:
          AppendCss(sb, ref first, "animation", "harlowe-fade-in-out 2s ease-in-out infinite alternate", preEscaped: true);
          break;
        case TextEffect.Shudder:
          AppendCss(sb, ref first, "display", "inline-block", preEscaped: true);
          AppendCss(sb, ref first, "animation", "harlowe-shudder 0.1s linear infinite", preEscaped: true);
          break;
        case TextEffect.Rumble:
          AppendCss(sb, ref first, "display", "inline-block", preEscaped: true);
          AppendCss(sb, ref first, "animation", "harlowe-rumble 0.1s linear infinite", preEscaped: true);
          break;
        case TextEffect.Sway:
          AppendCss(sb, ref first, "display", "inline-block", preEscaped: true);
          AppendCss(sb, ref first, "animation", "harlowe-sway 4s ease-in-out infinite", preEscaped: true);
          break;
        case TextEffect.Buoy:
          AppendCss(sb, ref first, "display", "inline-block", preEscaped: true);
          AppendCss(sb, ref first, "animation", "harlowe-buoy 2s ease-in-out infinite", preEscaped: true);
          break;
        case TextEffect.Fidget:
          AppendCss(sb, ref first, "display", "inline-block", preEscaped: true);
          AppendCss(sb, ref first, "animation", "harlowe-fidget 4s linear infinite", preEscaped: true);
          break;
      }
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
    /// HTML-escape prose for text-context output. Only the three characters
    /// that have special meaning between tags need replacing — quote escaping
    /// is reserved for the attribute helper.
    /// </summary>
    private static string EscapeText(string value)
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
          default: sb.Append(c); break;
        }
      }
      return sb.ToString();
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
