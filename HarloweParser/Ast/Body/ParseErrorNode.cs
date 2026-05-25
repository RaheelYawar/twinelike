namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A synthetic body node carrying a parse-error message. The HTML and Twee
  /// loaders (and <see cref="Harlowe.AddPassage"/>) emit one of these in place
  /// of a passage's real AST when tokenize/parse throws, so a single broken
  /// passage doesn't abort the whole story load — the rest of the story
  /// renders normally and the broken passage produces an in-prose error at
  /// render time. The body parser also emits one (with <see cref="OriginalSource"/>
  /// left null) when per-node recovery fires inside an otherwise-parseable
  /// passage.
  ///
  /// <para>Lives in the body AST namespace so existing visitors (renderer,
  /// branch collector, printer) get a uniform dispatch point.</para>
  /// </summary>
  public class ParseErrorNode : IBodyNode
  {
    /// <summary>
    /// The diagnostic shown to the author when this node renders.
    /// </summary>
    public string Message;

    /// <summary>
    /// The original source text this node stands in for, when available.
    /// Populated by loader-level recovery (where the full body source is in
    /// hand) so <see cref="Twee.MarkupPrinter"/> can round-trip the broken
    /// source even when the passage has no <see cref="HarlowePassage.RawBody"/>
    /// to fall back on (programmatically constructed passages). Body-parser
    /// per-node recovery leaves this null because the parser only sees tokens,
    /// not source text.
    /// </summary>
    public string OriginalSource;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);

    /// <summary>
    /// True when <paramref name="body"/> is the loader-recovery shape — a
    /// single top-level child that is a <see cref="ParseErrorNode"/>, meaning
    /// the entire passage failed to parse and the AST is a synthetic stub.
    /// Distinguishes this case from partial body-parser recovery (a real AST
    /// that also contains one or more ParseErrorNodes among its valid
    /// children), which should follow the normal IsDirty round-trip path
    /// rather than being treated as wholly broken.
    /// </summary>
    public static bool IsWhollyParseError(PassageBody body)
    {
      if (body?.Children == null) return false;
      if (body.Children.Count != 1) return false;
      return body.Children[0] is ParseErrorNode;
    }
  }
}
