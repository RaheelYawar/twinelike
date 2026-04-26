namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A read of a variable inside an expression, e.g. the <c>$brave</c> in
  /// <c>(if: $brave)</c>. Distinct from <see cref="Body.VariableNode"/>, which
  /// is the *interpolation* of a variable into rendered prose.
  /// </summary>
  public class VariableRefNode : IExpressionNode
  {
    /// <summary>The variable name without its sigil (no leading <c>$</c> or <c>_</c>).</summary>
    public string Name;

    /// <summary>True if this is a temporary (passage-scoped) <c>_</c> variable.</summary>
    public bool IsTemporary;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
