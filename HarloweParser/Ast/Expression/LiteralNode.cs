namespace Harlowe.Ast.Expression
{
  public class LiteralNode : IExpressionNode
  {
    public LiteralKind Kind;
    public object Value;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
