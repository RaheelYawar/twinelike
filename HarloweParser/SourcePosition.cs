using System.Globalization;

namespace Harlowe
{
  /// <summary>
  /// The two shapes a 1-based source position renders in, shared so every
  /// diagnostic prints it identically (culture-invariant). A line of <c>0</c>
  /// means "unknown" and formats as the empty string.
  /// </summary>
  internal static class SourcePosition
  {
    /// <summary>The <c> (line N, column N)</c> suffix used by <see cref="BrokenLink"/>/<see cref="ParseError"/> messages.</summary>
    internal static string Suffix(int line, int column)
    {
      if (line <= 0) return string.Empty;
      return " (line " + line.ToString(CultureInfo.InvariantCulture)
           + ", column " + column.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The <c> at line N, column N</c> clause used by rendered parse-error prose.</summary>
    internal static string AtClause(int line, int column)
    {
      if (line <= 0) return string.Empty;
      return " at line " + line.ToString(CultureInfo.InvariantCulture)
           + ", column " + column.ToString(CultureInfo.InvariantCulture);
    }
  }
}
