namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A run of literal prose between markup. The renderer prints
  /// <see cref="Content"/> verbatim. HTML entities have already been decoded
  /// by the parser before this node is constructed.
  /// </summary>
  public class TextNode : IBodyNode
  {
    public string Content;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
