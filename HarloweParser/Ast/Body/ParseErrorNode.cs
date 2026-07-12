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
    /// The diagnostic shown to the author when this node renders — a finished
    /// sentence (<c>parse error in passage 'X' at line 2, column 5: …</c>), since
    /// the renderer pushes it straight through <c>IRenderOutput.Error</c>.
    ///
    /// <para>Prose, not data. Tooling that wants to *act* on the error — jump to
    /// the line, group by passage — reads <see cref="Detail"/> /
    /// <see cref="Line"/> / <see cref="Column"/> instead, which carry the same
    /// facts structured. Both are set from the same exception at the same spot,
    /// so they can't drift.</para>
    /// </summary>
    public string Message;

    /// <summary>
    /// The parser's raw diagnostic, without the <c>parse error in passage 'X' at
    /// line N</c> wrapper <see cref="Message"/> bakes around it — e.g.
    /// <c>use 'is' instead of 'eq'</c>. The structured half of the message.
    /// </summary>
    public string Detail;

    /// <summary>1-based line in the passage body where the parse failed; <c>0</c> when unknown.</summary>
    public int Line;

    /// <summary>1-based column where the parse failed; <c>0</c> when unknown.</summary>
    public int Column;

    /// <summary>
    /// The original source text this node stands in for, when available.
    /// Populated by loader-level recovery (where the full body source is in
    /// hand) so <see cref="Twee.MarkupPrinter"/> can round-trip the broken
    /// source even when the passage has no <see cref="HarlowePassage.RawBody"/>
    /// to fall back on (programmatically constructed passages). Body-parser
    /// per-node recovery also populates this when the caller routes through
    /// <c>HarloweBodyParser.Parse(tokens, source)</c> — the slice covers the
    /// failed node's first token through the resync point, so a consumer who
    /// mutates the AST of a partially-recovered passage and re-saves keeps
    /// the broken substring instead of silently dropping it.
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
