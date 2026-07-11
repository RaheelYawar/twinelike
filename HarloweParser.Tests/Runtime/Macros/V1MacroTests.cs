using System;
using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  public class V1MacroTests
  {
    private static (MacroRegistry reg, MacroContext ctx) Setup(int? rngSeed = null)
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store };
      if (rngSeed.HasValue) ctx.Rng = new MulberryRng(rngSeed.Value);
      reg.Context = ctx;
      return (reg, ctx);
    }

    private static HarloweValue Call(MacroRegistry reg, MacroContext ctx, string name, params HarloweValue[] args)
      => reg.Invoke(name, new List<HarloweValue>(args), ctx);

    // set / put --------------------------------------------------------------

    [Fact]
    public void Set_AssignmentAlreadyDoneByEvaluator_ReturnsNullCoercedToEmpty()
    {
      var (reg, ctx) = Setup();
      // Simulate: evaluator already ran `$hp to 5`, store has hp=5, args carry the value.
      ctx.Store.Set("hp", false, HarloweValue.OfNumber(5));
      var v = Call(reg, ctx, "set", HarloweValue.OfNumber(5));
      Assert.Equal(string.Empty, v.AsString);
      Assert.Equal(5, ctx.Store.Get("hp", false).AsNumber);
    }

    [Fact]
    public void Set_PropagatesErrorArg()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "set", HarloweValue.OfError("bad"));
      Assert.True(v.IsError);
    }

    [Fact]
    public void Put_PropagatesErrorArg()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "put", HarloweValue.OfError("bad"));
      Assert.True(v.IsError);
    }

    // print / text -----------------------------------------------------------

    [Fact]
    public void Print_StringifiesNumber()
    {
      var (reg, ctx) = Setup();
      Assert.Equal("5", Call(reg, ctx, "print", HarloweValue.OfNumber(5)).AsString);
    }

    [Fact]
    public void Print_PassthroughString()
    {
      var (reg, ctx) = Setup();
      Assert.Equal("hi", Call(reg, ctx, "print", HarloweValue.OfString("hi")).AsString);
    }

    [Fact]
    public void Text_BehavesLikePrint()
    {
      var (reg, ctx) = Setup();
      Assert.Equal("true", Call(reg, ctx, "text", HarloweValue.OfBool(true)).AsString);
    }

    [Fact]
    public void Text_Variadic_JoinsArgsWithNoSeparator()
    {
      var (reg, ctx) = Setup();
      Assert.Equal("You have 10 HP", Call(reg, ctx, "text",
        HarloweValue.OfString("You have "), HarloweValue.OfNumber(10),
        HarloweValue.OfString(" HP")).AsString);
    }

    [Fact]
    public void Text_ZeroArgs_EmptyString()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(string.Empty, Call(reg, ctx, "text").AsString);
    }

    [Fact]
    public void Text_ArrayArg_CommaJoinsElements()
    {
      // Reference: (str: (a: 2, "Hot", 4, "U")) is "2,Hot,4,U".
      var (reg, ctx) = Setup();
      var arr = HarloweValue.OfArray(new List<HarloweValue>
      {
        HarloweValue.OfNumber(2), HarloweValue.OfString("Hot"),
        HarloweValue.OfNumber(4), HarloweValue.OfString("U"),
      });
      Assert.Equal("2,Hot,4,U", Call(reg, ctx, "text", arr).AsString);
    }

    [Fact]
    public void Text_RejectsDatamapArgument()
    {
      // Only String/Number/Boolean/Array are accepted; a Datamap errors.
      var (reg, ctx) = Setup();
      var dm = HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        { "hp", HarloweValue.OfNumber(10) },
      });
      Assert.True(Call(reg, ctx, "text", dm).IsError);
    }

    [Fact]
    public void String_AliasRegistered()
    {
      var (reg, ctx) = Setup();
      Assert.Equal("x", Call(reg, ctx, "string", HarloweValue.OfString("x")).AsString);
    }

    [Fact]
    public void Str_AliasIsVariadicToo()
    {
      var (reg, ctx) = Setup();
      Assert.Equal("ab", Call(reg, ctx, "str",
        HarloweValue.OfString("a"), HarloweValue.OfString("b")).AsString);
    }

    // num --------------------------------------------------------------------

    [Fact]
    public void Num_ParsesString()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(3.5, Call(reg, ctx, "num", HarloweValue.OfString("3.5")).AsNumber);
    }

    [Fact]
    public void Num_NumberInput_Errors()
    {
      // Reference rejects a Number argument ([String] signature); it is not a
      // passthrough. Authors convert the other direction with (str:).
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "num", HarloweValue.OfNumber(7)).IsError);
    }

    [Fact]
    public void Num_UnparseableString_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "num", HarloweValue.OfString("abc")).IsError);
    }

    [Fact]
    public void Num_BoolArg_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "num", HarloweValue.OfBool(true)).IsError);
    }

    [Fact]
    public void Num_EmptyString_IsZero()
    {
      // JS Number(""): empty string coerces to 0.
      var (reg, ctx) = Setup();
      Assert.Equal(0, Call(reg, ctx, "num", HarloweValue.OfString("")).AsNumber);
    }

    [Fact]
    public void Num_WhitespaceOnlyString_IsZero()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(0, Call(reg, ctx, "num", HarloweValue.OfString("   ")).AsNumber);
    }

    [Fact]
    public void Num_LeadingTrailingWhitespace_Parsed()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(42, Call(reg, ctx, "num", HarloweValue.OfString("  42  ")).AsNumber);
    }

    [Fact]
    public void Num_ScientificNotation_Parsed()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(1000, Call(reg, ctx, "num", HarloweValue.OfString("1e3")).AsNumber);
    }

    [Fact]
    public void Num_NegativeAndDecimal_Parsed()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(-3.5, Call(reg, ctx, "num", HarloweValue.OfString("-3.5")).AsNumber);
    }

    [Fact]
    public void Number_AliasRegistered()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(3, Call(reg, ctx, "number", HarloweValue.OfString("3")).AsNumber);
    }

    // if / unless / else -----------------------------------------------------

    /// <summary>Apply a conditional changer to a probe hook: returns the shown/hidden outcome and asserts the hook rendered iff shown.</summary>
    private static bool Applies(HarloweValue changer)
    {
      bool rendered = false;
      bool shown = changer.AsChanger.Apply(new BufferedRenderOutput(), _ => rendered = true);
      Assert.Equal(shown, rendered);
      return shown;
    }

    [Fact]
    public void If_ReturnsConditionalChanger_TrueShows_FalseHides()
    {
      var (reg, ctx) = Setup();
      var t = Call(reg, ctx, "if", HarloweValue.OfBool(true));
      Assert.Equal(HarloweValueKind.Changer, t.Kind);
      Assert.True(Applies(t));
      Assert.False(Applies(Call(reg, ctx, "if", HarloweValue.OfBool(false))));
    }

    [Fact]
    public void If_Invoke_DoesNotTouchPairing()
    {
      // Creating the changer must not set LastConditional — only applying it to
      // a hook does (reference: lastHookShown is written in section.ts's hook
      // application, not by the macro). A nested (set: $x to (if: $b)) between
      // an (if:)[...] and its (else:)[...] therefore can't corrupt the pairing.
      var (reg, ctx) = Setup();
      Call(reg, ctx, "if", HarloweValue.OfBool(true));
      Assert.Null(ctx.LastConditional);
    }

    [Fact]
    public void If_NonBool_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "if", HarloweValue.OfNumber(1)).IsError);
    }

    [Fact]
    public void Unless_NegatesCondition()
    {
      var (reg, ctx) = Setup();
      Assert.True(Applies(Call(reg, ctx, "unless", HarloweValue.OfBool(false))));
      Assert.False(Applies(Call(reg, ctx, "unless", HarloweValue.OfBool(true))));
    }

    [Fact]
    public void Else_AfterHiddenHook_ShowsChanger()
    {
      // (else:) reads the pairing at call time and bakes the decision into the
      // returned changer (reference: new Changer(`else`, [lastHookShown === false])).
      var (reg, ctx) = Setup();
      ctx.LastConditional = false;   // the preceding conditional hook was hidden
      Assert.True(Applies(Call(reg, ctx, "else")));
    }

    [Fact]
    public void Else_AfterShownHook_HidesChanger()
    {
      var (reg, ctx) = Setup();
      ctx.LastConditional = true;    // the preceding conditional hook was shown
      Assert.False(Applies(Call(reg, ctx, "else")));
    }

    [Fact]
    public void Else_WithNoPrecedingConditional_Errors()
    {
      // A stray (else:) with nothing before it is a structural mistake and
      // surfaces an in-prose error (matching reference Harlowe), rather than
      // silently no-opping.
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "else").IsError);
    }

    // goto -------------------------------------------------------------------

    [Fact]
    public void Goto_StoresPendingPassageName()
    {
      var (reg, ctx) = Setup();
      Call(reg, ctx, "goto", HarloweValue.OfString("Next"));
      Assert.Equal("Next", ctx.PendingGoto);
    }

    [Fact]
    public void Goto_NonString_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "goto", HarloweValue.OfNumber(1)).IsError);
    }

    // display ----------------------------------------------------------------

    [Fact]
    public void Display_NoRendererWired_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "display", HarloweValue.OfString("X")).IsError);
    }

    [Fact]
    public void Display_DelegatesToRenderPassageCallback()
    {
      // No BodyRenderer is active here, so ctx.Output is null and DisplayMacro
      // routes through the buffered-snapshot path — the callback writes into
      // a fresh buffer and DisplayMacro returns that buffer's text.
      var (reg, ctx) = Setup();
      ctx.RenderPassage = (name, output) =>
      {
        output.Text($"[{name}]");
        return HarloweValue.OfString(string.Empty);
      };
      var v = Call(reg, ctx, "display", HarloweValue.OfString("Some"));
      Assert.Equal("[Some]", v.AsString);
    }

    // random / either --------------------------------------------------------

    [Fact]
    public void Random_OneArg_RangeZeroToN()
    {
      var (reg, ctx) = Setup(rngSeed: 42);
      // With seed, deterministic. Just check range bounds.
      for (int i = 0; i < 50; i++)
      {
        double n = Call(reg, ctx, "random", HarloweValue.OfNumber(5)).AsNumber;
        Assert.InRange(n, 0, 5);
      }
    }

    [Fact]
    public void Random_TwoArgs_RangeAToB()
    {
      var (reg, ctx) = Setup(rngSeed: 1);
      for (int i = 0; i < 50; i++)
      {
        double n = Call(reg, ctx, "random", HarloweValue.OfNumber(10), HarloweValue.OfNumber(20)).AsNumber;
        Assert.InRange(n, 10, 20);
      }
    }

    [Fact]
    public void Random_ReversedRange_Normalised()
    {
      var (reg, ctx) = Setup(rngSeed: 1);
      double n = Call(reg, ctx, "random", HarloweValue.OfNumber(5), HarloweValue.OfNumber(2)).AsNumber;
      Assert.InRange(n, 2, 5);
    }

    [Fact]
    public void Random_DeterministicWithSeed()
    {
      var (regA, ctxA) = Setup(rngSeed: 123);
      var (regB, ctxB) = Setup(rngSeed: 123);
      double a = Call(regA, ctxA, "random", HarloweValue.OfNumber(1000)).AsNumber;
      double b = Call(regB, ctxB, "random", HarloweValue.OfNumber(1000)).AsNumber;
      Assert.Equal(a, b);
    }

    [Fact]
    public void Random_FractionalBounds_TruncatedNotRejected()
    {
      // Reference coerces bounds via parseInt; (random: 1.5, 1.9) is (random: 1, 1).
      var (reg, ctx) = Setup();
      Assert.Equal(1, Call(reg, ctx, "random",
        HarloweValue.OfNumber(1.5), HarloweValue.OfNumber(1.9)).AsNumber);
    }

    [Fact]
    public void Random_FractionalOneArg_Truncated()
    {
      var (reg, ctx) = Setup();
      // (random: 0.9) → upper bound truncates to 0 → range [0, 0] → 0.
      Assert.Equal(0, Call(reg, ctx, "random", HarloweValue.OfNumber(0.9)).AsNumber);
    }

    [Fact]
    public void Random_NegativeFractional_TruncatesTowardZero()
    {
      // Toward zero, not floor: -1.9 and -1.1 both truncate to -1, so the range
      // is [-1, -1] → -1 (floor would wrongly give -2).
      var (reg, ctx) = Setup();
      Assert.Equal(-1, Call(reg, ctx, "random",
        HarloweValue.OfNumber(-1.9), HarloweValue.OfNumber(-1.1)).AsNumber);
    }

    [Fact]
    public void Random_SpanWiderThanInt32_NoOverflow_StaysInRange()
    {
      // Regression: range = hi - lo + 1 here is 4e9 > 2^31, so the [0,1)-scaled
      // draw exceeds 2^31 ~46% of the time. A narrowing (int) cast would be
      // unspecified; the double/long scale must keep results in [lo, hi].
      var (reg, ctx) = Setup(rngSeed: 5);
      for (int i = 0; i < 200; i++)
      {
        double n = Call(reg, ctx, "random",
          HarloweValue.OfNumber(-2000000000), HarloweValue.OfNumber(2000000000)).AsNumber;
        Assert.InRange(n, -2000000000, 2000000000);
      }
    }

    [Fact]
    public void Either_ReturnsOneOfTheArgs()
    {
      var (reg, ctx) = Setup(rngSeed: 7);
      var picks = new HashSet<string>();
      for (int i = 0; i < 50; i++)
      {
        var v = Call(reg, ctx, "either",
          HarloweValue.OfString("a"),
          HarloweValue.OfString("b"),
          HarloweValue.OfString("c"));
        picks.Add(v.AsString);
      }
      foreach (var p in picks) Assert.Contains(p, new[] { "a", "b", "c" });
    }

    // a / dm -----------------------------------------------------------------

    [Fact]
    public void A_BuildsArray()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "a", HarloweValue.OfNumber(1), HarloweValue.OfNumber(2));
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Equal(2, v.AsArray.Count);
    }

    [Fact]
    public void A_EmptyAllowed()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "a");
      Assert.Empty(v.AsArray);
    }

    [Fact]
    public void Dm_BuildsDatamap()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "dm",
        HarloweValue.OfString("name"), HarloweValue.OfString("Sam"),
        HarloweValue.OfString("hp"), HarloweValue.OfNumber(10));
      Assert.Equal(HarloweValueKind.Datamap, v.Kind);
      Assert.Equal("Sam", v.AsDatamap["name"].AsString);
      Assert.Equal(10, v.AsDatamap["hp"].AsNumber);
    }

    [Fact]
    public void Dm_OddArgCount_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "dm",
        HarloweValue.OfString("name"), HarloweValue.OfString("Sam"),
        HarloweValue.OfString("hp")).IsError);
    }

    [Fact]
    public void Dm_NonStringKey_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "dm",
        HarloweValue.OfNumber(1), HarloweValue.OfString("v")).IsError);
    }

    [Fact]
    public void Dm_DuplicateKey_Errors()
    {
      // Using the same data name twice is an authoring mistake, not a silent
      // last-write-wins (matches reference Harlowe's map.has(key) check).
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "dm",
        HarloweValue.OfString("hp"), HarloweValue.OfNumber(10),
        HarloweValue.OfString("hp"), HarloweValue.OfNumber(5)).IsError);
    }

    [Fact]
    public void Dm_DuplicateKey_ErrorNamesTheKey()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "dm",
        HarloweValue.OfString("hp"), HarloweValue.OfNumber(10),
        HarloweValue.OfString("hp"), HarloweValue.OfNumber(5));
      Assert.True(v.IsError);
      Assert.Contains("hp", v.ErrorMessage);
    }

    [Fact]
    public void Dm_KeysAreCaseSensitive_NoFalseDuplicate()
    {
      // "HP" and "hp" are distinct data names — no duplicate-key error.
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "dm",
        HarloweValue.OfString("HP"), HarloweValue.OfNumber(1),
        HarloweValue.OfString("hp"), HarloweValue.OfNumber(2));
      Assert.Equal(HarloweValueKind.Datamap, v.Kind);
      Assert.Equal(2, v.AsDatamap.Count);
    }

    // modulo -----------------------------------------------------------------

    [Fact]
    public void Modulo_BasicMath()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(1, Call(reg, ctx, "modulo", HarloweValue.OfNumber(7), HarloweValue.OfNumber(3)).AsNumber);
    }

    [Fact]
    public void Modulo_DivisorZero_Errors()
    {
      var (reg, ctx) = Setup();
      Assert.True(Call(reg, ctx, "modulo", HarloweValue.OfNumber(7), HarloweValue.OfNumber(0)).IsError);
    }

    // arity edge cases via registry -----------------------------------------

    [Fact]
    public void Print_ZeroArgs_Errors()
    {
      var (reg, ctx) = Setup();
      var v = reg.Invoke("print", new List<HarloweValue>(), ctx);
      Assert.True(v.IsError);
    }

    [Fact]
    public void Random_ThreeArgs_Errors()
    {
      var (reg, ctx) = Setup();
      var v = reg.Invoke("random", new List<HarloweValue>
      {
        HarloweValue.OfNumber(1), HarloweValue.OfNumber(2), HarloweValue.OfNumber(3)
      }, ctx);
      Assert.True(v.IsError);
    }

    // history ---------------------------------------------------------------

    private class StubHistoryContext : IEvaluationContext
    {
      public HarloweValue Time => HarloweValue.OfNumber(0);
      public HarloweValue Visits => HarloweValue.OfNumber(0);
      public HarloweValue Passage => HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>());
      public HarloweValue History { get; set; }
      public HarloweValue Turns => HarloweValue.OfNumber(0);
    }

    [Fact]
    public void History_NoEvaluationContext_Errors()
    {
      var (reg, ctx) = Setup();
      // Setup() does not wire EvaluationContext; macro should error.
      var v = Call(reg, ctx, "history");
      Assert.True(v.IsError);
    }

    [Fact]
    public void History_PullsArrayFromContext()
    {
      var (reg, ctx) = Setup();
      var stub = new StubHistoryContext
      {
        History = HarloweValue.OfArray(new List<HarloweValue>
        {
          HarloweValue.OfString("Start"),
          HarloweValue.OfString("Middle")
        })
      };
      ctx.EvaluationContext = stub;
      var v = Call(reg, ctx, "history");
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Equal(2, v.AsArray.Count);
      Assert.Equal("Start", v.AsArray[0].AsString);
      Assert.Equal("Middle", v.AsArray[1].AsString);
    }

    [Fact]
    public void History_OneArg_ArityError()
    {
      var (reg, ctx) = Setup();
      var v = reg.Invoke("history", new List<HarloweValue> { HarloweValue.OfNumber(1) }, ctx);
      Assert.True(v.IsError);
    }

    [Fact]
    public void Num_InfinityStrings_CoerceToInfinities()
    {
      // JS Number() coerces the literal Infinity spellings; whitespace trims.
      var (reg, ctx) = Setup();
      Assert.True(double.IsPositiveInfinity(Call(reg, ctx, "num", HarloweValue.OfString("Infinity")).AsNumber));
      Assert.True(double.IsPositiveInfinity(Call(reg, ctx, "num", HarloweValue.OfString("+Infinity")).AsNumber));
      Assert.True(double.IsNegativeInfinity(Call(reg, ctx, "num", HarloweValue.OfString("-Infinity")).AsNumber));
      Assert.True(double.IsPositiveInfinity(Call(reg, ctx, "number", HarloweValue.OfString(" Infinity ")).AsNumber));
    }
  }
}
