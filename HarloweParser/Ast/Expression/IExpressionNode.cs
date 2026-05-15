namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// Marker for any node that can appear inside a macro's argument list. An
  /// expression node is something the runtime *evaluates to a value* — it does
  /// not produce visible output on its own. Compare with <see cref="Body.IBodyNode"/>,
  /// which describes content that the renderer prints.
  /// </summary>
  public interface IExpressionNode
  {
    /// <summary>Dispatch to the appropriate <see cref="IExpressionVisitor"/> overload (Visitor pattern).</summary>
    void Accept(IExpressionVisitor visitor);
  }

  /// <summary>
  /// Double-dispatch entry point for evaluating or transforming an expression
  /// tree. The evaluator implements this; static-analysis passes can too.
  /// </summary>
  public interface IExpressionVisitor
  {
    void Visit(LiteralNode node);
    void Visit(IdentifierNode node);
    void Visit(VariableRefNode node);
    void Visit(BinaryOpNode node);
    void Visit(UnaryOpNode node);
    void Visit(MacroCallNode node);
    void Visit(ArrayNode node);
    void Visit(DatamapNode node);
    void Visit(DatasetNode node);
    void Visit(LambdaNode node);
    void Visit(HookRefNode node);
  }
}
