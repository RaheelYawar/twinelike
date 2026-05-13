using Harlowe.Ast.Expression;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Shared utility every lambda-consuming macro routes through. Binds the
  /// lambda's parameter to one item, runs the appropriate clause through a
  /// fresh <see cref="ExpressionEvaluator"/> built from the caller's
  /// <see cref="MacroContext"/>, and restores the prior binding before
  /// returning. Errors short-circuit; the lambda sees the caller's variable
  /// store at invocation time rather than at construction time, so no closure
  /// capture is needed.
  ///
  /// <para>
  /// Shipped: <see cref="EvalPredicate"/> for <c>where</c>-clause lambdas
  /// (<c>(find:)</c>, <c>(all-pass:)</c>, <c>(some-pass:)</c>,
  /// <c>(none-pass:)</c>) and <see cref="EvalTransform"/> for <c>via</c>-clause
  /// lambdas (<c>(altered:)</c>). Later slices add <c>EvalFold</c> (making+via)
  /// and <c>BindEach</c> (body-position iteration for <c>(for:)</c>).
  /// </para>
  /// </summary>
  public static class LambdaInvoker
  {
    /// <summary>
    /// Bind <paramref name="item"/> to the lambda's parameter (or to the
    /// <c>it</c> slot when the parameter is implicit), evaluate the
    /// <c>where</c> clause, and return its boolean result. A non-boolean
    /// clause result or a clause-side error surfaces as a
    /// <see cref="HarloweValueKind.Error"/> value; the caller is expected to
    /// short-circuit on it.
    /// </summary>
    public static HarloweValue EvalPredicate(LambdaValue lambda, HarloweValue item, MacroContext ctx)
    {
      if (item.IsError) return item;
      if (lambda == null || lambda.Node == null) return HarloweValue.OfError("missing lambda");
      if (lambda.Node.WhereClause == null) return HarloweValue.OfError("lambda has no 'where' clause");

      var result = EvaluateClause(lambda, item, ctx, lambda.Node.WhereClause);
      if (result.IsError) return result;
      if (result.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"lambda 'where' clause must produce a Bool; got {result.Kind}");
      return result;
    }

    /// <summary>
    /// Bind <paramref name="item"/> to the lambda's parameter (or to the
    /// <c>it</c> slot), evaluate the <c>via</c> clause, and return its
    /// result. Used by transform-flavoured macros (<c>(altered:)</c>, etc.).
    /// Unlike <see cref="EvalPredicate"/>, the result kind is unconstrained
    /// — a transform can produce any value.
    /// </summary>
    public static HarloweValue EvalTransform(LambdaValue lambda, HarloweValue item, MacroContext ctx)
    {
      if (item.IsError) return item;
      if (lambda == null || lambda.Node == null) return HarloweValue.OfError("missing lambda");
      if (lambda.Node.ViaClause == null) return HarloweValue.OfError("lambda has no 'via' clause");
      return EvaluateClause(lambda, item, ctx, lambda.Node.ViaClause);
    }

    /// <summary>
    /// Shared binding-and-evaluation core for predicate and transform
    /// clauses. Always push-binds <c>it</c> to the item per Harlowe spec
    /// (<c>it</c> is interchangeable with the explicit parameter inside the
    /// clause body); layers the named binding on top when the lambda has a
    /// parameter. Errors propagate as-is — the caller layer adds clause-kind
    /// validation.
    /// </summary>
    private static HarloweValue EvaluateClause(LambdaValue lambda, HarloweValue item, MacroContext ctx, IExpressionNode clause)
    {
      var node = lambda.Node;
      var evaluator = new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker);

      using (ctx.Store.PushItBinding(item))
      {
        if (node.ParameterName == null)
          return evaluator.Evaluate(clause);
        using (ctx.Store.PushBinding(node.ParameterName, node.ParameterIsTemporary, item))
          return evaluator.Evaluate(clause);
      }
    }
  }
}
