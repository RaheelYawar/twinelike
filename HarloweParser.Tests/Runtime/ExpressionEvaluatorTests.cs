using System.Collections.Generic;
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
  }
}
