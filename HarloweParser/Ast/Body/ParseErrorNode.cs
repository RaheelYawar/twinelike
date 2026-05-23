namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A synthetic body node carrying a parse-error message. The HTML and Twee
  /// loaders emit one of these in place of a passage's real AST when
  /// tokenize/parse throws, so a single broken passage doesn't abort the whole
  /// story load — the rest of the story renders normally and the broken
  /// passage produces an in-prose error at render time.
  ///
  /// <para>Lives in the body AST namespace so existing visitors (renderer,
  /// branch collector, printer) get a uniform dispatch point. The body parser
  /// never produces this node — only the loader recovery paths do.</para>
  /// </summary>
  public class ParseErrorNode : IBodyNode
  {
    public string Message;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
