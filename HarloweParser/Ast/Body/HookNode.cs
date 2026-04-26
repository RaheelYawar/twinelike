using System.Collections.Generic;

namespace Harlowe.Ast.Body
{
  public enum HookAnchor
  {
    None,
    Left,
    Right
  }

  public class HookNode : IBodyNode
  {
    public string Name;
    public HookAnchor Anchor;
    public List<IBodyNode> Children;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
