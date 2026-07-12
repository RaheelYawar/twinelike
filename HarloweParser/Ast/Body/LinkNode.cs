namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A passage-to-passage link written with double-bracket syntax:
  /// <c>[[Just target]]</c>, <c>[[display text-&gt;target]]</c>, or
  /// <c>[[target&lt;-display text]]</c>. When <see cref="Text"/> is omitted in
  /// source, the parser sets it equal to <see cref="Target"/>.
  /// </summary>
  public class LinkNode : IBodyNode
  {
    /// <summary>The visible label shown to the player.</summary>
    public string Text;

    /// <summary>The name of the passage to navigate to when clicked.</summary>
    public string Target;

    /// <summary>
    /// 1-based line of the opening <c>[[</c> in the passage body, or <c>0</c>
    /// when unknown (a hand-built AST). Carried so a diagnostic can point the
    /// author at the link — see <see cref="Harlowe.GetBrokenLinks"/>.
    /// </summary>
    public int Line;

    /// <summary>1-based column of the opening <c>[[</c>; <c>0</c> when unknown.</summary>
    public int Column;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
