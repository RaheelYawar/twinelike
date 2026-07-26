namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A two-operand operator expression, e.g. <c>$hp + 5</c>, <c>$x is "yes"</c>,
  /// <c>$found and $brave</c>, or assignment-style <c>$x to 1</c>. The exact
  /// operator surface (arithmetic, comparison, logical, Harlowe-specific
  /// keywords like <c>to</c>, <c>into</c>, <c>contains</c>) is captured as a
  /// string so the evaluator can dispatch without a separate enum.
  /// </summary>
  public class BinaryOpNode : IExpressionNode
  {
    /// <summary>
    /// The canonical operator the evaluator dispatches on. Spellings that mean
    /// the same operator are folded here — <c>is an</c> arrives as <c>is a</c>.
    /// </summary>
    public string Operator;

    /// <summary>
    /// The spelling the author actually wrote, when it differs from
    /// <see cref="Operator"/>; null otherwise. Only the markup printer reads
    /// it, so a reserialized passage keeps <c>is an array</c> rather than
    /// emitting the ungrammatical <c>is a array</c>. Null on a hand-built AST,
    /// which simply prints the canonical form.
    /// </summary>
    public string SourceOperator;

    public IExpressionNode Left;
    public IExpressionNode Right;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
