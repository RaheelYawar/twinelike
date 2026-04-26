namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A single-operand operator expression, e.g. <c>not $found</c> or <c>-$hp</c>.
  /// </summary>
  public class UnaryOpNode : IExpressionNode
  {
    public string Operator;
    public IExpressionNode Operand;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
