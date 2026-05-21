namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Validates author-supplied strings before they land in a value-bearing
  /// <see cref="StyleSpec"/> field (Color, BackgroundColor, BackgroundImage,
  /// FontFamily, FontSize). Rejects characters that have structural meaning in
  /// CSS — <c>;</c> (declaration separator), <c>{</c> / <c>}</c> (rule blocks),
  /// <c>\</c> (CSS string escapes), newline characters — to keep a story
  /// variable from injecting extra declarations into the
  /// <c>style="..."</c> attribute the HTML adapter emits.
  ///
  /// <para>Macro layer is the right place to reject: it gives the author a
  /// clear in-prose error pointing at the offending macro, instead of silently
  /// emitting bad CSS or stripping characters the author meant to use. Macros
  /// that accept arbitrary strings (<c>(text-color:)</c>, <c>(background:)</c>,
  /// <c>(font:)</c>) call <see cref="Validate"/>; macros taking typed values
  /// (<c>(text-size:)</c> from a Number, <c>(opacity:)</c>, <c>(align:)</c>)
  /// don't need it.</para>
  /// </summary>
  internal static class StyleValueValidator
  {
    /// <summary>
    /// Returns null when <paramref name="value"/> is safe to embed in a CSS
    /// declaration; otherwise returns a ready-to-return error
    /// <see cref="HarloweValue"/> mentioning the macro name and the offending
    /// character (so the in-prose message tells the author what to fix).
    /// Returning the error value (rather than throwing) keeps the macro on the
    /// project's in-prose error contract.
    /// </summary>
    public static HarloweValue Validate(string macroName, string value)
    {
      if (value == null) return null;
      for (int i = 0; i < value.Length; i++)
      {
        char c = value[i];
        if (c == ';' || c == '{' || c == '}' || c == '\\' || c == '\n' || c == '\r')
        {
          return HarloweValue.OfError(
            $"({macroName}:) value contains a character that isn't allowed in a style value: '{Describe(c)}'");
        }
      }
      return null;
    }

    private static string Describe(char c)
    {
      switch (c)
      {
        case '\n': return "\\n";
        case '\r': return "\\r";
        default: return c.ToString();
      }
    }
  }
}
