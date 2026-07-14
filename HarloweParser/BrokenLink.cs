namespace Harlowe
{
  /// <summary>
  /// One navigation reference whose target passage doesn't exist, as reported by
  /// <see cref="Harlowe.GetBrokenLinks"/> — either the <c>[[…]]</c> syntax or a
  /// literal passage name handed to a navigation macro.
  ///
  /// <para>Shaped for a host engine to <em>show a developer</em> at load: it
  /// carries where the reference is (<see cref="PassageName"/> +
  /// <see cref="Line"/>/<see cref="Column"/>), what it is
  /// (<see cref="IsLink"/> / <see cref="MacroName"/>), where it was trying to go
  /// (<see cref="Target"/>), and a ready-to-print <see cref="Message"/> — so the
  /// engine logs a line rather than assembling English of its own.</para>
  /// </summary>
  public class BrokenLink
  {
    /// <summary>The passage containing the reference.</summary>
    public string PassageName;

    /// <summary>The passage name it points at, which no passage answers to.</summary>
    public string Target;

    /// <summary>
    /// The link's display text — <c>Go</c> in <c>[[Go-&gt;Missing]]</c>.
    /// <c>null</c> for a macro reference, which has no label.
    /// </summary>
    public string LinkText;

    /// <summary>
    /// The macro that holds the reference (<c>goto</c>, <c>display</c>,
    /// <c>link-goto</c>, <c>click-goto</c>, …), or <c>null</c> when this came
    /// from the <c>[[…]]</c> syntax. See <see cref="IsLink"/>.
    /// </summary>
    public string MacroName;

    /// <summary>1-based line within the passage body; <c>0</c> when unknown (a hand-built AST).</summary>
    public int Line;

    /// <summary>1-based column within the line; <c>0</c> when unknown.</summary>
    public int Column;

    /// <summary>True for the <c>[[…]]</c> syntax, false for a macro reference.</summary>
    public bool IsLink => MacroName == null;

    /// <summary>
    /// A ready-to-display diagnostic — what a host engine logs straight to its
    /// console. Built here rather than at each call site so every consumer says
    /// the same thing, and so the phrasing can improve without touching them.
    /// </summary>
    public string Message
    {
      get
      {
        // The label is quoted as a label, not rebuilt as [[…]] syntax — a
        // reconstructed "[[Go]]" for source that reads [[Go->Missing]] is a
        // string the author can't find by searching their own file.
        string what = !IsLink ? "(" + MacroName + ":)"
          : string.IsNullOrEmpty(LinkText) ? "a link"
          : "the link '" + LinkText + "'";
        return "In passage '" + PassageName + "'" + SourcePosition.Suffix(Line, Column)
             + ": " + what + " points to the passage '" + Target + "', which doesn't exist.";
      }
    }

    public override string ToString() => Message;
  }
}
