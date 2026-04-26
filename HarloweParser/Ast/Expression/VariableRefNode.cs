namespace Harlowe.Ast.Expression
{
  public class VariableRefNode : IExpressionNode
  {
    public string Name;
    public bool IsTemporary;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
