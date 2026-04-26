namespace Harlowe.Ast.Expression
{
  public interface IExpressionNode
  {
    void Accept(IExpressionVisitor visitor);
  }

  public interface IExpressionVisitor
  {
    void Visit(LiteralNode node);
    void Visit(VariableRefNode node);
    void Visit(BinaryOpNode node);
    void Visit(UnaryOpNode node);
    void Visit(MacroCallNode node);
    void Visit(ArrayNode node);
    void Visit(DatamapNode node);
    void Visit(DatasetNode node);
  }
}
