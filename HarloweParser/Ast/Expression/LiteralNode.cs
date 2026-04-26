namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A constant value baked in at parse time, e.g. <c>5</c>, <c>"hello"</c>, or
  /// <c>true</c>. <see cref="Value"/> holds the boxed CLR value
  /// (<see cref="string"/>, <see cref="double"/>, or <see cref="bool"/>) and
  /// <see cref="Kind"/> records which variant it is.
  /// </summary>
  public class LiteralNode : IExpressionNode
  {
    public LiteralKind Kind;
    public object Value;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
