namespace Harlowe.Ast.Expression
{
  public class UnaryOpNode : IExpressionNode
  {
    public string Operator;
    public IExpressionNode Operand;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
