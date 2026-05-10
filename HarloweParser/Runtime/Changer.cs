using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A composable styling primitive. Returned by changer macros like
  /// <c>(text-style: "bold")</c>; combined via <see cref="Compose"/> when the
  /// <c>+</c> operator joins two changers; applied by <see cref="BodyRenderer"/>
  /// when the changer sits in front of an attached hook.
  ///
  /// <para>Internally a Changer is a flat list of <c>(open, close)</c> HTML
  /// snippet pairs. <see cref="Apply"/> emits opens in list order, then asks
  /// the caller to render the hook contents, then emits closes in reverse —
  /// so the first wrapper in the list is the *outermost* layer in the rendered
  /// HTML. This matches the reading order of <c>A + B</c>: A wraps B wraps
  /// content. Composition is therefore concatenation of the wrapper lists.</para>
  ///
  /// <para>Changers stay opaque values — they don't render visibly on their
  /// own. <see cref="HarloweValue.ToHarloweString"/> returns an empty string
  /// for them, so <c>(print: (text-style: "bold"))</c> emits nothing rather
  /// than dumping internal state.</para>
  /// </summary>
  public class Changer
  {
    private readonly List<(string Open, string Close)> _wrappers;

    private Changer(List<(string, string)> wrappers)
    {
      _wrappers = wrappers;
    }

    /// <summary>
    /// Construct a changer with a single open/close wrapper pair. Most
    /// changer macros call this once with the HTML they want around the
    /// hook content (e.g. <c>("&lt;b&gt;", "&lt;/b&gt;")</c>).
    /// </summary>
    public static Changer FromHtml(string open, string close)
      => new Changer(new List<(string, string)> { (open ?? string.Empty, close ?? string.Empty) });

    /// <summary>
    /// Compose this changer with <paramref name="other"/>, producing a new
    /// changer whose wrapper list is <c>this</c>'s followed by
    /// <paramref name="other"/>'s. Reading order: <c>A + B</c> means A wraps
    /// B wraps content — matches the left-to-right authoring order in source.
    /// </summary>
    public Changer Compose(Changer other)
    {
      if (other == null) return this;
      var combined = new List<(string, string)>(_wrappers.Count + other._wrappers.Count);
      combined.AddRange(_wrappers);
      combined.AddRange(other._wrappers);
      return new Changer(combined);
    }

    /// <summary>
    /// Apply this changer to a hook: emit all open snippets in order through
    /// <paramref name="output"/>, invoke <paramref name="renderHook"/> to emit
    /// the hook's contents, then emit all close snippets in reverse order so
    /// the wrapping nests correctly.
    /// </summary>
    public void Apply(IRenderOutput output, System.Action renderHook)
    {
      for (int i = 0; i < _wrappers.Count; i++)
        output.Html(_wrappers[i].Open);
      renderHook?.Invoke();
      for (int i = _wrappers.Count - 1; i >= 0; i--)
        output.Html(_wrappers[i].Close);
    }

    /// <summary>
    /// Structural equality: two changers are equal iff their wrapper lists
    /// are equal pair-wise. Lets <see cref="HarloweValue.Equals"/> recurse
    /// into Changer values without special-casing.
    /// </summary>
    public override bool Equals(object obj)
    {
      if (!(obj is Changer other)) return false;
      if (_wrappers.Count != other._wrappers.Count) return false;
      for (int i = 0; i < _wrappers.Count; i++)
      {
        if (_wrappers[i].Open != other._wrappers[i].Open) return false;
        if (_wrappers[i].Close != other._wrappers[i].Close) return false;
      }
      return true;
    }

    public override int GetHashCode()
    {
      int h = _wrappers.Count;
      for (int i = 0; i < _wrappers.Count; i++)
      {
        h = (h * 397) ^ (_wrappers[i].Open?.GetHashCode() ?? 0);
        h = (h * 397) ^ (_wrappers[i].Close?.GetHashCode() ?? 0);
      }
      return h;
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
