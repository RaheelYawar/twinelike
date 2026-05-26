using Harlowe.Ast.Expression;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Shared utility every lambda-consuming macro routes through. Binds the
  /// lambda's parameter to one item, binds the implicit <c>it</c> and
  /// <c>pos</c> identifiers for the iteration, runs the appropriate clause
  /// through a fresh <see cref="ExpressionEvaluator"/> built from the
  /// caller's <see cref="MacroContext"/>, and restores the prior bindings
  /// before returning. Errors short-circuit; the lambda sees the caller's
  /// variable store at invocation time rather than at construction time, so
  /// no closure capture is needed.
  ///
  /// <para>
  /// Shipped: <see cref="EvalPredicate"/> for <c>where</c>-clause lambdas
  /// (<c>(find:)</c>, <c>(all-pass:)</c>, <c>(some-pass:)</c>,
  /// <c>(none-pass:)</c>), <see cref="EvalTransform"/> for <c>via</c>-clause
  /// lambdas (<c>(altered:)</c>), and <see cref="EvalFold"/> for
  /// <c>making</c>+<c>via</c> fold lambdas (<c>(folded:)</c>). Body-position
  /// iteration for <c>(for:)</c> binds inline in <c>Changer.Apply</c> rather
  /// than routing here.
  /// </para>
  ///
  /// <para>
  /// The <paramref name="pos"/> parameter on each entry point is the
  /// 1-indexed iteration position. Callers supply <c>i + 1</c> from their
  /// item loop. The binding exposes <c>pos</c> as an identifier inside the
  /// lambda clause body, matching reference Harlowe (see the <c>pos</c>
  /// binding in <c>ts/datatypes/lambda.ts</c>'s <c>apply()</c>).
  /// </para>
  /// </summary>
  public static class LambdaInvoker
  {
    /// <summary>
    /// Bind <paramref name="item"/> to the lambda's parameter (or to the
    /// <c>it</c> slot when the parameter is implicit), bind <c>pos</c> to
    /// <paramref name="pos"/>, evaluate the <c>where</c> clause, and return
    /// its boolean result. A non-boolean clause result or a clause-side
    /// error surfaces as a <see cref="HarloweValueKind.Error"/> value; the
    /// caller is expected to short-circuit on it.
    /// </summary>
    public static HarloweValue EvalPredicate(LambdaValue lambda, HarloweValue item, int pos, MacroContext ctx)
    {
      if (item.IsError) return item;
      if (lambda == null || lambda.Node == null) return HarloweValue.OfError("missing lambda");
      if (lambda.Node.WhereClause == null) return HarloweValue.OfError("lambda has no 'where' clause");

      var result = EvaluateClause(lambda, item, pos, ctx, lambda.Node.WhereClause);
      if (result.IsError) return result;
      if (result.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"lambda 'where' clause must produce a Bool; got {result.Kind}");
      return result;
    }

    /// <summary>
    /// Bind <paramref name="item"/> to the lambda's parameter (or to the
    /// <c>it</c> slot) and <c>pos</c> to <paramref name="pos"/>, evaluate
    /// the <c>via</c> clause, and return its result. Used by
    /// transform-flavoured macros (<c>(altered:)</c>, etc.). Unlike
    /// <see cref="EvalPredicate"/>, the result kind is unconstrained — a
    /// transform can produce any value.
    /// </summary>
    public static HarloweValue EvalTransform(LambdaValue lambda, HarloweValue item, int pos, MacroContext ctx)
    {
      if (item.IsError) return item;
      if (lambda == null || lambda.Node == null) return HarloweValue.OfError("missing lambda");
      if (lambda.Node.ViaClause == null) return HarloweValue.OfError("lambda has no 'via' clause");
      return EvaluateClause(lambda, item, pos, ctx, lambda.Node.ViaClause);
    }

    /// <summary>
    /// Bind <paramref name="item"/> to the lambda's item parameter and
    /// <paramref name="accumulator"/> to its <c>making</c> parameter, bind
    /// <c>pos</c> to <paramref name="pos"/>, evaluate the <c>via</c> clause,
    /// and return the result — the new accumulator value. Used by
    /// <c>(folded:)</c>. The lambda must carry both a <c>making</c> name and
    /// a <c>via</c> clause; anything else surfaces as an in-prose error.
    /// <c>it</c> is bound to the item alongside the named parameter,
    /// matching the predicate/transform discipline.
    /// </summary>
    public static HarloweValue EvalFold(LambdaValue lambda, HarloweValue accumulator, HarloweValue item, int pos, MacroContext ctx)
    {
      if (accumulator.IsError) return accumulator;
      if (item.IsError) return item;
      if (lambda == null || lambda.Node == null) return HarloweValue.OfError("missing lambda");

      var node = lambda.Node;
      if (node.MakingName == null) return HarloweValue.OfError("(folded:) lambda must have a 'making' clause");
      if (node.ViaClause == null) return HarloweValue.OfError("(folded:) lambda must have a 'via' clause");

      var evaluator = new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker);

      using (ctx.Store.PushItBinding(item))
      using (ctx.Store.PushPosBinding(pos))
      using (ctx.Store.PushBinding(node.MakingName, node.MakingIsTemporary, accumulator))
      {
        if (node.ParameterName == null)
          return evaluator.Evaluate(node.ViaClause);
        using (ctx.Store.PushBinding(node.ParameterName, node.ParameterIsTemporary, item))
          return evaluator.Evaluate(node.ViaClause);
      }
    }

    /// <summary>
    /// Shared binding-and-evaluation core for predicate and transform
    /// clauses. Always push-binds <c>it</c> to the item and <c>pos</c> to the
    /// iteration position per Harlowe spec (both are interchangeable with
    /// the explicit parameter inside the clause body); layers the named
    /// binding on top when the lambda has a parameter. Errors propagate
    /// as-is — the caller layer adds clause-kind validation.
    /// </summary>
    private static HarloweValue EvaluateClause(LambdaValue lambda, HarloweValue item, int pos, MacroContext ctx, IExpressionNode clause)
    {
      var node = lambda.Node;
      var evaluator = new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker);

      using (ctx.Store.PushItBinding(item))
      using (ctx.Store.PushPosBinding(pos))
      {
        if (node.ParameterName == null)
          return evaluator.Evaluate(clause);
        using (ctx.Store.PushBinding(node.ParameterName, node.ParameterIsTemporary, item))
          return evaluator.Evaluate(clause);
      }
    }
  }
}
