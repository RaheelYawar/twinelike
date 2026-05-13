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
  /// v2.3A ships <see cref="EvalPredicate"/> for <c>where</c>-clause lambdas
  /// (used by <c>(find:)</c>, <c>(all-pass:)</c>, etc.). Later slices add
  /// <c>EvalTransform</c> (via clause), <c>EvalFold</c> (making+via), and
  /// <c>BindEach</c> (body-position iteration for <c>(for:)</c>).
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
      var node = lambda.Node;
      if (node.WhereClause == null) return HarloweValue.OfError("lambda has no 'where' clause");

      var evaluator = new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker);

      // Always bind `it` to the item — the Harlowe spec says `it` is
      // interchangeable with the lambda parameter inside the clause body
      // (`_x where it > 5` works equivalently to `_x where _x > 5`). The
      // implicit-parameter form (`where it > 5`) is just the named form with
      // the name elided. The named binding is layered on top so the explicit
      // sigil also resolves.
      HarloweValue result;
      using (ctx.Store.PushItBinding(item))
      {
        if (node.ParameterName == null)
        {
          result = evaluator.Evaluate(node.WhereClause);
        }
        else
        {
          using (ctx.Store.PushBinding(node.ParameterName, node.ParameterIsTemporary, item))
            result = evaluator.Evaluate(node.WhereClause);
        }
      }

      if (result.IsError) return result;
      if (result.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"lambda 'where' clause must produce a Bool; got {result.Kind}");
      return result;
    }

  }
}
