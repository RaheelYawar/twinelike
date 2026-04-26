using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A key/value collection (Harlowe's "datamap"), built in source with
  /// <c>(dm: "name", "Ada", "hp", 10)</c>. <see cref="Keys"/> and
  /// <see cref="Values"/> are positional lists of equal length — a flat shape
  /// that survives serialization round-trips and avoids picking a hash type
  /// at parse time.
  /// </summary>
  public class DatamapNode : IExpressionNode
  {
    public List<IExpressionNode> Keys;
    public List<IExpressionNode> Values;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
