namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A comment the author excluded from the rendered passage: a <c>--</c>
  /// marker plus the one construct it eliminated (reference Harlowe 4.0's
  /// <c>comment</c> markup — a prose run, a comment hook <c>--[…]</c>, one
  /// macro call, etc.), or a whole <c>&lt;!-- … --&gt;</c> HTML comment.
  /// Renders as nothing. <see cref="OriginalSource"/> holds the full source
  /// span (marker and skipped content included) so <see cref="Twee.MarkupPrinter"/>
  /// re-canonicalizes a dirty passage without losing the author's notes; it is
  /// null when the AST was parsed without source text, in which case the
  /// printer emits nothing.
  /// </summary>
  public class CommentNode : IBodyNode
  {
    public string OriginalSource;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
