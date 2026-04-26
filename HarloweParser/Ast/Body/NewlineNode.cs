namespace Harlowe.Ast.Body
{
  public class NewlineNode : IBodyNode
  {
    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
