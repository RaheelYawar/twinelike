namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A line break inside passage prose. Modelled as its own node (rather than
  /// being folded into <see cref="TextNode"/>) because Harlowe's renderer
  /// treats consecutive newlines as paragraph breaks and macros may suppress
  /// or rewrite them.
  /// </summary>
  public class NewlineNode : IBodyNode
  {
    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
