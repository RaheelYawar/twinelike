using System.Collections.Generic;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  public class ExpressionEvaluatorTests
  {
    // Helpers ----------------------------------------------------------------

    private static HarloweValue Eval(string source, IVariableStore store = null,
                                     IEvaluationContext context = null, IMacroInvoker macros = null)
    {
      store = store ?? new HarloweVariableStore();
      var tokens = new HarloweTokenizer().Tokenize("(_:" + source + ")");
      // Wrap source in a fake macro so the body parser hands the tokens to the
      // expression parser. Then pull out the single argument.
      var parser = new HarloweExpressionParser();
      // Skip the MacroOpen token, parse one expression, ignore the rest.
      var cursor = new TokenCursor(tokens);
      cursor.Advance(); // consume MacroOpen
      var node = parser.ParseExpression(cursor);
      var evaluator = new ExpressionEvaluator(store, context, macros);
      return evaluator.Evaluate(node);
    }

    private class StubContext : IEvaluationContext
    {
      public HarloweValue Time { get; set; }
      public HarloweValue Visits { get; set; }
      public HarloweValue Passage { get; set; }
    }

    private class StubInvoker : IMacroInvoker
    {
      public System.Func<string, List<HarloweValue>, HarloweValue> Handler;
      public HarloweValue Invoke(string name, List<HarloweValue> args) => Handler(name, args);
    }

    // Literals ---------------------------------------------------------------

    [Fact]
    public void EvaluatesNumberLiteral() => Assert.Equal(5, Eval("5").AsNumber);

    [Fact]
    public void EvaluatesStringLiteral() => Assert.Equal("hi", Eval("\"hi\"").AsString);

    [Fact]
    public void EvaluatesBoolLiteral() => Assert.True(Eval("true").AsBool);

    // Variables --------------------------------------------------------------

    [Fact]
    public void ReadsStoryVariable()
    {
      var store = new HarloweVariableStore();
      store.Set("hp", false, HarloweValue.OfNumber(10));
      Assert.Equal(10, Eval("$hp", store).AsNumber);
    }

    [Fact]
    public void ReadsTempVariable()
    {
      var store = new HarloweVariableStore();
      store.Set("loop", true, HarloweValue.OfNumber(3));
      Assert.Equal(3, Eval("_loop", store).AsNumber);
    }

    [Fact]
    public void UnsetVariable_ReturnsError()
    {
      var v = Eval("$missing");
      Assert.True(v.IsError);
      Assert.Contains("$missing", v.ErrorMessage);
    }

    // Identifiers ------------------------------------------------------------

    [Fact]
    public void ItIdentifier_ResolvesFromStore()
    {
      var store = new HarloweVariableStore();
      store.Set("a", false, HarloweValue.OfNumber(7));
      Assert.Equal(7, Eval("it", store).AsNumber);
    }

    [Fact]
    public void ItIdentifier_ErrorsBeforeAnySet()
    {
      var v = Eval("it");
      Assert.True(v.IsError);
    }

    [Fact]
    public void TimeIdentifier_ReadsContext()
    {
      var ctx = new StubContext { Time = HarloweValue.OfNumber(1234) };
      Assert.Equal(1234, Eval("time", null, ctx).AsNumber);
    }

    [Fact]
    public void VisitsIdentifier_ReadsContext()
    {
      var ctx = new StubContext { Visits = HarloweValue.OfNumber(2) };
      Assert.Equal(2, Eval("visits", null, ctx).AsNumber);
      Assert.Equal(2, Eval("visit", null, ctx).AsNumber);
    }

    [Fact]
    public void UnknownIdentifier_ReturnsError()
    {
      var v = Eval("foobar");
      Assert.True(v.IsError);
      Assert.Contains("foobar", v.ErrorMessage);
    }

    // Arithmetic -------------------------------------------------------------

    [Fact]
    public void Addition_Numbers() => Assert.Equal(7, Eval("3 + 4").AsNumber);

    [Fact]
    public void Addition_Strings() => Assert.Equal("ab", Eval("\"a\" + \"b\"").AsString);

    [Fact]
    public void Addition_StringPlusNumber_Errors()
    {
      var v = Eval("\"a\" + 1");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Subtraction() => Assert.Equal(1, Eval("3 - 2").AsNumber);

    [Fact]
    public void Multiplication() => Assert.Equal(6, Eval("3 * 2").AsNumber);

    [Fact]
    public void Division() => Assert.Equal(2.5, Eval("5 / 2").AsNumber);

    [Fact]
    public void Division_ByZero_Errors()
    {
      var v = Eval("5 / 0");
      Assert.True(v.IsError);
      Assert.Contains("zero", v.ErrorMessage);
    }

    [Fact]
    public void UnaryMinus() => Assert.Equal(-5, Eval("-5").AsNumber);

    [Fact]
    public void UnaryPlus_NoOp() => Assert.Equal(5, Eval("+5").AsNumber);

    [Fact]
    public void UnaryMinus_OnString_Errors() => Assert.True(Eval("-\"hi\"").IsError);

    [Fact]
    public void Precedence_MultBindsTighterThanAdd() => Assert.Equal(7, Eval("1 + 2 * 3").AsNumber);

    // Comparisons ------------------------------------------------------------

    [Fact]
    public void LessThan() => Assert.True(Eval("1 < 2").AsBool);

    [Fact]
    public void LessOrEqual()
    {
      Assert.True(Eval("2 <= 2").AsBool);
      Assert.False(Eval("3 <= 2").AsBool);
    }

    [Fact]
    public void Greater()
    {
      Assert.True(Eval("3 > 2").AsBool);
      Assert.False(Eval("2 > 2").AsBool);
    }

    [Fact]
    public void GreaterOrEqual() => Assert.True(Eval("2 >= 2").AsBool);

    [Fact]
    public void Compare_OnString_Errors() => Assert.True(Eval("\"a\" < \"b\"").IsError);

    // Identity ---------------------------------------------------------------

    [Fact]
    public void Is_Numbers()
    {
      Assert.True(Eval("1 is 1").AsBool);
      Assert.False(Eval("1 is 2").AsBool);
    }

    [Fact]
    public void IsNot_ReturnsBool() => Assert.True(Eval("1 is not 2").AsBool);

    [Fact]
    public void Is_DifferentKinds_ReturnsFalse() => Assert.False(Eval("1 is \"1\"").AsBool);

    // Logical ----------------------------------------------------------------

    [Fact]
    public void And()
    {
      Assert.True(Eval("true and true").AsBool);
      Assert.False(Eval("true and false").AsBool);
    }

    [Fact]
    public void Or()
    {
      Assert.True(Eval("true or false").AsBool);
      Assert.False(Eval("false or false").AsBool);
    }

    [Fact]
    public void And_NumericOperand_Errors() => Assert.True(Eval("true and 1").IsError);

    [Fact]
    public void Not()
    {
      Assert.False(Eval("not true").AsBool);
      Assert.True(Eval("not false").AsBool);
    }

    [Fact]
    public void Not_OnNumber_Errors() => Assert.True(Eval("not 5").IsError);

    // Assignment (to / into) -------------------------------------------------

    [Fact]
    public void To_AssignsToStoryVariable()
    {
      var store = new HarloweVariableStore();
      Eval("$hp to 10", store);
      Assert.Equal(10, store.Get("hp", false).AsNumber);
    }

    [Fact]
    public void To_UpdatesItSlot()
    {
      var store = new HarloweVariableStore();
      Eval("$hp to 10", store);
      Assert.Equal(10, store.It.AsNumber);
    }

    [Fact]
    public void Into_AssignsRightHandTarget()
    {
      var store = new HarloweVariableStore();
      Eval("10 into $hp", store);
      Assert.Equal(10, store.Get("hp", false).AsNumber);
    }

    [Fact]
    public void To_RhsIsLiteralExpression()
    {
      var store = new HarloweVariableStore();
      Eval("$hp to 5 + 3", store);
      Assert.Equal(8, store.Get("hp", false).AsNumber);
    }

    [Fact]
    public void To_NonVariableTarget_Errors()
    {
      var v = Eval("5 to 10");
      Assert.True(v.IsError);
      Assert.Contains("variable", v.ErrorMessage);
    }

    [Fact]
    public void To_TempVariable()
    {
      var store = new HarloweVariableStore();
      Eval("_loop to 3", store);
      Assert.Equal(3, store.Get("loop", true).AsNumber);
    }

    // Containment ------------------------------------------------------------

    [Fact]
    public void Contains_String() => Assert.True(Eval("\"hello\" contains \"ell\"").AsBool);

    [Fact]
    public void Contains_String_Negative() => Assert.False(Eval("\"hello\" contains \"xyz\"").AsBool);

    [Fact]
    public void IsIn_IsContainsReversed() => Assert.True(Eval("\"ell\" is in \"hello\"").AsBool);

    [Fact]
    public void Contains_OnNumber_Errors() => Assert.True(Eval("5 contains 1").IsError);

    // Error propagation ------------------------------------------------------

    [Fact]
    public void ErrorOperand_ShortCircuitsBinary()
    {
      // $missing is unset → produces Error → 1 + $missing should be the same Error,
      // not a "type mismatch" error.
      var v = Eval("1 + $missing");
      Assert.True(v.IsError);
      Assert.Contains("$missing", v.ErrorMessage);
    }

    [Fact]
    public void ErrorOperand_ShortCircuitsUnary()
    {
      var v = Eval("not $missing");
      Assert.True(v.IsError);
      Assert.Contains("$missing", v.ErrorMessage);
    }

    [Fact]
    public void ErrorOperand_ShortCircuitsAssignment()
    {
      var store = new HarloweVariableStore();
      var v = Eval("$x to $missing", store);
      Assert.True(v.IsError);
      Assert.Null(store.Get("x", false));  // assignment did not happen
    }

    // Macro calls inside expressions ----------------------------------------

    [Fact]
    public void MacroCall_DispatchesThroughInvoker()
    {
      var invoker = new StubInvoker
      {
        Handler = (name, args) =>
        {
          Assert.Equal("double", name);
          return HarloweValue.OfNumber(args[0].AsNumber * 2);
        }
      };
      var v = Eval("(double: 5)", null, null, invoker);
      Assert.Equal(10, v.AsNumber);
    }

    [Fact]
    public void MacroCall_PreEvaluatesArgs()
    {
      var invoker = new StubInvoker
      {
        Handler = (name, args) => args[0]
      };
      var v = Eval("(id: 1 + 2)", null, null, invoker);
      Assert.Equal(3, v.AsNumber);
    }

    [Fact]
    public void MacroCall_NoInvoker_ReturnsError()
    {
      var v = Eval("(foo: 1)", null, null, null);
      Assert.True(v.IsError);
    }

    [Fact]
    public void MacroCall_ArgEvaluationError_ShortCircuits()
    {
      bool called = false;
      var invoker = new StubInvoker
      {
        Handler = (name, args) => { called = true; return HarloweValue.OfNumber(0); }
      };
      var v = Eval("(foo: $missing)", null, null, invoker);
      Assert.True(v.IsError);
      Assert.False(called);
    }
  }
}
