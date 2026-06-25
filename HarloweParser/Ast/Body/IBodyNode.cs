namespace Harlowe.Ast.Body
{
  /// <summary>
  /// Marker for any node that can appear as direct content inside a passage
  /// body or hook. Body nodes describe what the *renderer* will see in order:
  /// prose, line breaks, variable interpolations, macro invocations, hooks,
  /// links, raw HTML.
  /// </summary>
  public interface IBodyNode
  {
    /// <summary>Dispatch to the appropriate <see cref="IBodyVisitor"/> overload (Visitor pattern).</summary>
    void Accept(IBodyVisitor visitor);
  }

  /// <summary>
  /// Double-dispatch entry point for traversing a passage body. Implementers
  /// (e.g. a renderer, an evaluator, an HTML emitter, a static-analysis pass)
  /// override one method per concrete node type.
  /// </summary>
  public interface IBodyVisitor
  {
    void Visit(TextNode node);
    void Visit(NewlineNode node);
    void Visit(VariableNode node);
    void Visit(MacroNode node);
    void Visit(HookNode node);
    void Visit(FormatNode node);
    void Visit(LinkNode node);
    void Visit(HtmlNode node);
    void Visit(ChangerChainNode node);
    void Visit(ParseErrorNode node);
  }
}
