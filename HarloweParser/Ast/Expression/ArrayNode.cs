using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// An ordered, duplicate-permitting collection literal. In Harlowe these are
  /// usually built with <c>(a: 1, 2, 3)</c>; this node represents the parsed
  /// result so the evaluator does not have to special-case the <c>a</c> macro.
  /// </summary>
  public class ArrayNode : IExpressionNode
  {
    public List<IExpressionNode> Items;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
