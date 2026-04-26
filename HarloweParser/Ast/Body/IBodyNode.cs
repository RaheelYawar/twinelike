namespace Harlowe.Ast.Body
{
  public interface IBodyNode
  {
    void Accept(IBodyVisitor visitor);
  }

  public interface IBodyVisitor
  {
    void Visit(TextNode node);
    void Visit(NewlineNode node);
    void Visit(VariableNode node);
    void Visit(MacroNode node);
    void Visit(HookNode node);
    void Visit(LinkNode node);
    void Visit(HtmlNode node);
  }
}
