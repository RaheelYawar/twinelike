namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A synthetic body node carrying a parse-error message. The HTML and Twee
  /// loaders (and <see cref="Harlowe.AddPassage"/>) emit one of these in place
  /// of a passage's real AST when tokenize/parse throws, so a single broken
  /// passage doesn't abort the whole story load — the rest of the story
  /// renders normally and the broken passage produces an in-prose error at
  /// render time.
  ///
  /// <para>Lives in the body AST namespace so existing visitors (renderer,
  /// branch collector, printer) get a uniform dispatch point. The body parser
  /// never produces this node — only the loader recovery paths do.</para>
  /// </summary>
  public class ParseErrorNode : IBodyNode
  {
    public string Message;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);

    /// <summary>
    /// True if <paramref name="body"/> contains a <see cref="ParseErrorNode"/>
    /// at the top level. Loader recovery wraps a synthetic AST around a single
    /// <see cref="ParseErrorNode"/> child, so checking the top is sufficient
    /// and avoids walking arbitrarily deep trees.
    /// </summary>
    public static bool IsParseErrorBody(PassageBody body)
    {
      if (body?.Children == null) return false;
      for (int i = 0; i < body.Children.Count; i++)
        if (body.Children[i] is ParseErrorNode) return true;
      return false;
    }
  }
}
