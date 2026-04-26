namespace Harlowe.Ast.Expression
{
  public class BinaryOpNode : IExpressionNode
  {
    public string Operator;
    public IExpressionNode Left;
    public IExpressionNode Right;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
