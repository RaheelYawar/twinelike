using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
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
      // The test driver parses a single arbitrary expression — including the
      // assignment forms `$x to 5` / `5 into $x` that the body parser would
      // only allow inside a (set:)/(put:) arg position. These tests target the
      // evaluator's AssignTo handling directly, so wrap the source in `(set:)`
      // so the parser permits the assignment, then pull the first arg out.
      var tokens = new HarloweTokenizer().Tokenize("(set:" + source + ")");
      var parser = new HarloweExpressionParser();
      var cursor = new TokenCursor(tokens);
      cursor.Advance(); // consume MacroOpen
      var args = parser.ParseArgumentList(cursor, allowAssignment: true);
      var node = args.Count > 0 ? args[0] : null;
      var evaluator = new ExpressionEvaluator(store, context, macros);
      return evaluator.Evaluate(node);
    }

    private class StubContext : IEvaluationContext
    {
      public HarloweValue Time { get; set; }
      public HarloweValue Visits { get; set; }
      public HarloweValue Passage { get; set; }
      public HarloweValue History { get; set; }
      public HarloweValue Turns { get; set; }
    }

    private class StubInvoker : IMacroInvoker
    {
      public System.Func<string, List<HarloweValue>, HarloweValue> Handler;
      public HarloweValue Invoke(string name, List<HarloweValue> args) => Handler(name, args);
      public bool Contains(string name) => true; // stub assumes every name is known
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
    public void Addition_ChangerPlusChanger_Composes()
    {
      // Goes through StandardMacros so (text-style:) is available; the result
      // should be a Changer whose layers stack outer-to-inner.
      var v = EvalP("(text-style: \"bold\") + (text-style: \"italic\")");
      Assert.Equal(HarloweValueKind.Changer, v.Kind);
      Assert.Equal(
        Changer.FromStyle(new StyleSpec { Bold = true }).Compose(
          Changer.FromStyle(new StyleSpec { Italic = true })),
        v.AsChanger);
    }

    [Fact]
    public void Addition_ChangerPlusNumber_Errors()
    {
      var v = EvalP("(text-style: \"bold\") + 5");
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
    public void Modulo() => Assert.Equal(1, Eval("7 % 3").AsNumber);

    [Fact]
    public void Modulo_NegativeDividend_FollowsDividendSign()
    {
      // Matches reference Harlowe (which delegates to JS `%`): the result's
      // sign follows the dividend. -7 % 3 → -1, not 2.
      Assert.Equal(-1, Eval("-7 % 3").AsNumber);
    }

    [Fact]
    public void Modulo_ByZero_Errors()
    {
      var v = Eval("5 % 0");
      Assert.True(v.IsError);
      Assert.Contains("zero", v.ErrorMessage);
    }

    [Fact]
    public void Modulo_NonNumber_Errors()
    {
      var v = Eval("\"abc\" % 2");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Modulo_SamePrecedenceAsMul()
    {
      // 1 + 7 % 3 → 1 + (7 % 3) → 2 (multiplicative binds tighter than additive).
      Assert.Equal(2, Eval("1 + 7 % 3").AsNumber);
    }

    // --- Polymorphic + (Array / Datamap / Boolean) ---

    [Fact]
    public void Add_Arrays_Concatenates()
    {
      // Matches reference Harlowe's "+" entry in ts/twinescript/operations.ts:
      // Array + Array spreads both into a new array, preserving order.
      var v = EvalP("(a:1,2) + (a:3,4)");
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Equal(4, v.AsArray.Count);
      Assert.Equal(1, v.AsArray[0].AsNumber);
      Assert.Equal(2, v.AsArray[1].AsNumber);
      Assert.Equal(3, v.AsArray[2].AsNumber);
      Assert.Equal(4, v.AsArray[3].AsNumber);
    }

    [Fact]
    public void Add_EmptyArrays_ProducesEmpty()
    {
      var v = EvalP("(a:) + (a:)");
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Empty(v.AsArray);
    }

    [Fact]
    public void Add_Datamaps_RhsWinsOnKeyCollision()
    {
      // Reference: "values of keys used on the right side trump those on the
      // left side."
      var v = EvalP("(dm: \"a\", 1, \"b\", 2) + (dm: \"b\", 99, \"c\", 3)");
      Assert.Equal(HarloweValueKind.Datamap, v.Kind);
      var map = v.AsDatamap;
      Assert.Equal(3, map.Count);
      Assert.Equal(1, map["a"].AsNumber);
      Assert.Equal(99, map["b"].AsNumber);  // RHS won.
      Assert.Equal(3, map["c"].AsNumber);
    }

    [Fact]
    public void Add_Booleans_LogicalOr()
    {
      Assert.True(Eval("true + false").AsBool);
      Assert.True(Eval("false + true").AsBool);
      Assert.True(Eval("true + true").AsBool);
      Assert.False(Eval("false + false").AsBool);
    }

    [Fact]
    public void Add_MixedTypes_Errors()
    {
      // doNotCoerce: + of mismatched kinds errors.
      var v = EvalP("(a:1) + 2");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Add_ArrayResult_DoesNotAliasInputs()
    {
      // The concat result is a fresh List; mutating it must not show up in
      // either operand (defensive against future copy-on-write attempts).
      var v = EvalP("(a:1,2) + (a:3)");
      Assert.Equal(3, v.AsArray.Count);
    }

    // --- Polymorphic - (Array / String) ---

    [Fact]
    public void Subtract_Strings_RemovesAllOccurrencesOfRhs()
    {
      // Reference: `"hello" - "l"` → `"heo"` (every occurrence of the RHS
      // substring removed from the LHS).
      Assert.Equal("heo", Eval("\"hello\" - \"l\"").AsString);
    }

    [Fact]
    public void Subtract_Strings_EmptyRhs_LeavesLhsUnchanged()
    {
      // JS `l.split("").join("")` returns l unchanged (split-on-empty
      // explodes to characters, join puts them back). We special-case empty
      // RHS to match without depending on platform string.Replace semantics.
      Assert.Equal("hello", Eval("\"hello\" - \"\"").AsString);
    }

    [Fact]
    public void Subtract_Strings_RhsNotPresent_LeavesLhsUnchanged()
    {
      Assert.Equal("hello", Eval("\"hello\" - \"zzz\"").AsString);
    }

    [Fact]
    public void Subtract_Arrays_RemovesEachRhsElement()
    {
      // Reference: `[1,3,5,3] - [3] = [1,5]` — filter LHS by membership in
      // RHS via `is`-equality. Note both 3s in LHS get removed.
      var v = EvalP("(a:1,3,5,3) - (a:3)");
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Equal(2, v.AsArray.Count);
      Assert.Equal(1, v.AsArray[0].AsNumber);
      Assert.Equal(5, v.AsArray[1].AsNumber);
    }

    [Fact]
    public void Subtract_Arrays_RhsIsMultipleValues()
    {
      var v = EvalP("(a:1,2,3,4,5) - (a:2,4)");
      Assert.Equal(3, v.AsArray.Count);
      Assert.Equal(1, v.AsArray[0].AsNumber);
      Assert.Equal(3, v.AsArray[1].AsNumber);
      Assert.Equal(5, v.AsArray[2].AsNumber);
    }

    [Fact]
    public void Subtract_Arrays_EmptyRhs_LeavesLhsUnchanged()
    {
      var v = EvalP("(a:1,2,3) - (a:)");
      Assert.Equal(3, v.AsArray.Count);
    }

    [Fact]
    public void Subtract_Arrays_NoMatchingElements_LeavesLhsUnchanged()
    {
      var v = EvalP("(a:1,2,3) - (a:99)");
      Assert.Equal(3, v.AsArray.Count);
    }

    [Fact]
    public void Subtract_NumberMinusArray_Errors()
    {
      // doNotCoerce: kinds must match.
      var v = EvalP("5 - (a:1)");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Subtract_BareItemNotInArray_Errors()
    {
      // Reference docs explicitly: "Subtracting 1 element from an array
      // requires it be wrapped in an (a:) macro." `(a:1,2) - 1` is a
      // type-mismatch error in both impls.
      var v = EvalP("(a:1,2) - 1");
      Assert.True(v.IsError);
    }

    [Fact]
    public void Text_AndStr_AreAliases()
    {
      // Reference Harlowe registers both (text:) and (str:) as the same
      // value-to-string coercion. The lambda docs example uses (str: pos);
      // both spellings must work.
      Assert.Equal("5", EvalP("(text: 5)").AsString);
      Assert.Equal("5", EvalP("(str: 5)").AsString);
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
    public void To_ItBindsToAssignmentTarget_NotLastSetVariable()
    {
      // `(set: $a to it + 1)` must read $a's current value, not whatever the
      // most recent Set of any variable stored. An intervening set to a
      // different variable used to leak its value into `it`.
      var store = new HarloweVariableStore();
      Eval("$a to 1", store);
      Eval("$hp to 50", store);
      Eval("$a to it + 1", store);
      Assert.Equal(2, store.Get("a", false).AsNumber);
    }

    [Fact]
    public void To_ItOnUnsetTarget_Errors()
    {
      // First-ever `(set: $hp to it - 10)` reads $hp's (absent) value through
      // `it`, which must surface "'it' is not yet set" rather than silently
      // using an unrelated variable's value.
      var store = new HarloweVariableStore();
      var result = Eval("$hp to it - 10", store);
      Assert.True(result.IsError);
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

    [Fact]
    public void DoesNotContain_String_True() => Assert.True(Eval("\"hello\" does not contain \"xyz\"").AsBool);

    [Fact]
    public void DoesNotContain_String_False() => Assert.False(Eval("\"hello\" does not contain \"ell\"").AsBool);

    [Fact]
    public void IsNotIn_IsDoesNotContainReversed() => Assert.True(Eval("\"xyz\" is not in \"hello\"").AsBool);

    [Fact]
    public void IsNotIn_False() => Assert.False(Eval("\"ell\" is not in \"hello\"").AsBool);

    [Fact]
    public void DoesNotContain_OnNumber_PropagatesError()
    {
      // OpContains errors must not be flipped to true by the negation wrapper.
      var v = Eval("5 does not contain 1");
      Assert.True(v.IsError);
    }

    [Fact]
    public void IsNotIn_OnNumber_PropagatesError()
    {
      var v = Eval("1 is not in 5");
      Assert.True(v.IsError);
    }

    [Fact]
    public void UnknownMacroCall_DoesNotEvaluateAssignmentArg()
    {
      // Expression-position counterpart: `(notamacro: $x to 5)` is now a
      // parse error rather than an evaluation error. The parser rejects
      // `to`/`into` outside (set:)/(put:) so the mutation can't leak even if
      // the inner macro happens to be unknown.
      var store = new HarloweVariableStore();
      var ex = Assert.Throws<HarloweParseException>(
        () => Eval("(notamacro: $x to 5)", store, macros: new MacroRegistry()));
      Assert.Contains("to", ex.Message);
      Assert.Null(store.Get("x", false));
    }

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

    // Property access ('s, of, its) -----------------------------------------
    //
    // The (a:) and (dm:) constructions in these tests need a real macro
    // invoker, so this region uses StandardMacros instead of the StubInvoker.

    private static HarloweValue EvalP(string source, IVariableStore store = null)
    {
      store = store ?? new HarloweVariableStore();
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      registry.Context = new MacroContext { Store = store, Invoker = registry };
      return Eval(source, store, null, registry);
    }

    // Datamap

    [Fact]
    public void Property_Datamap_IdentifierAccessor()
      => Assert.Equal("Bob", EvalP("(dm: \"name\", \"Bob\")'s name").AsString);

    [Fact]
    public void Property_Datamap_StringAccessor()
      => Assert.Equal("Bob", EvalP("(dm: \"name\", \"Bob\")'s \"name\"").AsString);

    [Fact]
    public void Property_Datamap_MissingKey_Errors()
    {
      var v = EvalP("(dm: \"name\", \"Bob\")'s missing");
      Assert.True(v.IsError);
      Assert.Contains("key", v.ErrorMessage);
    }

    [Fact]
    public void Property_Datamap_NumericKey_Errors()
    {
      var v = EvalP("(dm: \"n\", \"B\")'s 5");
      Assert.True(v.IsError);
      Assert.Contains("String", v.ErrorMessage);
    }

    [Fact]
    public void Property_Of_DatamapReversed()
      => Assert.Equal("Bob", EvalP("name of (dm: \"name\", \"Bob\")").AsString);

    [Fact]
    public void Property_Datamap_Chained()
      => Assert.Equal(10, EvalP("(dm: \"p\", (dm: \"hp\", 10))'s p's hp").AsNumber);

    [Fact]
    public void Property_Of_ChainedRightAssoc_NestedDatamap()
    {
      // `name of person of group` with right-associative `of` reads as
      // `name of (person of group)`. `group` is a datamap whose "person"
      // entry is itself a datamap whose "name" entry is the answer.
      var v = EvalP("name of person of (dm: \"person\", (dm: \"name\", \"Bob\"))");
      Assert.Equal("Bob", v.AsString);
    }

    // Array

    [Fact]
    public void Property_Array_Length()
      => Assert.Equal(3, EvalP("(a: 10, 20, 30)'s length").AsNumber);

    [Fact]
    public void Property_Array_FirstIndex()
      => Assert.Equal(10, EvalP("(a: 10, 20, 30)'s 1").AsNumber);

    [Fact]
    public void Property_Array_LastIndex()
      => Assert.Equal(30, EvalP("(a: 10, 20, 30)'s 3").AsNumber);

    [Fact]
    public void Property_Array_OutOfRange_Errors()
    {
      var v = EvalP("(a: 10, 20, 30)'s 4");
      Assert.True(v.IsError);
      Assert.Contains("range", v.ErrorMessage);
    }

    [Fact]
    public void Property_Array_StringIndex_Errors()
      => Assert.True(EvalP("(a: 10)'s \"x\"").IsError);

    [Fact]
    public void Property_Of_Array_LengthReversed()
      => Assert.Equal(2, EvalP("length of (a: 1, 2)").AsNumber);

    // String

    [Fact]
    public void Property_String_Length()
      => Assert.Equal(5, EvalP("\"hello\"'s length").AsNumber);

    [Fact]
    public void Property_String_FirstChar()
      => Assert.Equal("h", EvalP("\"hello\"'s 1").AsString);

    [Fact]
    public void Property_String_OutOfRange_Errors()
      => Assert.True(EvalP("\"hello\"'s 6").IsError);

    [Fact]
    public void Property_String_NonWholeIndex_MessageUsesInvariantNumber()
    {
      // The non-whole index is interpolated into the error; it must format
      // invariantly (".") regardless of host locale.
      var prior = Thread.CurrentThread.CurrentCulture;
      try
      {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        var v = EvalP("\"hello\"'s 2.5");
        Assert.True(v.IsError);
        Assert.Contains("got 2.5", v.ErrorMessage);
      }
      finally { Thread.CurrentThread.CurrentCulture = prior; }
    }

    // Surrogate-pair (astral) characters count and index as one code point,
    // matching reference Harlowe's code-point string model. U+1F40E is 🐎.
    [Fact]
    public void Property_String_Length_CountsAstralCharAsOne()
      => Assert.Equal(2, EvalP("\"a🐎\"'s length").AsNumber);

    [Fact]
    public void Property_String_Index_ReturnsWholeAstralChar()
      => Assert.Equal("🐎", EvalP("\"a🐎\"'s 2nd").AsString);

    [Fact]
    public void Property_String_FirstChar_AstralLeading()
      => Assert.Equal("🐎", EvalP("\"🐎b\"'s 1st").AsString);

    [Fact]
    public void Property_String_LastChar_PastAstral()
      => Assert.Equal("b", EvalP("\"a🐎b\"'s last").AsString);

    [Fact]
    public void Property_String_SecondLast_IsAstralChar()
      => Assert.Equal("🐎", EvalP("\"a🐎b\"'s 2ndlast").AsString);

    [Fact]
    public void Property_String_UnknownIdentifier_Errors()
      => Assert.True(EvalP("\"hello\"'s notalength").IsError);

    // its shorthand — `its` reads from the store's `it` slot, which is
    // updated by every Set. Seeding via `to` confirms the integration end-to-end.

    [Fact]
    public void Property_Its_AfterTo()
    {
      var store = new HarloweVariableStore();
      EvalP("$x to (dm: \"name\", \"Bob\")", store);
      Assert.Equal("Bob", EvalP("its name", store).AsString);
    }

    // Number/Bool — no properties

    [Fact]
    public void Property_OnNumber_Errors()
      => Assert.True(EvalP("5's length").IsError);

    // Ordinal accessors (1st, 2nd, last, 2ndlast) ---------------------------

    // Array — forward ordinals

    [Fact]
    public void Ordinal_Array_FirstOrdinal()
      => Assert.Equal(10, EvalP("(a: 10, 20, 30)'s 1st").AsNumber);

    [Fact]
    public void Ordinal_Array_SecondOrdinal()
      => Assert.Equal(20, EvalP("(a: 10, 20, 30)'s 2nd").AsNumber);

    [Fact]
    public void Ordinal_Array_ThirdOrdinal()
      => Assert.Equal(30, EvalP("(a: 10, 20, 30)'s 3rd").AsNumber);

    [Fact]
    public void Ordinal_Array_FourthOrdinal()
      => Assert.Equal(40, EvalP("(a: 10, 20, 30, 40)'s 4th").AsNumber);

    [Fact]
    public void Ordinal_Array_OutOfRange_Errors()
    {
      var v = EvalP("(a: 10, 20)'s 5th");
      Assert.True(v.IsError);
      Assert.Contains("range", v.ErrorMessage);
    }

    // Array — back-anchored ordinals

    [Fact]
    public void Ordinal_Array_Last()
      => Assert.Equal(30, EvalP("(a: 10, 20, 30)'s last").AsNumber);

    [Fact]
    public void Ordinal_Array_SecondLast()
      => Assert.Equal(20, EvalP("(a: 10, 20, 30)'s 2ndlast").AsNumber);

    [Fact]
    public void Ordinal_Array_ThirdLast()
      => Assert.Equal(10, EvalP("(a: 10, 20, 30)'s 3rdlast").AsNumber);

    [Fact]
    public void Ordinal_Array_LastOnSingleton()
      => Assert.Equal(10, EvalP("(a: 10)'s last").AsNumber);

    [Fact]
    public void Ordinal_Array_NthLastOutOfRange_Errors()
      => Assert.True(EvalP("(a: 10, 20)'s 5thlast").IsError);

    // String — forward and back ordinals

    [Fact]
    public void Ordinal_String_First()
      => Assert.Equal("h", EvalP("\"hello\"'s 1st").AsString);

    [Fact]
    public void Ordinal_String_Last()
      => Assert.Equal("o", EvalP("\"hello\"'s last").AsString);

    [Fact]
    public void Ordinal_String_SecondLast()
      => Assert.Equal("l", EvalP("\"hello\"'s 2ndlast").AsString);

    [Fact]
    public void Ordinal_String_OutOfRange_Errors()
      => Assert.True(EvalP("\"hi\"'s 5th").IsError);

    // `of` form — same dispatch path, opposite operand order

    [Fact]
    public void Ordinal_OfForm_Array()
      => Assert.Equal(10, EvalP("1st of (a: 10, 20)").AsNumber);

    [Fact]
    public void Ordinal_OfForm_LastOnString()
      => Assert.Equal("o", EvalP("last of \"hello\"").AsString);

    // Datamap — ordinals are not keys; the existing "no key" message stands

    [Fact]
    public void Ordinal_Datamap_Errors()
    {
      var v = EvalP("(dm: \"name\", \"Bob\")'s 1st");
      Assert.True(v.IsError);
      Assert.Contains("key", v.ErrorMessage);
    }

    [Fact]
    public void Ordinal_Datamap_LastErrors()
      => Assert.True(EvalP("(dm: \"name\", \"Bob\")'s last").IsError);

    // Primitives — ordinals don't apply

    [Fact]
    public void Ordinal_OnNumber_Errors()
      => Assert.True(EvalP("5's last").IsError);

    // Hook references --------------------------------------------------------

    [Fact]
    public void HookRef_EvaluatesToHookNameValue()
    {
      var v = Eval("?cake");
      Assert.Equal(HarloweValueKind.HookName, v.Kind);
      Assert.Equal("cake", v.AsHookName.Name);
      Assert.Empty(v.AsHookName.Steps);
    }

    [Fact]
    public void HookRef_CarriesOrdinalSteps()
    {
      var v = Eval("?cake's last");
      Assert.Equal(HarloweValueKind.HookName, v.Kind);
      var step = Assert.Single(v.AsHookName.Steps);
      Assert.Equal(1, step.Index);
      Assert.True(step.FromEnd);
    }

    [Fact]
    public void HookName_Equality_StructuralAndCaseInsensitive()
    {
      Assert.Equal(Eval("?cake"), Eval("?CAKE"));
      Assert.NotEqual(Eval("?cake"), Eval("?pie"));
      Assert.Equal(Eval("?cake's 1st"), Eval("?cake's 1st"));
      Assert.NotEqual(Eval("?cake's 1st"), Eval("?cake's last"));
    }

    [Fact]
    public void HookName_HasNoVisibleText()
      => Assert.Equal(string.Empty, Eval("?cake").ToHarloweString());

    // contains / is in — Array and Datamap containers -------------------------

    private static HarloweVariableStore StoreWith(string name, HarloweValue value)
    {
      var store = new HarloweVariableStore();
      store.Set(name, false, value);
      return store;
    }

    private static HarloweValue Numbers(params double[] ns)
    {
      var items = new List<HarloweValue>();
      foreach (var n in ns) items.Add(HarloweValue.OfNumber(n));
      return HarloweValue.OfArray(items);
    }

    private static HarloweValue OneKeyMap(string key, double value)
      => HarloweValue.OfDatamap(new Dictionary<string, HarloweValue> { [key] = HarloweValue.OfNumber(value) });

    [Fact]
    public void Contains_Array_FindsElement()
      => Assert.True(Eval("$arr contains 2", StoreWith("arr", Numbers(1, 2, 3))).AsBool);

    [Fact]
    public void Contains_Array_MissingElement_False()
      => Assert.False(Eval("$arr contains 9", StoreWith("arr", Numbers(1, 2, 3))).AsBool);

    [Fact]
    public void IsIn_Array_IsContainsReversed()
      => Assert.True(Eval("3 is in $arr", StoreWith("arr", Numbers(1, 2, 3))).AsBool);

    [Fact]
    public void DoesNotContain_Array_Negates()
      => Assert.True(Eval("$arr does not contain 9", StoreWith("arr", Numbers(1, 2, 3))).AsBool);

    [Fact]
    public void Contains_Datamap_ChecksKeys()
    {
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      Assert.True(Eval("$dm contains \"hp\"", store).AsBool);
      Assert.False(Eval("$dm contains \"mp\"", store).AsBool);
    }

    [Fact]
    public void Contains_Datamap_NonStringKey_Errors()
    {
      var v = Eval("$dm contains 5", StoreWith("dm", OneKeyMap("hp", 10)));
      Assert.True(v.IsError);
      Assert.Contains("Datamap key must be a String", v.ErrorMessage);
    }

    // passage identifier -------------------------------------------------------

    [Fact]
    public void Passage_Identifier_ReadsContext()
    {
      var ctx = new StubContext { Passage = HarloweValue.OfString("P1") };
      Assert.Equal("P1", Eval("passage", null, ctx).AsString);
    }

    [Fact]
    public void Passage_Identifier_WithoutContext_Errors()
    {
      var v = Eval("passage");
      Assert.True(v.IsError);
      Assert.Contains("outside a story session", v.ErrorMessage);
    }

    // Polymorphic + / - on unsupported kinds ----------------------------------

    [Fact]
    public void Plus_UnsupportedKinds_Errors()
    {
      // Same-kind but not a kind + supports: HookName + HookName falls through
      // to the type error (Bool + Bool is logical OR, so it can't be used here).
      var v = Eval("?a + ?b");
      Assert.True(v.IsError);
      Assert.Contains("+ does not apply", v.ErrorMessage);
    }

    [Fact]
    public void Minus_UnsupportedKinds_Errors()
    {
      var v = Eval("true - false");
      Assert.True(v.IsError);
      Assert.Contains("- does not apply", v.ErrorMessage);
    }

    // Property / index error paths --------------------------------------------

    [Fact]
    public void ArrayProperty_Unknown_Errors()
    {
      var v = Eval("$arr's foo", StoreWith("arr", Numbers(1)));
      Assert.True(v.IsError);
      Assert.Contains("no property 'foo'", v.ErrorMessage);
    }

    [Fact]
    public void DatamapComputedKey_Missing_Errors()
    {
      var v = Eval("$dm's (\"mp\")", StoreWith("dm", OneKeyMap("hp", 10)));
      Assert.True(v.IsError);
      Assert.Contains("no key 'mp'", v.ErrorMessage);
    }

    [Fact]
    public void StringComputedIndex_NonNumber_Errors()
    {
      var v = Eval("\"abc\"'s (true)");
      Assert.True(v.IsError);
      Assert.Contains("string index must be a Number", v.ErrorMessage);
    }

    [Fact]
    public void ComputedAccessorOnBool_Errors()
    {
      var v = Eval("$b's (1)", StoreWith("b", HarloweValue.OfBool(true)));
      Assert.True(v.IsError);
      Assert.Contains("has no properties", v.ErrorMessage);
    }

    [Fact]
    public void ArrayComputedIndex_Fractional_Errors()
    {
      var v = Eval("$arr's (1.5)", StoreWith("arr", Numbers(1, 2)));
      Assert.True(v.IsError);
      Assert.Contains("whole number", v.ErrorMessage);
    }

    // Lambda values ------------------------------------------------------------

    [Fact]
    public void Lambdas_EqualOnlyByIdentity()
    {
      var lambda = Eval("where it > 1");
      Assert.Equal(HarloweValueKind.Lambda, lambda.Kind);

      var store = new HarloweVariableStore();
      store.Set("a", false, lambda);
      store.Set("b", false, lambda);
      Assert.True(Eval("$a is $b", store).AsBool);

      store.Set("c", false, Eval("where it > 1"));
      Assert.False(Eval("$a is $c", store).AsBool);
    }

    [Fact]
    public void HookRefStep_HashMatchesEquality()
    {
      var a = new HookRefStep { Index = 2, FromEnd = true };
      var b = new HookRefStep { Index = 2, FromEnd = true };
      Assert.Equal(a, b);
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
      Assert.NotEqual(a, new HookRefStep { Index = 2, FromEnd = false });
    }

    [Fact]
    public void Lambda_HasNoVisibleText_AndStableHash()
    {
      var lambda = Eval("where it > 1");
      Assert.Equal(string.Empty, lambda.ToHarloweString());
      Assert.Equal(lambda.GetHashCode(), lambda.GetHashCode());
    }

    // Changer equality through `is` — recurses into per-patch structural
    // equality (ClearStyles/Iteration/Revision/Interaction patches) ------------

    private static IMacroInvoker Registry()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = reg };
      reg.Context = ctx;
      return reg;
    }

    [Fact]
    public void Changers_RevisionPatch_EqualByModeAndTarget()
    {
      var reg = Registry();
      Assert.True(Eval("(replace: ?x) is (replace: ?x)", null, null, reg).AsBool);
      Assert.False(Eval("(replace: ?x) is (append: ?x)", null, null, reg).AsBool);
      Assert.False(Eval("(replace: ?x) is (replace: ?y)", null, null, reg).AsBool);
    }

    [Fact]
    public void Changers_InteractionPatch_EqualByKindAndTarget()
    {
      var reg = Registry();
      Assert.True(Eval("(click: \"x\") is (click: \"x\")", null, null, reg).AsBool);
      Assert.False(Eval("(click: \"x\") is (click: \"y\")", null, null, reg).AsBool);
      Assert.False(Eval("(click: \"x\") is (mouseover: \"x\")", null, null, reg).AsBool);
    }

    [Fact]
    public void Changers_ClearStylesPatch_Equal()
    {
      var reg = Registry();
      Assert.True(Eval("(text-style: \"none\") is (text-style: \"none\")", null, null, reg).AsBool);
    }

    [Fact]
    public void Changers_IterationPatch_LambdaIdentityMakesFreshForsUnequal()
    {
      // (for:)'s lambda compares by reference — two separately-written (for:)s
      // are never equal, matching LambdaValue's identity discipline.
      var reg = Registry();
      Assert.False(Eval("(for: each _x, 1) is (for: each _x, 1)", null, null, reg).AsBool);
    }

    // Property assignment — (set: $x's key to v), (put: v into $x's key) ------
    //
    // The write path resolves the target chain before the RHS runs and
    // rebuilds it copy-on-write (reference varref.ts's #mutateRight), so an
    // error anywhere means zero mutation and aliased collections never see
    // each other's writes.

    [Fact]
    public void PropertyAssign_Datamap_IdentifierKey()
    {
      var store = new HarloweVariableStore();
      EvalP("$person to (dm: \"name\", \"Ann\")", store);
      var v = EvalP("$person's name to \"Bob\"", store);
      Assert.False(v.IsError);
      Assert.Equal("Bob", EvalP("$person's name", store).AsString);
    }

    [Fact]
    public void PropertyAssign_Datamap_ComputedKey()
    {
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      var v = EvalP("$dm's (\"h\" + \"p\") to 3", store);
      Assert.False(v.IsError);
      Assert.Equal(3, EvalP("$dm's hp", store).AsNumber);
    }

    [Fact]
    public void PropertyAssign_Datamap_NewKeyCreated()
    {
      // Reference: a final missing key is created by a plain Map.set.
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      var v = EvalP("$dm's mp to 5", store);
      Assert.False(v.IsError);
      Assert.Equal(5, EvalP("$dm's mp", store).AsNumber);
      Assert.Equal(10, EvalP("$dm's hp", store).AsNumber);
    }

    [Fact]
    public void PropertyAssign_Datamap_NonStringComputedKey_Errors()
    {
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      var v = EvalP("$dm's (5) to 1", store);
      Assert.True(v.IsError);
      Assert.Equal("the datamap can only have string data names, not a Number.", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_Array_OrdinalForms()
    {
      var store = StoreWith("arr", Numbers(1, 2, 3));
      Assert.False(EvalP("$arr's 1st to 10", store).IsError);
      Assert.False(EvalP("$arr's last to 30", store).IsError);
      Assert.False(EvalP("$arr's 2ndlast to 20", store).IsError);
      Assert.False(EvalP("$arr's (2) to 22", store).IsError);
      var arr = store.Get("arr", false).AsArray;
      Assert.Equal(10, arr[0].AsNumber);
      Assert.Equal(22, arr[1].AsNumber);
      Assert.Equal(30, arr[2].AsNumber);
    }

    [Fact]
    public void PropertyAssign_Array_AppendAtCountPlusOne()
    {
      // Deliberate divergence from reference's sparse-array growth: Count+1
      // appends, anything beyond errors.
      var store = StoreWith("arr", Numbers(1, 2, 3));
      var v = EvalP("$arr's 4th to 99", store);
      Assert.False(v.IsError);
      var arr = store.Get("arr", false).AsArray;
      Assert.Equal(4, arr.Count);
      Assert.Equal(99, arr[3].AsNumber);
    }

    [Fact]
    public void PropertyAssign_Array_BeyondAppend_Errors()
    {
      var store = StoreWith("arr", Numbers(1, 2, 3));
      var v = EvalP("$arr's 6th to 1", store);
      Assert.True(v.IsError);
      Assert.Equal("array index 6 is out of range (1..4)", v.ErrorMessage);
      Assert.Equal(3, store.Get("arr", false).AsArray.Count);
    }

    [Fact]
    public void PropertyAssign_Array_LastOnEmpty_Errors()
    {
      var store = StoreWith("arr", Numbers());
      var v = EvalP("$arr's last to 1", store);
      Assert.True(v.IsError);
      Assert.Contains("out of range (1..1)", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_Array_Length_Errors()
    {
      var store = StoreWith("arr", Numbers(1, 2));
      var v = EvalP("$arr's length to 5", store);
      Assert.True(v.IsError);
      Assert.Equal("I can't forcibly alter the 'length' of the array.", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_Array_NonOrdinalName_Errors()
    {
      var store = StoreWith("arr", Numbers(1, 2));
      var v = EvalP("$arr's foo to 5", store);
      Assert.True(v.IsError);
      Assert.Equal("the array can only have position data names ('3rd', '1st', (5), etc.), not 'foo'.", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_Array_FractionalComputedIndex_Errors()
    {
      var store = StoreWith("arr", Numbers(1, 2));
      var v = EvalP("$arr's (1.5) to 9", store);
      Assert.True(v.IsError);
      Assert.Contains("whole number", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_String_ReplacesCodePoint()
    {
      var store = StoreWith("s", HarloweValue.OfString("cat"));
      var v = EvalP("$s's 1st to \"b\"", store);
      Assert.False(v.IsError);
      Assert.Equal("bat", store.Get("s", false).AsString);
      Assert.False(EvalP("$s's last to \"p\"", store).IsError);
      Assert.Equal("bap", store.Get("s", false).AsString);
    }

    [Fact]
    public void PropertyAssign_String_AstralPositionsAndValues()
    {
      // Positions count code points (an astral emoji is one), and a
      // surrogate-pair replacement value is one code point too.
      var store = StoreWith("s", HarloweValue.OfString("a\U0001F600c"));
      Assert.False(EvalP("$s's 2nd to \"x\"", store).IsError);
      Assert.Equal("axc", store.Get("s", false).AsString);
      Assert.False(EvalP("$s's 1st to \"\U0001F600\"", store).IsError);
      Assert.Equal("\U0001F600xc", store.Get("s", false).AsString);
    }

    [Fact]
    public void PropertyAssign_String_NonStringValue_Errors()
    {
      var store = StoreWith("s", HarloweValue.OfString("cat"));
      var v = EvalP("$s's 1st to 5", store);
      Assert.True(v.IsError);
      Assert.Equal("I can't put this non-string value, 5, in a string.", v.ErrorMessage);
      Assert.Equal("cat", store.Get("s", false).AsString);
    }

    [Fact]
    public void PropertyAssign_String_WrongLengthValue_Errors()
    {
      var store = StoreWith("s", HarloweValue.OfString("cat"));
      var v = EvalP("$s's 1st to \"ab\"", store);
      Assert.True(v.IsError);
      Assert.Equal("\"ab\" is not the right length to fit into this string location.", v.ErrorMessage);
      Assert.Equal("cat", store.Get("s", false).AsString);
    }

    [Fact]
    public void PropertyAssign_String_NoAppend()
    {
      var store = StoreWith("s", HarloweValue.OfString("cat"));
      var v = EvalP("$s's 4th to \"x\"", store);
      Assert.True(v.IsError);
      Assert.Contains("out of range (1..3)", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_OfForm()
    {
      var store = StoreWith("arr", Numbers(1, 2, 3));
      var v = EvalP("1st of $arr to 5", store);
      Assert.False(v.IsError);
      Assert.Equal(5, EvalP("$arr's 1st", store).AsNumber);
    }

    [Fact]
    public void PropertyAssign_MixedOfAndPossessiveChain()
    {
      // `hp of $chars's hero` — 's binds tighter, so this is
      // `hp of ($chars's hero)`: descend hero, then write hp.
      var store = new HarloweVariableStore();
      EvalP("$chars to (dm: \"hero\", (dm: \"hp\", 10))", store);
      var v = EvalP("hp of $chars's hero to 5", store);
      Assert.False(v.IsError);
      Assert.Equal(5, EvalP("$chars's hero's hp", store).AsNumber);
    }

    [Fact]
    public void PropertyAssign_NestedArrayInDatamap()
    {
      var store = new HarloweVariableStore();
      EvalP("$dm to (dm: \"list\", (a: 1, 2, 3))", store);
      var v = EvalP("$dm's list's 2nd to 9", store);
      Assert.False(v.IsError);
      Assert.Equal(9, EvalP("$dm's list's 2nd", store).AsNumber);
    }

    [Fact]
    public void PropertyAssign_CopyOnWrite_AliasedVariableUnaffected()
    {
      // Set stores references — the aliasing is real but unobservable, because
      // the write clones every container level before mutating.
      var store = new HarloweVariableStore();
      var shared = Numbers(1, 2, 3);
      store.Set("a", false, shared);
      store.Set("b", false, shared);
      Assert.False(EvalP("$a's 1st to 99", store).IsError);
      Assert.Equal(99, EvalP("$a's 1st", store).AsNumber);
      Assert.Equal(1, EvalP("$b's 1st", store).AsNumber);
      Assert.Equal(1, shared.AsArray[0].AsNumber);
    }

    [Fact]
    public void PropertyAssign_CopyOnWrite_UntouchedSiblingsShareReferences()
    {
      var store = new HarloweVariableStore();
      var hero = new Dictionary<string, HarloweValue> { ["hp"] = HarloweValue.OfNumber(10) };
      var villain = new Dictionary<string, HarloweValue> { ["hp"] = HarloweValue.OfNumber(20) };
      store.Set("chars", false, HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["hero"] = HarloweValue.OfDatamap(hero),
        ["villain"] = HarloweValue.OfDatamap(villain)
      }));
      Assert.False(EvalP("$chars's hero's hp to 5", store).IsError);
      var newChars = store.Get("chars", false).AsDatamap;
      Assert.NotSame(hero, newChars["hero"].AsDatamap);
      Assert.Same(villain, newChars["villain"].AsDatamap);
      Assert.Equal(5, newChars["hero"].AsDatamap["hp"].AsNumber);
      Assert.Equal(10, hero["hp"].AsNumber);
    }

    [Fact]
    public void PropertyAssign_ItBindsToSlotCurrentValue()
    {
      var store = StoreWith("arr", Numbers(10, 20));
      var v = EvalP("$arr's 1st to it + 1", store);
      Assert.False(v.IsError);
      Assert.Equal(11, EvalP("$arr's 1st", store).AsNumber);
    }

    [Fact]
    public void PropertyAssign_ItEndsAsAssignedValue()
    {
      var store = StoreWith("arr", Numbers(10, 20));
      EvalP("$arr's 1st to 99", store);
      Assert.Equal(99, store.It.AsNumber);
    }

    [Fact]
    public void PropertyAssign_NewKey_ItUnset_RhsReadingItErrors()
    {
      // A new datamap key has no current value; `it` binds null and only
      // errors if the RHS actually reads it — and nothing is mutated.
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      var v = EvalP("$dm's mp to it + 1", store);
      Assert.True(v.IsError);
      Assert.Contains("'it' is not yet set", v.ErrorMessage);
      Assert.False(store.Get("dm", false).AsDatamap.ContainsKey("mp"));
    }

    [Fact]
    public void PropertyAssign_PutInto()
    {
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      var v = EvalP("5 into $dm's hp", store);
      Assert.False(v.IsError);
      Assert.Equal(5, EvalP("$dm's hp", store).AsNumber);
    }

    [Fact]
    public void PropertyAssign_RhsError_NoMutation()
    {
      var store = StoreWith("arr", Numbers(1, 2));
      var before = store.Get("arr", false);
      var v = EvalP("$arr's 1st to $missing", store);
      Assert.True(v.IsError);
      Assert.Same(before, store.Get("arr", false));
      Assert.Equal(1, before.AsArray[0].AsNumber);
    }

    [Fact]
    public void PropertyAssign_ComputedAccessor_EvaluatedOnce()
    {
      int calls = 0;
      var invoker = new StubInvoker
      {
        Handler = (name, args) => { calls++; return HarloweValue.OfNumber(1); }
      };
      var store = StoreWith("arr", Numbers(10, 20));
      var v = Eval("$arr's (idx:) to 99", store, null, invoker);
      Assert.False(v.IsError);
      Assert.Equal(1, calls);
      Assert.Equal(99, store.Get("arr", false).AsArray[0].AsNumber);
    }

    [Fact]
    public void PropertyAssign_IntermediateMissingKey_Errors()
    {
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      var v = EvalP("$dm's stats's hp to 5", store);
      Assert.True(v.IsError);
      Assert.Contains("no key 'stats'", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_IntermediateArrayBounds_ReadWording()
    {
      // Mid-chain levels are reads: no append slot, read-path range shown.
      var store = StoreWith("arr", Numbers(1, 2));
      var v = EvalP("$arr's 5th's 1st to 5", store);
      Assert.True(v.IsError);
      Assert.Contains("out of range (1..2)", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_ScalarBase_Errors()
    {
      var store = StoreWith("num", HarloweValue.OfNumber(5));
      var v = EvalP("$num's 1st to 1", store);
      Assert.True(v.IsError);
      Assert.Equal("a Number doesn't have data names, let alone '1st'", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_Colour_Errors()
    {
      var store = new HarloweVariableStore();
      EvalP("$col to (rgb: 10, 20, 30)", store);
      var v = EvalP("$col's r to 5", store);
      Assert.True(v.IsError);
      Assert.Equal("I can't modify the colour", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_NonVariableBase_Errors()
    {
      var v = EvalP("(a: 1)'s 1st to 2");
      Assert.True(v.IsError);
      Assert.Equal("I can't (set:) that value, if it isn't stored in a variable.", v.ErrorMessage);

      var put = EvalP("5 into (a: 1)'s 1st");
      Assert.True(put.IsError);
      Assert.Equal("I can't (put:) that value, if it isn't stored in a variable.", put.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_ItsBase_Errors()
    {
      var v = EvalP("its name to 5");
      Assert.True(v.IsError);
      Assert.Contains("isn't stored in a variable", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_UnsetRoot_Errors()
    {
      var v = EvalP("$nope's 1st to 5");
      Assert.True(v.IsError);
      Assert.Contains("$nope is not set", v.ErrorMessage);
    }

    [Fact]
    public void PropertyAssign_TempVariableTarget()
    {
      var store = new HarloweVariableStore();
      store.Set("t", true, Numbers(1, 2));
      var v = EvalP("_t's 1st to 9", store);
      Assert.False(v.IsError);
      Assert.Equal(9, store.Get("t", true).AsArray[0].AsNumber);
    }

    [Fact]
    public void PropertyAssign_MultiArgSet_BothArgsApply()
    {
      // `(set: $a's 1st to 5, $b to 2)` — each arg is an independent
      // assignment expression evaluated in order.
      var store = new HarloweVariableStore();
      store.Set("a", false, Numbers(1, 2));
      var tokens = new HarloweTokenizer().Tokenize("(set: $a's 1st to 5, $b to 2)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var args = new HarloweExpressionParser().ParseArgumentList(cursor, allowAssignment: true);
      Assert.Equal(2, args.Count);
      var evaluator = new ExpressionEvaluator(store, null, null);
      foreach (var arg in args) Assert.False(evaluator.Evaluate(arg).IsError);
      Assert.Equal(5, store.Get("a", false).AsArray[0].AsNumber);
      Assert.Equal(2, store.Get("b", false).AsNumber);
    }

    // (move:) — copy then delete the data-structure source slot ---------------
    //
    // Move work happens in EvaluateMacroArgument's routing of an `into` arg,
    // so these tests parse the arg under a (put:) wrapper (which permits
    // `into` at arg top) and evaluate it as a (move:) argument.

    private static HarloweValue EvalMoveArg(string arg, IVariableStore store = null, IMacroInvoker macros = null)
      => EvalAssignmentArg("move", arg, store, macros);

    private static HarloweValue EvalUnpackArg(string arg, IVariableStore store = null)
      => EvalAssignmentArg("unpack", arg, store, null);

    private static HarloweValue EvalAssignmentArg(string macroName, string arg, IVariableStore store, IMacroInvoker macros)
    {
      store = store ?? new HarloweVariableStore();
      if (macros == null)
      {
        var registry = new MacroRegistry();
        StandardMacros.RegisterAll(registry);
        registry.Context = new MacroContext { Store = store, Invoker = registry };
        macros = registry;
      }
      var tokens = new HarloweTokenizer().Tokenize("(put:" + arg + ")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var args = new HarloweExpressionParser().ParseArgumentList(cursor, allowAssignment: true);
      var evaluator = new ExpressionEvaluator(store, null, macros);
      return evaluator.EvaluateMacroArgument(macroName, args[0]);
    }

    [Fact]
    public void Move_ArrayElement_SplicesOut()
    {
      var store = StoreWith("a", Numbers(1, 2, 3));
      var v = EvalMoveArg("$a's 1st into $b", store);
      Assert.False(v.IsError);
      Assert.Equal(1, store.Get("b", false).AsNumber);
      var arr = store.Get("a", false).AsArray;
      Assert.Equal(2, arr.Count);
      Assert.Equal(2, arr[0].AsNumber);   // spliced, not a hole
      Assert.Equal(3, arr[1].AsNumber);
    }

    [Fact]
    public void Move_ArrayLast()
    {
      var store = StoreWith("a", Numbers(1, 2, 3));
      Assert.False(EvalMoveArg("$a's last into $b", store).IsError);
      Assert.Equal(3, store.Get("b", false).AsNumber);
      Assert.Equal(2, store.Get("a", false).AsArray.Count);
    }

    [Fact]
    public void Move_DatamapKey_Removed()
    {
      var store = new HarloweVariableStore();
      store.Set("dm", false, HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["hp"] = HarloweValue.OfNumber(10),
        ["mp"] = HarloweValue.OfNumber(5)
      }));
      Assert.False(EvalMoveArg("$dm's hp into $b", store).IsError);
      Assert.Equal(10, store.Get("b", false).AsNumber);
      var map = store.Get("dm", false).AsDatamap;
      Assert.False(map.ContainsKey("hp"));
      Assert.Equal(5, map["mp"].AsNumber);
    }

    [Fact]
    public void Move_StringChar_Excised()
    {
      var store = StoreWith("s", HarloweValue.OfString("cat"));
      Assert.False(EvalMoveArg("$s's 2nd into $b", store).IsError);
      Assert.Equal("a", store.Get("b", false).AsString);
      Assert.Equal("ct", store.Get("s", false).AsString);
    }

    [Fact]
    public void Move_StringAstralChar()
    {
      var store = StoreWith("s", HarloweValue.OfString("a\U0001F600c"));
      Assert.False(EvalMoveArg("$s's 2nd into $b", store).IsError);
      Assert.Equal("\U0001F600", store.Get("b", false).AsString);
      Assert.Equal("ac", store.Get("s", false).AsString);
    }

    [Fact]
    public void Move_WholeVariable_SourceKept()
    {
      // Reference's documented contract: "Moving from variable to variable,
      // such as by (move:$p into $q), won't cause $p to be deleted."
      var store = StoreWith("p", HarloweValue.OfNumber(7));
      Assert.False(EvalMoveArg("$p into $q", store).IsError);
      Assert.Equal(7, store.Get("q", false).AsNumber);
      Assert.Equal(7, store.Get("p", false).AsNumber);
    }

    [Fact]
    public void Move_TempVariables()
    {
      var store = new HarloweVariableStore();
      store.Set("src", true, Numbers(1, 2));
      Assert.False(EvalMoveArg("_src's 1st into _dst", store).IsError);
      Assert.Equal(1, store.Get("dst", true).AsNumber);
      Assert.Single(store.Get("src", true).AsArray);
    }

    [Fact]
    public void Move_NonVariableSource_Errors()
    {
      var v = EvalMoveArg("5 into $x");
      Assert.True(v.IsError);
      Assert.Equal("I can't (move:) that value, if it isn't stored in a variable.", v.ErrorMessage);

      var macroBase = EvalMoveArg("(a: 1)'s 1st into $x");
      Assert.True(macroBase.IsError);
      Assert.Contains("isn't stored in a variable", macroBase.ErrorMessage);
    }

    [Fact]
    public void Move_SourceMissingKey_Errors_NothingAssigned()
    {
      // The source read fails first (reference: src.get() errors abort the
      // whole AssignmentRequest) — the destination is never touched.
      var store = StoreWith("dm", OneKeyMap("hp", 10));
      var v = EvalMoveArg("$dm's mp into $x", store);
      Assert.True(v.IsError);
      Assert.Contains("no key 'mp'", v.ErrorMessage);
      Assert.Null(store.Get("x", false));
    }

    [Fact]
    public void Move_UnsetSourceRoot_Errors()
    {
      Assert.Contains("$nope is not set", EvalMoveArg("$nope's 1st into $x").ErrorMessage);
      Assert.Contains("$gone is not set", EvalMoveArg("$gone into $x").ErrorMessage);
    }

    [Fact]
    public void Move_Length_AssignsThenErrors()
    {
      // `length` reads fine but can't be deleted. Reference's order is
      // set-then-delete, so the destination keeps the value and the canSet
      // error surfaces after; the source array is untouched.
      var store = StoreWith("a", Numbers(1, 2, 3));
      var v = EvalMoveArg("$a's length into $x", store);
      Assert.True(v.IsError);
      Assert.Equal("I can't forcibly alter the 'length' of the array.", v.ErrorMessage);
      Assert.Equal(3, store.Get("x", false).AsNumber);
      Assert.Equal(3, store.Get("a", false).AsArray.Count);
    }

    [Fact]
    public void Move_ColourProperty_AssignsThenErrors()
    {
      var store = new HarloweVariableStore();
      EvalP("$col to (rgb: 10, 20, 30)", store);
      var v = EvalMoveArg("$col's r into $x", store);
      Assert.True(v.IsError);
      Assert.Equal("I can't modify the colour", v.ErrorMessage);
      Assert.Equal(10, store.Get("x", false).AsNumber);
    }

    [Fact]
    public void Move_DestPropertyChain()
    {
      var store = StoreWith("a", Numbers(1, 2));
      store.Set("dm", false, OneKeyMap("hp", 10));
      Assert.False(EvalMoveArg("$a's 1st into $dm's slot", store).IsError);
      Assert.Equal(1, store.Get("dm", false).AsDatamap["slot"].AsNumber);
      Assert.Single(store.Get("a", false).AsArray);
    }

    [Fact]
    public void Move_NestedSource_SiblingsShared()
    {
      var store = new HarloweVariableStore();
      var hero = new Dictionary<string, HarloweValue>
      {
        ["hp"] = HarloweValue.OfNumber(10),
        ["mp"] = HarloweValue.OfNumber(4)
      };
      var villain = new Dictionary<string, HarloweValue> { ["hp"] = HarloweValue.OfNumber(20) };
      store.Set("chars", false, HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["hero"] = HarloweValue.OfDatamap(hero),
        ["villain"] = HarloweValue.OfDatamap(villain)
      }));
      Assert.False(EvalMoveArg("$chars's hero's hp into $x", store).IsError);
      Assert.Equal(10, store.Get("x", false).AsNumber);
      var chars = store.Get("chars", false).AsDatamap;
      Assert.False(chars["hero"].AsDatamap.ContainsKey("hp"));
      Assert.Equal(4, chars["hero"].AsDatamap["mp"].AsNumber);
      Assert.Same(villain, chars["villain"].AsDatamap);   // untouched sibling shared
      Assert.Equal(10, hero["hp"].AsNumber);              // original map not mutated (COW)
    }

    [Fact]
    public void Move_ComputedAccessor_EvaluatedOnce()
    {
      // The read and the delete share one resolve — reference compiles the
      // property chain once, so `(move: $a's (idx:) into $b)` must not draw
      // the index twice.
      int calls = 0;
      var invoker = new StubInvoker
      {
        Handler = (name, args) => { calls++; return HarloweValue.OfNumber(2); }
      };
      var store = StoreWith("a", Numbers(10, 20, 30));
      var v = EvalMoveArg("$a's (idx:) into $b", store, invoker);
      Assert.False(v.IsError);
      Assert.Equal(1, calls);
      Assert.Equal(20, store.Get("b", false).AsNumber);
      var arr = store.Get("a", false).AsArray;
      Assert.Equal(2, arr.Count);
      Assert.Equal(10, arr[0].AsNumber);
      Assert.Equal(30, arr[1].AsNumber);
    }

    [Fact]
    public void Move_DestError_SourceNotDeleted()
    {
      // Reference's order: dest.set() errors abort before src.delete().
      var store = StoreWith("a", Numbers(1, 2));
      store.Set("num", false, HarloweValue.OfNumber(5));
      var v = EvalMoveArg("$a's 1st into $num's 1st", store);
      Assert.True(v.IsError);
      Assert.Contains("doesn't have data names", v.ErrorMessage);
      Assert.Equal(2, store.Get("a", false).AsArray.Count);
    }

    [Fact]
    public void Move_OverlappingSourceAndDest()
    {
      // `(move: $a's 1st into $a's 2nd)` — the delete re-fetches containers
      // after the assignment, so both operations land: [1,2,3] → read 1 →
      // assign a[1]=1 → [1,1,3] → delete 1st → [1,3].
      var store = StoreWith("a", Numbers(1, 2, 3));
      Assert.False(EvalMoveArg("$a's 1st into $a's 2nd", store).IsError);
      var arr = store.Get("a", false).AsArray;
      Assert.Equal(2, arr.Count);
      Assert.Equal(1, arr[0].AsNumber);
      Assert.Equal(3, arr[1].AsNumber);
    }

    [Fact]
    public void Move_ItEndsAsMovedValue()
    {
      var store = StoreWith("a", Numbers(9, 8));
      EvalMoveArg("$a's 1st into $b", store);
      Assert.Equal(9, store.It.AsNumber);
    }

    [Fact]
    public void Move_CopyOnWrite_AliasedUnaffected()
    {
      var store = new HarloweVariableStore();
      var shared = Numbers(1, 2, 3);
      store.Set("a", false, shared);
      store.Set("b", false, shared);
      Assert.False(EvalMoveArg("$a's 1st into $c", store).IsError);
      Assert.Equal(2, store.Get("a", false).AsArray.Count);
      Assert.Equal(3, store.Get("b", false).AsArray.Count);   // alias untouched
      Assert.Equal(3, shared.AsArray.Count);
    }

    // Operator discipline (reference's typeChecker pre-pass) ------------------

    [Fact]
    public void ValidateOperators_SetWithInto_Errors()
    {
      // Nothing-assigned coverage is end-to-end in BodyRendererTests
      // (Set_IntoOperator_Errors_NothingAssigned); the pre-pass never touches
      // the store.
      var err = ValidateArgs("set", "(set: 5 into $x, $y to 1)");
      Assert.NotNull(err);
      Assert.Equal("Please say 'to' when using the (set:) macro.", err.ErrorMessage);
    }

    [Fact]
    public void ValidateOperators_PutWithTo_Errors()
    {
      var err = ValidateArgs("put", "(put: $x to 5)");
      Assert.NotNull(err);
      Assert.Equal("Please say 'into' when using the (put:) or (unpack:) macro.", err.ErrorMessage);
    }

    [Fact]
    public void ValidateOperators_MoveWithTo_Errors()
    {
      var err = ValidateArgs("move", "(move: $x to 5)");
      Assert.NotNull(err);
      Assert.Equal("Please say 'into' when using the (move:) macro.", err.ErrorMessage);
    }

    [Fact]
    public void ValidateOperators_NonAssignmentArg_Errors()
    {
      var err = ValidateArgs("set", "(set: 5)");
      Assert.NotNull(err);
      Assert.Contains("requires a 'to' assignment", err.ErrorMessage);
    }

    [Fact]
    public void ValidateOperators_OtherMacros_Unchecked()
      => Assert.Null(ValidateArgs("print", "(print: 5)"));

    private static HarloweValue ValidateArgs(string macroName, string source)
    {
      var evaluator = new ExpressionEvaluator(new HarloweVariableStore(), null, null);
      var tokens = new HarloweTokenizer().Tokenize(source);
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var args = new HarloweExpressionParser().ParseArgumentList(cursor, allowAssignment: true);
      return evaluator.ValidateAssignmentOperators(macroName, args);
    }

    [Fact]
    public void ValidateOperators_UnpackWithTo_Errors()
    {
      var err = ValidateArgs("unpack", "(unpack: $x to 5)");
      Assert.NotNull(err);
      Assert.Equal("Please say 'into' when using the (put:) or (unpack:) macro.", err.ErrorMessage);
    }

    [Fact]
    public void ValidateOperators_UnpackPlainVariableDest_Errors()
    {
      // Reference's XOR dest-shape gate: (unpack:) requires a pattern.
      var err = ValidateArgs("unpack", "(unpack: $a into $x)");
      Assert.NotNull(err);
      Assert.Equal("Please use the (unpack:) macro with arrays or datamaps containing variables to the right of 'into'.", err.ErrorMessage);
    }

    [Fact]
    public void ValidateOperators_PutPatternDest_Errors()
    {
      var err = ValidateArgs("put", "(put: 5 into (a: $x))");
      Assert.NotNull(err);
      Assert.Equal("Please use the (put:) macro with just single variables to the right of 'into'.", err.ErrorMessage);
    }

    [Fact]
    public void ValidateOperators_SetPatternDest_Errors()
    {
      var err = ValidateArgs("set", "(set: (a: $x) to 5)");
      Assert.NotNull(err);
      Assert.Equal("Please use the (set:) macro with just single variables to the left of 'to'.", err.ErrorMessage);
    }

    // (unpack:) — destructuring -----------------------------------------------

    [Fact]
    public void Unpack_ArrayPattern_ReferenceDocExample()
    {
      // Reference doc: `(unpack: (a: 1, 2, 3) into (a: $x, 2, $y))` sets $x
      // to 1 and $y to 3 — the literal 2 is a positional match.
      var store = new HarloweVariableStore();
      var v = EvalUnpackArg("(a: 1, 2, 3) into (a: $x, 2, $y)", store);
      Assert.False(v.IsError);
      Assert.Equal(1, store.Get("x", false).AsNumber);
      Assert.Equal(3, store.Get("y", false).AsNumber);
    }

    [Fact]
    public void Unpack_DatamapPattern_ReferenceDocExample()
    {
      // `(unpack: (dm: "B", 3, "A", 1) into (dm: "A", $x, "B", $y))` → $x=1, $y=3.
      var store = new HarloweVariableStore();
      var v = EvalUnpackArg("(dm: \"B\", 3, \"A\", 1) into (dm: \"A\", $x, \"B\", $y)", store);
      Assert.False(v.IsError);
      Assert.Equal(1, store.Get("x", false).AsNumber);
      Assert.Equal(3, store.Get("y", false).AsNumber);
    }

    [Fact]
    public void Unpack_NestedPatterns_ReferenceDocExample()
    {
      // `(unpack: (a: (a: 1), 2, (dm: "A", 3)) into (a: (a: $x), 2, (dm: "A", $y)))` → $x=1, $y=3.
      var store = new HarloweVariableStore();
      var v = EvalUnpackArg("(a: (a: 1), 2, (dm: \"A\", 3)) into (a: (a: $x), 2, (dm: \"A\", $y))", store);
      Assert.False(v.IsError);
      Assert.Equal(1, store.Get("x", false).AsNumber);
      Assert.Equal(3, store.Get("y", false).AsNumber);
    }

    [Fact]
    public void Unpack_ExtraSourceValues_Ignored()
    {
      var store = new HarloweVariableStore();
      Assert.False(EvalUnpackArg("(a: 1, 2, 3) into (a: $x)", store).IsError);
      Assert.Equal(1, store.Get("x", false).AsNumber);
    }

    [Fact]
    public void Unpack_PatternLongerThanSource_Errors()
    {
      var v = EvalUnpackArg("(a: 1) into (a: $x, $y, $z)");
      Assert.True(v.IsError);
      Assert.Equal("I can't unpack this array because it needs 2 more values.", v.ErrorMessage);

      // Correctly singular (reference's template always pluralises).
      var one = EvalUnpackArg("(a: 1) into (a: $x, $y)");
      Assert.Equal("I can't unpack this array because it needs 1 more value.", one.ErrorMessage);
    }

    [Fact]
    public void Unpack_LiteralMismatch_Errors()
    {
      var store = new HarloweVariableStore();
      var v = EvalUnpackArg("(a: 2, 3) into (a: 3, 2)", store);
      Assert.True(v.IsError);
      Assert.Equal("I tried to unpack, but 3 in the pattern didn't match 2.", v.ErrorMessage);
    }

    [Fact]
    public void Unpack_DatatypeInPattern_ChecksWithoutBinding()
    {
      // Reference's destructure() compares a non-variable pattern position with
      // matches(), so a bare datatype there is a type check on that slot and
      // binds nothing.
      var store = new HarloweVariableStore();
      var v = EvalUnpackArg("(a: 2, \"x\") into (a: num, $s)", store);
      Assert.False(v.IsError);
      Assert.Equal("x", store.Get("s", false).AsString);
      Assert.Null(store.Get("num", false));
    }

    [Fact]
    public void Unpack_DatatypeInPattern_MismatchErrors()
    {
      var v = EvalUnpackArg("(a: 2, \"x\") into (a: str, $s)");
      Assert.True(v.IsError);
      Assert.Equal("I tried to unpack, but str in the pattern didn't match 2.", v.ErrorMessage);
    }

    [Fact]
    public void Unpack_NoVariablesInPattern_Errors()
    {
      // With matching literals the walk binds nothing — reference's doc
      // promises an error when the destination contains no variables.
      var v = EvalUnpackArg("(a: 2, 3) into (a: 2, 3)");
      Assert.True(v.IsError);
      Assert.Equal("I can't store a new value inside the array that isn't in a variable.", v.ErrorMessage);
    }

    [Fact]
    public void Unpack_DatamapMissingKey_Errors()
    {
      var v = EvalUnpackArg("(dm: \"hp\", 10) into (dm: \"mp\", $x)");
      Assert.True(v.IsError);
      Assert.Equal("I can't unpack this datamap because it needs a 'mp' data name.", v.ErrorMessage);
    }

    [Fact]
    public void Unpack_OddDatamapPattern_Errors()
    {
      var v = EvalUnpackArg("(dm: \"hp\", 10) into (dm: \"hp\")");
      Assert.True(v.IsError);
      Assert.Contains("even number", v.ErrorMessage);
    }

    [Fact]
    public void Unpack_NonCollectionSource_MismatchErrors()
    {
      var v = EvalUnpackArg("5 into (a: $x)");
      Assert.True(v.IsError);
      Assert.Equal("I tried to unpack, but the (a:) pattern didn't match 5.", v.ErrorMessage);
    }

    [Fact]
    public void Unpack_SpreadInPattern_Errors()
    {
      var v = EvalUnpackArg("(a: 1, 2) into (a: ...$x)");
      Assert.True(v.IsError);
      Assert.Contains("datatypes", v.ErrorMessage);
    }

    [Fact]
    public void Unpack_TempVarAndPropertyChainTargets()
    {
      var store = new HarloweVariableStore();
      store.Set("dm", false, OneKeyMap("slot", 0));
      var v = EvalUnpackArg("(a: 7, 8) into (a: _t, $dm's slot)", store);
      Assert.False(v.IsError);
      Assert.Equal(7, store.Get("t", true).AsNumber);
      Assert.Equal(8, EvalP("$dm's slot", store).AsNumber);
    }

    [Fact]
    public void Unpack_DuplicateVariable_FirstPositionWins()
    {
      // Bindings execute reversed (reference), so the first occurrence lands last.
      var store = new HarloweVariableStore();
      Assert.False(EvalUnpackArg("(a: 1, 2) into (a: $x, $x)", store).IsError);
      Assert.Equal(1, store.Get("x", false).AsNumber);
    }

    [Fact]
    public void Unpack_ItEndsAsSourceValue()
    {
      var store = new HarloweVariableStore();
      EvalUnpackArg("(a: 1, 2) into (a: $x, $y)", store);
      Assert.Equal(HarloweValueKind.Array, store.It.Kind);
      Assert.Equal(2, store.It.AsArray.Count);
    }

    [Fact]
    public void Unpack_VariableSource_NeverDeletes()
    {
      var store = StoreWith("src", Numbers(4, 5));
      Assert.False(EvalUnpackArg("$src into (a: $x, $y)", store).IsError);
      Assert.Equal(4, store.Get("x", false).AsNumber);
      Assert.Equal(5, store.Get("y", false).AsNumber);
      Assert.Equal(2, store.Get("src", false).AsArray.Count);
    }

    [Fact]
    public void Unpack_ComputedDatamapPatternKey()
    {
      var store = new HarloweVariableStore();
      var v = EvalUnpackArg("(dm: \"hp\", 9) into (dm: \"h\" + \"p\", $x)", store);
      Assert.False(v.IsError);
      Assert.Equal(9, store.Get("x", false).AsNumber);
    }

    // (move:) with pattern destinations ---------------------------------------

    [Fact]
    public void MovePattern_ArrayPositions_MovedAndSpliced()
    {
      // Reference doc: `(move: $array into (a: $x, $y))` moves the first and
      // second values out. Deletes run descending, so [1,2,3] keeps only 3.
      var store = StoreWith("array", Numbers(1, 2, 3));
      var v = EvalMoveArg("$array into (a: $x, $y)", store);
      Assert.False(v.IsError);
      Assert.Equal(1, store.Get("x", false).AsNumber);
      Assert.Equal(2, store.Get("y", false).AsNumber);
      var arr = store.Get("array", false).AsArray;
      Assert.Single(arr);
      Assert.Equal(3, arr[0].AsNumber);
    }

    [Fact]
    public void MovePattern_DatamapKeys_Removed()
    {
      var store = new HarloweVariableStore();
      store.Set("stats", false, HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["Maths"] = HarloweValue.OfNumber(12),
        ["Science"] = HarloweValue.OfNumber(8),
        ["Art"] = HarloweValue.OfNumber(6)
      }));
      var v = EvalMoveArg("$stats into (dm: \"Maths\", _m, \"Science\", _s)", store);
      Assert.False(v.IsError);
      Assert.Equal(12, store.Get("m", true).AsNumber);
      Assert.Equal(8, store.Get("s", true).AsNumber);
      var map = store.Get("stats", false).AsDatamap;
      Assert.Single(map);
      Assert.True(map.ContainsKey("Art"));
    }

    [Fact]
    public void MovePattern_ChainSource_DeletesNestedSlot()
    {
      var store = new HarloweVariableStore();
      var hero = new Dictionary<string, HarloweValue>
      {
        ["hp"] = HarloweValue.OfNumber(10),
        ["mp"] = HarloweValue.OfNumber(4)
      };
      store.Set("chars", false, HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["hero"] = HarloweValue.OfDatamap(hero)
      }));
      var v = EvalMoveArg("$chars's hero into (dm: \"hp\", $x)", store);
      Assert.False(v.IsError);
      Assert.Equal(10, store.Get("x", false).AsNumber);
      var heroNow = store.Get("chars", false).AsDatamap["hero"].AsDatamap;
      Assert.False(heroNow.ContainsKey("hp"));
      Assert.Equal(4, heroNow["mp"].AsNumber);
      Assert.Equal(10, hero["hp"].AsNumber);   // COW: the original map untouched
    }

    [Fact]
    public void MovePattern_LiteralSource_Errors()
    {
      var v = EvalMoveArg("(a: 1, 2) into (a: $x)");
      Assert.True(v.IsError);
      Assert.Contains("isn't stored in a variable", v.ErrorMessage);
    }

    [Fact]
    public void MovePattern_Mismatch_NothingAssignedOrDeleted()
    {
      var store = StoreWith("a", Numbers(1, 2));
      var v = EvalMoveArg("$a into (a: 9, $x)", store);
      Assert.True(v.IsError);
      Assert.Contains("didn't match", v.ErrorMessage);
      Assert.Equal(2, store.Get("a", false).AsArray.Count);
      Assert.Null(store.Get("x", false));
    }

    // The `random` data name --------------------------------------------------

    private static HarloweValue EvalWithRng(string source, IVariableStore store, IRng rng)
    {
      var tokens = new HarloweTokenizer().Tokenize("(set:" + source + ")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var args = new HarloweExpressionParser().ParseArgumentList(cursor, allowAssignment: true);
      return new ExpressionEvaluator(store, null, null, rng).Evaluate(args[0]);
    }

    [Fact]
    public void Random_Array_ReturnsMember()
    {
      var v = EvalP("(a: 10, 20, 30)'s random");
      Assert.False(v.IsError);
      Assert.Contains(v.AsNumber, new double[] { 10, 20, 30 });
    }

    [Fact]
    public void Random_Array_DeterministicPerSeed()
    {
      var store = StoreWith("arr", Numbers(1, 2, 3, 4, 5, 6, 7, 8));
      var first = EvalWithRng("$arr's random", store, new MulberryRng("seed"));
      var second = EvalWithRng("$arr's random", store, new MulberryRng("seed"));
      Assert.Equal(first.AsNumber, second.AsNumber);
    }

    [Fact]
    public void Random_EmptyArray_Errors()
    {
      var v = EvalP("$e's random", StoreWith("e", Numbers()));
      Assert.True(v.IsError);
      // "an empty array" is reference's objectName() rendering of a zero-length
      // array, interpolated into a sentence that then says "empty" twice.
      Assert.Equal("I can't get a random value from an empty array, because it's empty.", v.ErrorMessage);
    }

    [Fact]
    public void Random_String_ReturnsCharacter()
    {
      var v = Eval("\"cat\"'s random");
      Assert.False(v.IsError);
      Assert.Contains(v.AsString, new[] { "c", "a", "t" });
    }

    [Fact]
    public void Random_EmptyString_Errors()
    {
      var v = Eval("\"\"'s random");
      Assert.True(v.IsError);
      Assert.Equal("I can't get a random value from an empty string, because it's empty.", v.ErrorMessage);
    }

    [Fact]
    public void Random_Datamap_IsAPlainKey()
    {
      // On a datamap, `random` is just a data name — a key lookup, not a draw.
      var store = StoreWith("dm", OneKeyMap("random", 42));
      Assert.Equal(42, EvalP("$dm's random", store).AsNumber);
      var missing = EvalP("$m's random", StoreWith("m", OneKeyMap("hp", 1)));
      Assert.Contains("no key 'random'", missing.ErrorMessage);
    }

    [Fact]
    public void Random_PropertyAssign_WritesOnePosition()
    {
      // Reference compiles `random` to a concrete index before canSet sees
      // it, so assignment through it is legal and hits exactly one slot.
      var store = StoreWith("arr", Numbers(1, 1, 1));
      Assert.False(EvalP("$arr's random to 99", store).IsError);
      var arr = store.Get("arr", false).AsArray;
      Assert.Equal(3, arr.Count);
      int nines = 0;
      foreach (var item in arr) if (item.AsNumber == 99) nines++;
      Assert.Equal(1, nines);
    }

    [Fact]
    public void Random_Move_DeckDraw()
    {
      // The reference (move:) doc's headline idiom: `(move: $deck's random
      // into $card)` — the read and the delete share one drawn index, so the
      // moved card is exactly the removed element.
      var store = StoreWith("deck", Numbers(10, 20, 30, 40));
      var v = EvalMoveArg("$deck's random into $card", store);
      Assert.False(v.IsError);
      var deck = store.Get("deck", false).AsArray;
      var card = store.Get("card", false);
      Assert.Equal(3, deck.Count);
      // deck + card must be the original multiset — a mismatched read/delete
      // index would duplicate one element and lose another.
      var remaining = new List<double> { 10, 20, 30, 40 };
      Assert.True(remaining.Remove(card.AsNumber));
      foreach (var item in deck) Assert.True(remaining.Remove(item.AsNumber));
      Assert.Empty(remaining);
    }
  }
}
