using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  public class DatamapNode : IExpressionNode
  {
    public List<IExpressionNode> Keys;
    public List<IExpressionNode> Values;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
