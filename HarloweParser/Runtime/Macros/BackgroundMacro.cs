using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(background: "navy")[hook]</c> or <c>(background: "art/sky.png")[hook]</c>.
  /// Returns a <see cref="Changer"/> that sets either
  /// <see cref="StyleSpec.BackgroundColor"/> or
  /// <see cref="StyleSpec.BackgroundImage"/> depending on the shape of the
  /// argument string. Registered under <c>background</c> and the alias
  /// <c>bg</c>.
  ///
  /// <para>The image vs. colour distinction is a heuristic on the string —
  /// values that look like image references (ending in a common image
  /// extension, starting with <c>http://</c>/<c>https://</c>/<c>data:image/</c>,
  /// or wrapped in <c>url(...)</c>) are treated as images; anything else as a
  /// colour. The reference impl has a typed <c>Colour</c> value; we make do
  /// with a string heuristic until/unless a typed colour value lands.</para>
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
      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a String, got {arg.Kind}");

      // Trim once up front. Incidental leading/trailing whitespace would
      // otherwise defeat the LooksLikeImage prefix/suffix matchers — e.g.
      // `" url(art/sky.png) "` would fail StartsWith("url(") and fall into
      // the BackgroundColor branch, emitting `background-color: <url>` which
      // browsers silently drop. The trimmed form is what we both validate and
      // store, so the downstream emit doesn't carry stray whitespace either.
      //
      // Parameterless Trim is deliberate. It strips all Unicode whitespace
      // (NBSP, ideographic space, etc.), matching reference Harlowe's
      // ts/macrolib/stylechangers.ts which tolerates the same set via regex
      // `^\s*` (ECMAScript `\s` covers Unicode Space_Separator chars). An
      // ASCII-only Trim would make this macro stricter than reference and
      // silently misroute NBSP-padded image paths from word-processor
      // copy-pastes to the BackgroundColor branch.
      var value = arg.AsString?.Trim();
      var invalid = StyleValueValidator.Validate(_name, value);
      if (invalid != null) return invalid;
      if (LooksLikeImage(value))
      {
        // Authors may write either a bare URL ("art/sky.png") or the CSS-shape
        // ("url(art/sky.png)"). HtmlRenderOutput wraps BackgroundImage in
        // url(...) when it emits, so a value already wrapped in url() would
        // produce url(url(...)) — strip the CSS wrapper here. An empty inner
        // URL is rejected with an in-prose error rather than emitting silently
        // malformed CSS.
        if (StartsWithCssUrl(value))
        {
          if (!TryUnwrapCssUrl(value, out var url))
            return HarloweValue.OfError($"({_name}:) malformed url() value: '{value}'");
          if (string.IsNullOrWhiteSpace(url))
            return HarloweValue.OfError($"({_name}:) url() value is empty");
          return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { BackgroundImage = url }));
        }
        return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { BackgroundImage = value }));
      }
      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { BackgroundColor = value }));
    }

    private static bool LooksLikeImage(string s)
    {
      if (string.IsNullOrEmpty(s)) return false;
      var lower = s.ToLowerInvariant();
      if (lower.StartsWith("http://") || lower.StartsWith("https://")
       || lower.StartsWith("data:image/") || lower.StartsWith("url("))
        return true;
      return lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")
          || lower.EndsWith(".gif") || lower.EndsWith(".svg") || lower.EndsWith(".webp")
          || lower.EndsWith(".bmp");
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
