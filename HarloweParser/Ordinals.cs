namespace Harlowe
{
  /// <summary>
  /// Parses an ordinal accessor name (<c>last</c>, <c>1st</c>, <c>2nd</c>,
  /// <c>2ndlast</c>, …) into a 1-based index and a from-the-end flag. The
  /// single vocabulary shared by expression evaluation (array/string property
  /// reads and writes) and the parser's <c>?hook's 1st</c> narrowing fold, so
  /// the recognised forms can't drift apart. The
  /// <c>st</c>/<c>nd</c>/<c>rd</c>/<c>th</c> suffix is decorative — <c>2st</c>
  /// and <c>1nd</c> still parse — matching Harlowe's permissive author-facing
  /// behaviour.
  /// </summary>
  internal static class Ordinals
  {
    /// <summary>
    /// Recognised forms: <c>last</c> (index=1, fromEnd=true), <c>Nth</c>
    /// (forward indexing), and <c>Nthlast</c> (back-anchored). Returns false
    /// for any other name so the caller can report an unknown property.
    /// </summary>
    public static bool TryParse(string name, out int index, out bool fromEnd)
    {
      index = 0;
      fromEnd = false;
      if (string.IsNullOrEmpty(name)) return false;
      if (name == "last") { index = 1; fromEnd = true; return true; }
      int p = 0;
      while (p < name.Length && char.IsDigit(name[p])) p++;
      if (p == 0) return false;
      if (p + 2 > name.Length) return false;
      string suffix = name.Substring(p, 2);
      if (suffix != "st" && suffix != "nd" && suffix != "rd" && suffix != "th") return false;
      int after = p + 2;
      if (!int.TryParse(name.Substring(0, p), out int n)) return false;
      if (after == name.Length) { index = n; fromEnd = false; return true; }
      if (after + 4 == name.Length && name.Substring(after, 4) == "last")
      { index = n; fromEnd = true; return true; }
      return false;
    }
  }
}
