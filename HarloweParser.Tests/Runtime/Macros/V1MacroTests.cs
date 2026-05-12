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
      if (rngSeed.HasValue) ctx.Rng = new Random(rngSeed.Value);
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

    // num --------------------------------------------------------------------

    [Fact]
    public void Num_ParsesString()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(3.5, Call(reg, ctx, "num", HarloweValue.OfString("3.5")).AsNumber);
    }

    [Fact]
    public void Num_NumberPassthrough()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(7, Call(reg, ctx, "num", HarloweValue.OfNumber(7)).AsNumber);
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

    // if / unless / else -----------------------------------------------------

    [Fact]
    public void If_TrueReturnsTrueAndSetsLastConditional()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "if", HarloweValue.OfBool(true));
      Assert.True(v.AsBool);
      Assert.True(ctx.LastConditional);
    }

    [Fact]
    public void If_FalseReturnsFalse()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "if", HarloweValue.OfBool(false));
      Assert.False(v.AsBool);
      Assert.False(ctx.LastConditional);
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
      Assert.True(Call(reg, ctx, "unless", HarloweValue.OfBool(false)).AsBool);
      Assert.False(Call(reg, ctx, "unless", HarloweValue.OfBool(true)).AsBool);
    }

    [Fact]
    public void Else_AfterFailingIf_Renders()
    {
      var (reg, ctx) = Setup();
      Call(reg, ctx, "if", HarloweValue.OfBool(false));
      var v = Call(reg, ctx, "else");
      Assert.True(v.AsBool);
    }

    [Fact]
    public void Else_AfterSucceedingIf_DoesNotRender()
    {
      var (reg, ctx) = Setup();
      Call(reg, ctx, "if", HarloweValue.OfBool(true));
      var v = Call(reg, ctx, "else");
      Assert.False(v.AsBool);
    }

    [Fact]
    public void Else_WithNoPrecedingConditional_DoesNotRender()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "else");
      Assert.False(v.AsBool);
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
  }
}
