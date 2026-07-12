using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(background: navy)[hook]</c> or <c>(background: "art/sky.png")[hook]</c>.
  /// Returns a <see cref="Changer"/> that sets either
  /// <see cref="StyleSpec.BackgroundColor"/> or
  /// <see cref="StyleSpec.BackgroundImage"/>. Registered under
  /// <c>background</c> and the alias <c>bg</c>.
  ///
  /// <para><b>Colour vs image.</b> A typed <see cref="HarloweValueKind.Colour"/>
  /// (a built-in name like <c>navy</c>, a hex literal, or the product of
  /// <c>(rgb:)</c>/<c>(hsl:)</c>) is a colour. A String is a colour only when it
  /// is hex-shaped (<c>"#a4e"</c>) or a CSS function call (<c>"rgb(0,0,255)"</c>);
  /// <em>every other string is an image URL</em>. This is reference's rule
  /// (<c>ts/macrolib/stylechangers.ts</c>: hex or <c>/^\s*(?:\w+)\(/</c> → colour,
  /// else "default to <c>url(value)</c>"), which means a named colour must be
  /// written bare — <c>(bg: blue)</c>, not <c>(bg: "blue")</c>, since the latter
  /// is an image path in reference too.</para>
  ///
  /// <para><b>Deliberate superset:</b> an author-written <c>url(...)</c> wrapper
  /// is unwrapped and treated as an image. Reference's <c>\w+\(</c> colour test
  /// catches <c>url(</c> and misroutes it to <c>background-color</c> (broken CSS
  /// either way), so accepting it costs no story fidelity and spares the author
  /// a silent failure.</para>
  /// </summary>
  public class BackgroundMacro : IMacro
  {
    private readonly string _name;

    public BackgroundMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;

      // A typed Colour is engine-generated from numeric components, so it
      // skips the author-string validator — no author text reaches the CSS.
      if (arg.Kind == HarloweValueKind.Colour)
        return HarloweValue.OfChanger(
          Changer.FromStyle(new StyleSpec { BackgroundColor = arg.AsColour.ToCssString() }));

      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a Colour or String, got {arg.Kind}");

      // Trim once up front. Incidental leading/trailing whitespace would
      // otherwise defeat the shape matchers below — e.g. `" url(art/sky.png) "`
      // would fail StartsWith("url(") and be treated as an image path with a
      // stray space. The trimmed form is what we both validate and store, so
      // the downstream emit doesn't carry the whitespace either.
      //
      // Parameterless Trim is deliberate. It strips all Unicode whitespace
      // (NBSP, ideographic space, etc.), matching reference Harlowe, which
      // tolerates the same set via regex `^\s*` (ECMAScript `\s` covers Unicode
      // Space_Separator chars). An ASCII-only Trim would make this macro
      // stricter than reference and silently misroute NBSP-padded image paths
      // from word-processor copy-pastes.
      var value = arg.AsString?.Trim();
      var invalid = StyleValueValidator.Validate(_name, value);
      if (invalid != null) return invalid;

      // Gradients need the Gradient value type (and a raw-CSS background
      // channel) that we don't have yet. Say so, rather than wrapping the
      // gradient in url(...) and emitting CSS the host silently drops.
      if (LooksLikeGradient(value))
        return HarloweValue.OfError(
          $"({_name}:) doesn't support gradient values yet — Gradient values and the (gradient:) macro aren't implemented");

      // Authors may write the CSS-shape ("url(art/sky.png)") rather than a bare
      // path. HtmlRenderOutput wraps BackgroundImage in url(...) when it emits,
      // so a value already wrapped would produce url(url(...)) — strip the CSS
      // wrapper here. An empty or malformed wrapper is an in-prose error rather
      // than silently broken CSS.
      if (StartsWithCssUrl(value))
      {
        if (!TryUnwrapCssUrl(value, out var url))
          return HarloweValue.OfError($"({_name}:) malformed url() value: '{value}'");
        if (string.IsNullOrWhiteSpace(url))
          return HarloweValue.OfError($"({_name}:) url() value is empty");
        return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { BackgroundImage = url }));
      }

      // Reference's colour tests: a hex string, or any CSS function call
      // (`rgb(…)`, `hsl(…)`, `color-mix(…)`). Everything else is an image.
      if (ColourValue.FromHex(value) != null || LooksLikeCssFunction(value))
        return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { BackgroundColor = value }));

      if (value.Length == 0)
        return HarloweValue.OfError($"({_name}:) value is empty");

      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { BackgroundImage = value }));
    }

    /// <summary>
    /// Reference's <c>/^\s*(?:\w+)\(/</c> colour test — word characters
    /// immediately followed by <c>(</c>. Already-trimmed input, so the leading
    /// whitespace allowance is moot. Note <c>linear-gradient(</c> does NOT match
    /// (the hyphen isn't a word char), which is why reference tests gradients
    /// separately and why <see cref="LooksLikeGradient"/> runs first here.
    /// </summary>
    private static bool LooksLikeCssFunction(string s)
    {
      int i = 0;
      while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
      return i > 0 && i < s.Length && s[i] == '(';
    }

    /// <summary>
    /// Reference's gradient test:
    /// <c>/^\s*(?:repeating-)?(?:linear|radial|conic)-gradient\(/</c>.
    /// </summary>
    private static bool LooksLikeGradient(string s)
    {
      var lower = s.ToLowerInvariant();
      if (lower.StartsWith("repeating-")) lower = lower.Substring("repeating-".Length);
      return lower.StartsWith("linear-gradient(")
          || lower.StartsWith("radial-gradient(")
          || lower.StartsWith("conic-gradient(");
    }

    /// <summary>
    /// Case-insensitive test for the four-char <c>url(</c> prefix. Cheap
    /// gate before the stricter <see cref="TryUnwrapCssUrl"/> call.
    /// </summary>
    private static bool StartsWithCssUrl(string s)
    {
      if (s.Length < 4) return false;
      return (s[0] == 'u' || s[0] == 'U')
          && (s[1] == 'r' || s[1] == 'R')
          && (s[2] == 'l' || s[2] == 'L')
          && s[3] == '(';
    }

    /// <summary>
    /// Strict <c>url(...)</c> unwrap. Returns true and sets <paramref name="url"/>
    /// to the inner URL only when the entire input matches the canonical CSS
    /// shape — the closing <c>)</c> must be the final character. Tolerates a
    /// single layer of paired quotes (<c>url("...")</c> / <c>url('...')</c>).
    ///
    /// <para>Returns false on trailing content after <c>)</c> (e.g.
    /// <c>url(x.png)evil</c>), on a missing close paren, or on mismatched
    /// quoting. The caller surfaces the rejection as an in-prose error rather
    /// than silently emitting double-wrapped <c>url(url(...)evil)</c> CSS.</para>
    /// </summary>
    private static bool TryUnwrapCssUrl(string s, out string url)
    {
      url = null;
      if (!StartsWithCssUrl(s)) return false;
      // Require the closing `)` to be the final character — any trailing
      // content means the value is not a clean CSS url() shape and the
      // unwrap is unsafe (downstream would double-wrap and emit broken CSS).
      if (s.Length < 5 || s[s.Length - 1] != ')') return false;
      var inner = s.Substring(4, s.Length - 5).Trim();
      if (inner.Length >= 2
          && ((inner[0] == '"' && inner[inner.Length - 1] == '"')
           || (inner[0] == '\'' && inner[inner.Length - 1] == '\'')))
        inner = inner.Substring(1, inner.Length - 2);
      url = inner;
      return true;
    }
  }
}
