namespace Harlowe.Ast.Body
{
  /// <summary>A line of three or more hyphens, rendered as <c>&lt;hr&gt;</c> (reference's <c>hr</c> token).</summary>
  public class HorizontalRuleNode : IBodyNode
  {
    /// <summary>The line as written, including the line break it consumes. Null on a hand-built node.</summary>
    public string OriginalSource;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
