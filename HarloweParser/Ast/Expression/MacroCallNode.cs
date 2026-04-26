using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  public class MacroCallNode : IExpressionNode
  {
    public string Name;
    public List<IExpressionNode> Arguments;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
