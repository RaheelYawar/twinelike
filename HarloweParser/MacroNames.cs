using System.Text;

namespace Harlowe
{
  /// <summary>
  /// Macro-name normalization. Reference Harlowe treats macro names as
  /// case-insensitive AND dash/underscore-insensitive (the
  /// <c>insensitiveName</c> helper in <c>ts/utils.ts</c>:
  /// <c>e.toLowerCase().replace(/-|_/g, "")</c>), so <c>(go-to:)</c>,
  /// <c>(goto:)</c>, <c>(GoTo:)</c> and <c>(go_to:)</c> are one and the same
  /// macro. Registration and every macro-name comparison run through this, so
  /// the author's spelling is free while the AST keeps the original text for
  /// round-trip serialization.
  /// </summary>
  public static class MacroNames
  {
    /// <summary>Lower-cases (invariant) and strips <c>-</c>/<c>_</c> from a macro name.</summary>
    public static string Normalize(string name)
    {
      if (string.IsNullOrEmpty(name)) return name ?? string.Empty;
      var sb = new StringBuilder(name.Length);
      for (int i = 0; i < name.Length; i++)
      {
        char c = name[i];
        if (c == '-' || c == '_') continue;
        // Invariant lower-case: macro names are ASCII, and a culture-sensitive
        // ToLower() would map ASCII 'I' to dotless 'ı' under tr-TR.
        sb.Append(char.ToLowerInvariant(c));
      }
      return sb.ToString();
    }

    /// <summary>
    /// True for a call to a custom macro held in a variable, <c>($name:)</c> or
    /// <c>(_name:)</c>: the leading sigil is what reference's runner tests
    /// (<c>macroNameToken.text.startsWith</c> in <c>ts/twinescript/runner.ts</c>).
    /// Check this before <see cref="Normalize"/>, which would strip a leading
    /// underscore and turn <c>(_a:)</c> into a call to <c>(a:)</c>.
    /// </summary>
    /// <summary>
    /// True when two names are the same under <see cref="Normalize"/>, without
    /// building either: reference's <c>insensitiveName</c>, which also decides
    /// which hook a <c>?name</c> means.
    /// </summary>
    internal static bool SameName(string a, string b)
    {
      a = a ?? string.Empty;
      b = b ?? string.Empty;
      int i = 0, j = 0;
      while (true)
      {
        while (i < a.Length && (a[i] == '-' || a[i] == '_')) i++;
        while (j < b.Length && (b[j] == '-' || b[j] == '_')) j++;
        if (i == a.Length || j == b.Length) return i == a.Length && j == b.Length;
        if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[j])) return false;
        i++;
        j++;
      }
    }

    public static bool IsCustomCall(string name)
      => !string.IsNullOrEmpty(name) && (name[0] == '$' || name[0] == '_');
  }
}
