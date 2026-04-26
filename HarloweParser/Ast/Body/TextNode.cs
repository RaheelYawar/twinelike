namespace Harlowe.Ast.Body
{
  public class TextNode : IBodyNode
  {
    public string Content;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
