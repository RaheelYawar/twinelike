using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  public class ArrayNode : IExpressionNode
  {
    public List<IExpressionNode> Items;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
