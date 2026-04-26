namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A raw HTML fragment that appeared in the passage source. Harlowe permits
  /// inline HTML; the parser preserves it verbatim so the host renderer can
  /// pass it straight to a browser/UI layer that understands it.
  /// </summary>
  public class HtmlNode : IBodyNode
  {
    public string RawHtml;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
