using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// An unordered, deduplicating collection (Harlowe's "dataset"), built in
  /// source with <c>(ds: 1, 2, 3)</c>. Deduplication happens at evaluation
  /// time, not at parse time, so the AST keeps every item the author wrote.
  /// </summary>
  public class DatasetNode : IExpressionNode
  {
    public List<IExpressionNode> Items;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
