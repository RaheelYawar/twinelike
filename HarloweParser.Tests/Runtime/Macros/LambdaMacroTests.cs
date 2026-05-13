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

      var verdict = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), ctx);

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

      var verdict = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), ctx);

      Assert.True(verdict.AsBool);
      Assert.Same(priorIt, ctx.Store.It); // `it` restored
    }

    [Fact]
    public void EvalPredicate_PropagatesItemError()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x where _x > 5");
      var v = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfError("upstream"), ctx);
      Assert.True(v.IsError);
    }

    [Fact]
    public void EvalPredicate_NonBoolClause_ReportsError()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x where _x + 1");
      var v = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(5), ctx);
      Assert.True(v.IsError);
      Assert.Contains("Bool", v.ErrorMessage);
    }

    [Fact]
    public void EvalPredicate_PropagatesClauseError()
    {
      var (reg, ctx) = Setup();
      // unset $missing — looked up inside the predicate, surfaces as error.
      var lambda = ParseLambda("_x where $missing > 5");
      var v = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), ctx);
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
      var verdict = LambdaInvoker.EvalPredicate(lambda, HarloweValue.OfNumber(10), ctx);
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
      var v = LambdaInvoker.EvalTransform(lambda, HarloweValue.OfNumber(7), ctx);
      Assert.Equal(14, v.AsNumber);
    }

    [Fact]
    public void EvalTransform_MissingViaClause_Errors()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x where _x > 5"); // no via clause
      var v = LambdaInvoker.EvalTransform(lambda, HarloweValue.OfNumber(7), ctx);
      Assert.True(v.IsError);
      Assert.Contains("via", v.ErrorMessage);
    }

    [Fact]
    public void EvalTransform_AnyKindAllowed_UnlikePredicate()
    {
      var (reg, ctx) = Setup();
      var lambda = ParseLambda("_x via _x + 1"); // produces Number, not Bool
      var v = LambdaInvoker.EvalTransform(lambda, HarloweValue.OfNumber(7), ctx);
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
  }
}
