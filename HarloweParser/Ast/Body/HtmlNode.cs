namespace Harlowe.Ast.Body
{
  public class HtmlNode : IBodyNode
  {
    public string RawHtml;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
