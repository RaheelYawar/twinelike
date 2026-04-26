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
    public string Operator;
    public IExpressionNode Left;
    public IExpressionNode Right;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
