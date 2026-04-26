using System.Collections.Generic;
using Harlowe.Ast.Expression;

namespace Harlowe.Ast.Body
{
  public class MacroNode : IBodyNode
  {
    public string Name;
    public List<IExpressionNode> Arguments;
    public HookNode AttachedHook;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
