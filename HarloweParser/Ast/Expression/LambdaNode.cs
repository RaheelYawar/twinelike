namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A lambda expression: the right-hand side of a macro argument that consumes
  /// behaviour rather than a fixed value. Covers every permutation Harlowe
  /// recognises with a single node carrying nullable clauses, since the variant
  /// space is small and AST consumers benefit from one Visit overload.
  ///
  /// <para>
  /// Three surface shapes, all expressed by setting fields below:
  /// </para>
  /// <list type="bullet">
  /// <item><description><c>_x where _x &gt; 5</c> — explicit parameter + predicate.</description></item>
  /// <item><description><c>where it &gt; 5</c> — implicit parameter (the evaluator
  /// binds <c>it</c> at invocation).</description></item>
  /// <item><description><c>_running making _x via _running + _x</c> — fold lambda
  /// with both an accumulator and an item parameter.</description></item>
  /// </list>
  ///
  /// <para>
  /// Lambdas see the caller's scope at invocation time (no closure capture).
  /// The runtime wrapper is <see cref="Harlowe.Runtime.LambdaValue"/>, which is
  /// just a thin reference to this node.
  /// </para>
  /// </summary>
  public class LambdaNode : IExpressionNode
  {
    /// <summary>Bound parameter name without sigil. Null for the implicit-<c>it</c> form.</summary>
    public string ParameterName;

    /// <summary>True if the parameter was written as a temporary (<c>_x</c>) rather than a story (<c>$x</c>) variable.</summary>
    public bool ParameterIsTemporary;

    /// <summary>Optional type tag from <c>-type</c> syntax (e.g. <c>num-type _x</c>). Null when untyped. Not enforced in v2.3A.</summary>
    public string ParameterType;

    /// <summary>Accumulator parameter for fold lambdas (<c>_running making _x via ...</c>). Null outside the fold shape.</summary>
    public string MakingName;

    /// <summary>True if the accumulator parameter was written as a temporary variable.</summary>
    public bool MakingIsTemporary;

    /// <summary>Optional type tag for the accumulator. Null in v2.3A.</summary>
    public string MakingType;

    /// <summary>Predicate clause (<c>where ...</c>). Null when absent. A lambda has at most one of <see cref="WhereClause"/> / <see cref="ViaClause"/>.</summary>
    public IExpressionNode WhereClause;

    /// <summary>Transform clause (<c>via ...</c>). Null when absent.</summary>
    public IExpressionNode ViaClause;

    /// <summary>Reserved for <c>(event:)</c> in a later slice. Always null in v2.3.</summary>
    public IExpressionNode WhenClause;

    /// <summary>True for the <c>each _x</c> body-iteration form; no clause body is parsed.</summary>
    public bool IsEach;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
