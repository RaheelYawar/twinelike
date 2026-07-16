using System.Collections.Generic;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// v2.3A — lambda-consuming macros (<c>(find:)</c>, <c>(all-pass:)</c>) plus
  /// scope-isolation invariants on the underlying <see cref="LambdaInvoker"/>.
  /// Driven through the full evaluator → registry pipeline so the tests cover
  /// the parser-to-runtime hand-off in addition to the macro body.
  /// </summary>
  public class LambdaMacroTests
  {
    private static (MacroRegistry reg, MacroContext ctx) Setup()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = reg };
      reg.Context = ctx;
      return (reg, ctx);
    }

    private static HarloweValue Eval(MacroRegistry reg, MacroContext ctx, string expr)
    {
      var tokens = new HarloweTokenizer().Tokenize("(_:" + expr + ")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var node = new HarloweExpressionParser().ParseExpression(cursor);
      var evaluator = new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker);
      return evaluator.Evaluate(node);
    }

    private static LambdaValue ParseLambda(string expr)
    {
      var tokens = new HarloweTokenizer().Tokenize("(_:" + expr + ")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var node = new HarloweExpressionParser().ParseExpression(cursor);
      return new LambdaValue { Node = (LambdaNode)node };
    }

    // --- LambdaInvoker.EvalPredicate ---

    [Fact]
    public void EvalPredicate_NamedParam_BindsTempAndRestores()
    {
      var (reg, ctx) = Setup();
      // Surrounding scope has a temp _x set; predicate body should see the
      // bound item, not the surrounding value. After the call, _x must be
      // restored to its prior value.
      ctx.Store.Set("x", true, HarloweValue.OfNumber(999));
      var lambda = ParseLambda("_x where _x > 5");

      var verdict = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), 1, ctx);

      Assert.Equal(HarloweValueKind.Bool, verdict.Kind);
      Assert.True(verdict.AsBool);
      Assert.Equal(999, ctx.Store.Get("x", true).AsNumber); // restored
    }

    [Fact]
    public void EvalPredicate_ImplicitIt_BindsItAndRestores()
    {
      var (reg, ctx) = Setup();
      ctx.Store.Set("anchor", false, HarloweValue.OfNumber(1)); // installs an `it`
      var priorIt = ctx.Store.It;
      var lambda = ParseLambda("where it > 5");

      var verdict = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), 1, ctx);

      Assert.True(verdict.AsBool);
      Assert.Same(priorIt, ctx.Store.It); // `it` restored
    }

    [Fact]
    public void EvalPredicate_PropagatesItemError()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x where _x > 5");
      var v = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfError("upstream"), 1, ctx);
      Assert.True(v.IsError);
    }

    [Fact]
    public void EvalPredicate_NonBoolClause_ReportsError()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x where _x + 1");
      var v = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(5), 1, ctx);
      Assert.True(v.IsError);
      Assert.Contains("Bool", v.ErrorMessage);
    }

    [Fact]
    public void EvalPredicate_PropagatesClauseError()
    {
      var (reg, ctx) = Setup();
      // unset $missing — looked up inside the predicate, surfaces as error.
      var lambda = ParseLambda("_x where $missing > 5");
      var v = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), 1, ctx);
      Assert.True(v.IsError);
    }

    [Fact]
    public void EvalPredicate_NamedParam_AlsoBindsIt()
    {
      // Harlowe spec: inside a lambda body, `it` and the explicit parameter
      // refer to the same value. So `_x where it > 5` must see item=10 as
      // both _x and `it`.
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x where it > 5");
      var verdict = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), 1, ctx);
      Assert.True(verdict.AsBool);
    }

    // --- (find:) ---

    [Fact]
    public void Find_FiltersInlineItems()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(find: _x where _x > 5, 1, 6, 3, 7)");
      var arr = v.AsArray;
      Assert.Equal(2, arr.Count);
      Assert.Equal(6, arr[0].AsNumber);
      Assert.Equal(7, arr[1].AsNumber);
    }

    // --- `pos` identifier (1-indexed iteration position) ---

    [Fact]
    public void Pos_AlteredVia_BindsOneIndexedPosition()
    {
      // Documented reference example: `(altered: via it + (str:pos), "A","B","C")`
      // → (a:"A1","B2","C3"). pos binds 1, 2, 3. Uses (str:) directly to
      // match reference's docs verbatim — (str:) is now registered as an
      // alias for (text:).
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(altered: _x via _x + (str: pos), \"A\", \"B\", \"C\")");
      var arr = v.AsArray;
      Assert.Equal(3, arr.Count);
      Assert.Equal("A1", arr[0].AsString);
      Assert.Equal("B2", arr[1].AsString);
      Assert.Equal("C3", arr[2].AsString);
    }

    [Fact]
    public void Pos_FindWhere_AvailableInPredicate()
    {
      // Predicate that picks items at even positions.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(find: _x where pos % 2 is 0, \"a\", \"b\", \"c\", \"d\")");
      var arr = v.AsArray;
      Assert.Equal(2, arr.Count);
      Assert.Equal("b", arr[0].AsString); // pos 2
      Assert.Equal("d", arr[1].AsString); // pos 4
    }

    [Fact]
    public void Pos_OutsideLambda_IsError()
    {
      // `pos` is only meaningful inside a lambda iteration. Reading it
      // from bare expression scope errors in-prose.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(print: pos)");
      Assert.True(v.IsError);
      Assert.Contains("pos", v.ErrorMessage);
    }

    [Fact]
    public void Pos_FoldedVia_StartsAtTwoSinceFirstItemIsAccumulator()
    {
      // (folded:) treats items[0] as the initial accumulator (no lambda
      // call), so the first lambda invocation processes items[1] at pos=2.
      // The accumulator is `pos` summed; final result is 2 + 3 = 5.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _x making _acc via _acc + pos, 0, 10, 20)");
      Assert.Equal(5, v.AsNumber); // 0 + 2 + 3
    }

    [Fact]
    public void Find_FiltersSingleArrayArg()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(find: _x where _x > 5, (a: 1, 6, 3, 7))");
      Assert.Equal(2, v.AsArray.Count);
    }

    [Fact]
    public void Find_NoMatches_ReturnsEmptyArray()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(find: _x where _x > 100, 1, 2, 3)");
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Empty(v.AsArray);
    }

    [Fact]
    public void Find_ImplicitItForm()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(find: where it > 5, 1, 6, 3, 7)");
      Assert.Equal(2, v.AsArray.Count);
    }

    [Fact]
    public void Find_NonLambdaFirstArg_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(find: 5, 1, 2, 3)");
      Assert.True(v.IsError);
      Assert.Contains("lambda", v.ErrorMessage);
    }

    [Fact]
    public void Find_ErrorInIterable_ShortCircuits()
    {
      var (reg, ctx) = Setup();
      // Reading unset $bad as one of the items surfaces an error that find propagates.
      var v = Eval(reg, ctx, "(find: _x where _x > 0, 1, $bad, 3)");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Find_ErrorInLambdaSlot_PropagatesOriginalError()
    {
      var (reg, ctx) = Setup();
      // The lambda-slot expression itself errors (unset $bad). The original
      // error must propagate via the central gate, not be masked behind the
      // macro's own "first argument must be a lambda" message (cf.
      // Find_NonLambdaFirstArg_Errors, where a plain non-lambda still reports it).
      var v = Eval(reg, ctx, "(find: $bad, 1, 2, 3)");
      Assert.True(v.IsError);
      Assert.DoesNotContain("must be a lambda", v.ErrorMessage);
    }

    // --- (all-pass:) ---

    [Fact]
    public void AllPass_AllMatch_TrueAndNoShortCircuitNeeded()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(all-pass: _x where _x > 0, 1, 2, 3)");
      Assert.True(v.AsBool);
    }

    [Fact]
    public void AllPass_OneFails_ReturnsFalse()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(all-pass: _x where _x > 0, 1, -5, 3)");
      Assert.False(v.AsBool);
    }

    [Fact]
    public void AllPass_EmptyIsVacuouslyTrue()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(all-pass: _x where _x > 0, (a:))");
      Assert.True(v.AsBool);
    }

    [Fact]
    public void AllPass_ImplicitItForm()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(all-pass: where it > 0, 1, 2, 3)");
      Assert.True(v.AsBool);
    }

    [Fact]
    public void AllPass_ParamShadowsSurroundingStoreValue()
    {
      var (reg, ctx) = Setup();
      ctx.Store.Set("x", true, HarloweValue.OfNumber(-1)); // would fail predicate
      var v = Eval(reg, ctx, "(all-pass: _x where _x > 0, 1, 2)");
      Assert.True(v.AsBool);
      Assert.Equal(-1, ctx.Store.Get("x", true).AsNumber); // surrounding _x untouched
    }

    // --- LambdaInvoker.EvalTransform ---

    [Fact]
    public void EvalTransform_BindsParamAndComputesResult()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x via _x * 2");
      var v = LambdaInvoker.EvalTransform(lambda, HarloweValue.OfNumber(7), 1, ctx);
      Assert.Equal(14, v.AsNumber);
    }

    [Fact]
    public void EvalTransform_MissingViaClause_Errors()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x where _x > 5"); // no via clause
      var v = LambdaInvoker.EvalTransform(lambda, HarloweValue.OfNumber(7), 1, ctx);
      Assert.True(v.IsError);
      Assert.Contains("via", v.ErrorMessage);
    }

    [Fact]
    public void EvalTransform_AnyKindAllowed_UnlikePredicate()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x via _x + 1"); // produces Number, not Bool
      var v = LambdaInvoker.EvalTransform(lambda, HarloweValue.OfNumber(7), 1, ctx);
      Assert.Equal(HarloweValueKind.Number, v.Kind);
    }

    // --- (altered:) ---

    [Fact]
    public void Altered_MapsInlineItems()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(altered: _x via _x * 2, 1, 2, 3)");
      Assert.Equal(2, v.AsArray[0].AsNumber);
      Assert.Equal(4, v.AsArray[1].AsNumber);
      Assert.Equal(6, v.AsArray[2].AsNumber);
    }

    [Fact]
    public void Altered_MapsSingleArrayArg()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(altered: _x via _x * 2, (a: 1, 2, 3))");
      Assert.Equal(3, v.AsArray.Count);
    }

    [Fact]
    public void Altered_ImplicitItForm()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(altered: via it * 10, 1, 2, 3)");
      Assert.Equal(10, v.AsArray[0].AsNumber);
      Assert.Equal(30, v.AsArray[2].AsNumber);
    }

    [Fact]
    public void Altered_RejectsWhereOnlyLambda()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(altered: _x where _x > 5, 1, 2, 3)");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Altered_PropagatesTransformError()
    {
      var (reg, ctx) = Setup();
      // Division by zero inside the transform — error surfaces from the macro.
      var v = Eval(reg, ctx, "(altered: _x via _x / 0, 1, 2)");
      Assert.True(v.IsError);
    }

    // --- (some-pass:) ---

    [Fact]
    public void SomePass_OneMatch_TrueAndShortCircuits()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(some-pass: _x where _x > 5, 1, 6, 3)");
      Assert.True(v.AsBool);
    }

    [Fact]
    public void SomePass_NoMatch_False()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(some-pass: _x where _x > 100, 1, 2, 3)");
      Assert.False(v.AsBool);
    }

    [Fact]
    public void SomePass_Empty_False()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(some-pass: _x where _x > 5, (a:))");
      Assert.False(v.AsBool);
    }

    // --- (none-pass:) ---

    [Fact]
    public void NonePass_NoMatch_True()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(none-pass: _x where _x > 100, 1, 2, 3)");
      Assert.True(v.AsBool);
    }

    [Fact]
    public void NonePass_OneMatch_FalseAndShortCircuits()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(none-pass: _x where _x > 5, 1, 6, 3)");
      Assert.False(v.AsBool);
    }

    [Fact]
    public void NonePass_Empty_True()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(none-pass: _x where _x > 5, (a:))");
      Assert.True(v.AsBool);
    }

    // --- LambdaInvoker.EvalFold (v2.3C) ---

    [Fact]
    public void EvalFold_BindsAccumulatorAndItem()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_item making _acc via _acc + _item");
      var v = LambdaInvoker.EvalFold(lambda, HarloweValue.OfNumber(10), HarloweValue.OfNumber(3), 2, ctx);
      Assert.Equal(13, v.AsNumber);
    }

    [Fact]
    public void EvalFold_BindsBothNamesAndRestores()
    {
      var (reg, ctx) = Setup();
      // Surrounding scope has temps that collide with both lambda params; the
      // clause must see the bound values, and both must be restored after.
      ctx.Store.Set("item", true, HarloweValue.OfNumber(111));
      ctx.Store.Set("acc", true, HarloweValue.OfNumber(222));
      var lambda = ParseLambda("_item making _acc via _acc + _item");

      var v = LambdaInvoker.EvalFold(lambda, HarloweValue.OfNumber(10), HarloweValue.OfNumber(3), 2, ctx);

      Assert.Equal(13, v.AsNumber);
      Assert.Equal(111, ctx.Store.Get("item", true).AsNumber);
      Assert.Equal(222, ctx.Store.Get("acc", true).AsNumber);
    }

    [Fact]
    public void EvalFold_MissingMakingClause_Errors()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x via _x * 2"); // no making clause
      var v = LambdaInvoker.EvalFold(lambda, HarloweValue.OfNumber(1), HarloweValue.OfNumber(2), 2, ctx);
      Assert.True(v.IsError);
      Assert.Contains("making", v.ErrorMessage);
    }

    [Fact]
    public void EvalFold_PropagatesAccumulatorError()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_item making _acc via _acc + _item");
      var v = LambdaInvoker.EvalFold(lambda, HarloweValue.OfError("upstream"), HarloweValue.OfNumber(2), 2, ctx);
      Assert.True(v.IsError);
    }

    [Fact]
    public void EvalFold_NoParameterName_BindsOnlyItAndAccumulator()
    {
      // The parser requires a parameter before `making`, but the AST is public —
      // a hand-built parameterless fold lambda must still evaluate, with the
      // item reachable through `it`.
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_item making _acc via _acc + it");
      lambda.Node.ParameterName = null;
      var v = LambdaInvoker.EvalFold(lambda, HarloweValue.OfNumber(10), HarloweValue.OfNumber(3), 2, ctx);
      Assert.Equal(13, v.AsNumber);
    }

    // --- (folded:) ---

    [Fact]
    public void Folded_SumsInlineItems()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _acc via _acc + _item, 1, 2, 3, 4)");
      Assert.Equal(10, v.AsNumber);
    }

    [Fact]
    public void Folded_SingleArrayArg()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _acc via _acc + _item, (a: 5, 6, 7))");
      Assert.Equal(18, v.AsNumber);
    }

    [Fact]
    public void Folded_SingleItem_ReturnsItself()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _acc via _acc + _item, 42)");
      Assert.Equal(42, v.AsNumber);
    }

    [Fact]
    public void Folded_EmptyArray_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _acc via _acc + _item, (a:))");
      Assert.True(v.IsError);
      Assert.Contains("at least one", v.ErrorMessage);
    }

    [Fact]
    public void Folded_NonLambdaFirstArg_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: 5, 1, 2, 3)");
      Assert.True(v.IsError);
      Assert.Contains("lambda", v.ErrorMessage);
    }

    [Fact]
    public void Folded_RejectsWhereOnlyLambda()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _x where _x > 0, 1, 2, 3)");
      Assert.True(v.IsError);
      Assert.Contains("making", v.ErrorMessage);
    }

    [Fact]
    public void Folded_PropagatesFoldBodyError()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _acc via _acc / 0, 1, 2, 3)");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Folded_AccumulatorShadowsSurroundingTemp()
    {
      var (reg, ctx) = Setup();
      ctx.Store.Set("acc", true, HarloweValue.OfNumber(999));
      var v = Eval(reg, ctx, "(folded: _item making _acc via _acc + _item, 1, 2, 3)");
      Assert.Equal(6, v.AsNumber);
      Assert.Equal(999, ctx.Store.Get("acc", true).AsNumber); // restored
    }

    // --- (folded:) where-clause filter (divergence #15) ---

    [Fact]
    public void Folded_WhereClause_FiltersItems()
    {
      // The reference doc's own example: sums only the values greater than 0.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _total via _total + _item where _item > 0, 0, 5, -2, 3)");
      Assert.Equal(8, v.AsNumber);
    }

    [Fact]
    public void Folded_WhereClause_SeedExempt()
    {
      // Per 3.3.6 the where clause doesn't apply to the first value — it goes
      // into the accumulator from the outset and never becomes a loop item.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _total via _total + _item where _item > 0, -10, 1, 2)");
      Assert.Equal(-7, v.AsNumber);
    }

    [Fact]
    public void Folded_WhereClause_AllFiltered_ReturnsSeed()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _total via _total + _item where _item > 0, 7, -1, -2)");
      Assert.Equal(7, v.AsNumber);
    }

    [Fact]
    public void Folded_WhereClause_SeesAccumulator()
    {
      // The filter runs under the same bindings as the via body, so it can
      // consult the running total: stop adding once _total reaches 3.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _i making _total via _total + _i where _total < 3, 0, 2, 2, 2)");
      Assert.Equal(4, v.AsNumber);
    }

    [Fact]
    public void Folded_WhereClause_NonBool_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _total via _total + _item where _item + 1, 0, 1)");
      Assert.True(v.IsError);
      Assert.Contains("Bool", v.ErrorMessage);
    }

    [Fact]
    public void Folded_WhereClause_ErrorPropagates()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(folded: _item making _total via _total + _item where _item > \"a\", 0, 1)");
      Assert.True(v.IsError);
    }

    // --- (rotated-to:) ---

    [Fact]
    public void RotatedTo_PivotInMiddle_RotatesMatchToFront()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(rotated-to: _x where _x is 3, 1, 2, 3, 4, 5)");
      var arr = v.AsArray;
      Assert.Equal(new double[] { 3, 4, 5, 1, 2 },
        new[] { arr[0].AsNumber, arr[1].AsNumber, arr[2].AsNumber, arr[3].AsNumber, arr[4].AsNumber });
    }

    [Fact]
    public void RotatedTo_PivotAlreadyFirst_Unchanged()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(rotated-to: _x where _x is 1, 1, 2, 3)");
      var arr = v.AsArray;
      Assert.Equal(1, arr[0].AsNumber);
      Assert.Equal(3, arr[2].AsNumber);
    }

    [Fact]
    public void RotatedTo_NoMatch_Unchanged()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(rotated-to: _x where _x > 100, 1, 2, 3)");
      var arr = v.AsArray;
      Assert.Equal(3, arr.Count);
      Assert.Equal(1, arr[0].AsNumber);
    }

    [Fact]
    public void RotatedTo_SingleArrayArg()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(rotated-to: _x where _x is 4, (a: 2, 4, 6))");
      Assert.Equal(4, v.AsArray[0].AsNumber);
      Assert.Equal(6, v.AsArray[1].AsNumber);
      Assert.Equal(2, v.AsArray[2].AsNumber);
    }

    [Fact]
    public void RotatedTo_ImplicitItForm()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(rotated-to: where it is 3, 1, 2, 3)");
      Assert.Equal(3, v.AsArray[0].AsNumber);
    }

    [Fact]
    public void RotatedTo_NonLambdaFirstArg_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(rotated-to: 3, 1, 2, 3)");
      Assert.True(v.IsError);
      Assert.Contains("lambda", v.ErrorMessage);
    }

    [Fact]
    public void RotatedTo_RejectsViaOnlyLambda()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(rotated-to: _x via _x * 2, 1, 2, 3)");
      Assert.True(v.IsError);
    }

    // --- (sorted:) ---

    [Fact]
    public void Sorted_Numbers_Ascending()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: 3, 1, 4, 1, 5, 9, 2, 6)");
      var arr = v.AsArray;
      Assert.Equal(new double[] { 1, 1, 2, 3, 4, 5, 6, 9 },
        new[] { arr[0].AsNumber, arr[1].AsNumber, arr[2].AsNumber, arr[3].AsNumber,
                arr[4].AsNumber, arr[5].AsNumber, arr[6].AsNumber, arr[7].AsNumber });
    }

    [Fact]
    public void Sorted_Strings_Ordinal()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: \"cherry\", \"apple\", \"banana\")");
      var arr = v.AsArray;
      Assert.Equal("apple", arr[0].AsString);
      Assert.Equal("banana", arr[1].AsString);
      Assert.Equal("cherry", arr[2].AsString);
    }

    [Fact]
    public void Sorted_SingleArrayArg()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: (a: 3, 1, 2))");
      Assert.Equal(1, v.AsArray[0].AsNumber);
      Assert.Equal(3, v.AsArray[2].AsNumber);
    }

    [Fact]
    public void Sorted_SingleItem_Identity()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: 42)");
      Assert.Single(v.AsArray);
      Assert.Equal(42, v.AsArray[0].AsNumber);
    }

    [Fact]
    public void Sorted_EmptyArray_ReturnsEmpty()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: (a:))");
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Empty(v.AsArray);
    }

    [Fact]
    public void Sorted_MixedNumbersAndStrings_NumbersFirst()
    {
      // Reference's own documented example: (sorted: ...$a) over
      // (a:'A','C','E','G',2,1) produces (a:1,2,"A","C","E","G").
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: \"A\", \"C\", \"E\", \"G\", 2, 1)");
      var arr = v.AsArray;
      Assert.Equal(6, arr.Count);
      Assert.Equal(1, arr[0].AsNumber);
      Assert.Equal(2, arr[1].AsNumber);
      Assert.Equal("A", arr[2].AsString);
      Assert.Equal("C", arr[3].AsString);
      Assert.Equal("E", arr[4].AsString);
      Assert.Equal("G", arr[5].AsString);
    }

    [Fact]
    public void Sorted_NumericString_StaysWithStrings()
    {
      // Documented divergence pin: reference stringifies numbers into its
      // NaturalSort so "1" would interleave ahead of 2; ours keeps the kinds
      // apart — every number before every string.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: \"1\", 2)");
      Assert.Equal(2, v.AsArray[0].AsNumber);
      Assert.Equal("1", v.AsArray[1].AsString);
    }

    [Fact]
    public void Sorted_NonOrderableKind_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: true, false)");
      Assert.True(v.IsError);
      Assert.Contains("'via' lambda", v.ErrorMessage);
    }

    [Fact]
    public void Sorted_ZeroValues_ReturnsEmptyArray()
    {
      // Reference: "As of 3.3.0, giving zero or more values (after the
      // optional lambda) to (sorted:) will cause an empty array to be
      // returned, rather than causing an error to occur."
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted:)");
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Empty(v.AsArray);
    }

    [Fact]
    public void Sorted_ViaLambda_SortsByProducedKey()
    {
      // Reference's example: (sorted: via its length * -1, "Gus", "Arthur",
      // "William") produces (a: "William", "Arthur", "Gus").
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: via its length * -1, \"Gus\", \"Arthur\", \"William\")");
      var arr = v.AsArray;
      Assert.Equal("William", arr[0].AsString);
      Assert.Equal("Arthur", arr[1].AsString);
      Assert.Equal("Gus", arr[2].AsString);
    }

    [Fact]
    public void Sorted_ViaLambda_EqualKeys_StableOrder()
    {
      // Reference's stability example: sorting only by first letter always
      // produces (a:"Alice","Bob","Blake","Bella","Bertrude") — equal-keyed
      // values keep their given order.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx,
        "(sorted: via its 1st, \"Bob\", \"Alice\", \"Blake\", \"Bella\", \"Bertrude\")");
      var arr = v.AsArray;
      Assert.Equal("Alice", arr[0].AsString);
      Assert.Equal("Bob", arr[1].AsString);
      Assert.Equal("Blake", arr[2].AsString);
      Assert.Equal("Bella", arr[3].AsString);
      Assert.Equal("Bertrude", arr[4].AsString);
    }

    [Fact]
    public void Sorted_ViaLambda_SortsNonScalarItemsByKey()
    {
      // With a lambda, the values themselves may be any kind — only the
      // produced keys must be numbers or strings.
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx,
        "(sorted: via its name, (dm: \"name\", \"Zed\"), (dm: \"name\", \"Amy\"))");
      var arr = v.AsArray;
      Assert.Equal("Amy", arr[0].AsDatamap["name"].AsString);
      Assert.Equal("Zed", arr[1].AsDatamap["name"].AsString);
    }

    [Fact]
    public void Sorted_ViaLambda_NonViaLambda_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: _x where _x > 0, 1, 2)");
      Assert.True(v.IsError);
      Assert.Contains("'via' lambda", v.ErrorMessage);
    }

    [Fact]
    public void Sorted_ViaLambda_NonScalarKey_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Eval(reg, ctx, "(sorted: via (a: it), 1, 2)");
      Assert.True(v.IsError);
      Assert.Contains("number or string", v.ErrorMessage);
    }

    [Fact]
    public void Sorted_DoesNotMutateInputArray()
    {
      var (reg, ctx) = Setup();
      ctx.Store.Set("nums", false, HarloweValue.OfArray(new List<HarloweValue>
      {
        HarloweValue.OfNumber(3), HarloweValue.OfNumber(1), HarloweValue.OfNumber(2)
      }));
      var v = Eval(reg, ctx, "(sorted: $nums)");
      Assert.Equal(1, v.AsArray[0].AsNumber);
      // original array order untouched
      var original = ctx.Store.Get("nums", false).AsArray;
      Assert.Equal(3, original[0].AsNumber);
    }
  }
}
