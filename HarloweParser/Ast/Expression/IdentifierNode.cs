namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A bare identifier inside an expression. Covers two related Harlowe
  /// concepts: <em>built-in identifiers</em> like <c>it</c>, <c>time</c>,
  /// <c>visit</c>, <c>passage</c>, and <em>data names</em> like the right-hand
  /// side of <c>$a's name</c> or the left-hand side of <c>name of $a</c>.
  ///
  /// <para>
  /// Distinct from <see cref="LiteralNode"/>: a literal carries a fixed value;
  /// an identifier is a <em>name</em> that the runtime resolves against either
  /// the built-in identifier set or the surrounding data structure. The
  /// distinction matters for the evaluator and for any static analysis.
  /// </para>
  /// </summary>
  public class IdentifierNode : IExpressionNode
  {
    /// <summary>The identifier text exactly as written, e.g. <c>"it"</c>, <c>"time"</c>, <c>"name"</c>.</summary>
    public string Name;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
