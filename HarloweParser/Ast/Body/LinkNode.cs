namespace Harlowe.Ast.Body
{
  public class LinkNode : IBodyNode
  {
    public string Text;
    public string Target;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
