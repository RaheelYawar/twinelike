namespace Harlowe.Ast.Body
{
  public class VariableNode : IBodyNode
  {
    public string Name;
    public bool IsTemporary;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
