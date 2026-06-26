using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the <c>(min:)</c> maths macro — variadic, evaluates to the lowest
  /// of the given numbers (reference <c>ts/macrolib/values.ts</c>:
  /// <c>min: [min, rest(Number)]</c>, so one-or-more Numbers). A single trailing
  /// Array is auto-unpacked (the project's deferred-spread convention, shared
  /// with <c>(sorted:)</c>), so <c>(min: (a:3,1,2))</c> is the min of 3, 1, 2.
  /// </summary>
  public class MinMacroTests
  {
    private static (MacroRegistry reg, MacroContext ctx) Setup()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = reg };
      reg.Context = ctx;
      return (reg, ctx);
    }

    private static HarloweValue Call(MacroRegistry reg, MacroContext ctx, params HarloweValue[] args)
      => reg.Invoke("min", new List<HarloweValue>(args), ctx);

    private static HarloweValue Min(MacroRegistry reg, MacroContext ctx, params double[] nums)
    {
      var args = new HarloweValue[nums.Length];
      for (int i = 0; i < nums.Length; i++) args[i] = HarloweValue.OfNumber(nums[i]);
      return Call(reg, ctx, args);
    }

    private static HarloweValue Array(params double[] nums)
    {
      var items = new List<HarloweValue>(nums.Length);
      foreach (var n in nums) items.Add(HarloweValue.OfNumber(n));
      return HarloweValue.OfArray(items);
    }

    [Fact]
    public void Min_ReferenceExample()
    {
      var (reg, ctx) = Setup();
      var v = Min(reg, ctx, 2, -5, 2, 7, 0.1);
      Assert.Equal(HarloweValueKind.Number, v.Kind);
      Assert.Equal(-5.0, v.AsNumber);
    }

    [Fact]
    public void Min_TwoArgs()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(1.0, Min(reg, ctx, 3, 1).AsNumber);
    }

    [Fact]
    public void Min_SingleArg_ReturnsIt()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(5.0, Min(reg, ctx, 5).AsNumber);
    }

    [Fact]
    public void Min_AllNegative()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(-3.0, Min(reg, ctx, -3, -1, -2).AsNumber);
    }

    [Fact]
    public void Min_ClampIdiom_CapsValue()
    {
      // (min: $x, 100) caps $x at 100 — the canonical clamping use.
      var (reg, ctx) = Setup();
      Assert.Equal(100.0, Min(reg, ctx, 150, 100).AsNumber);
    }

    [Fact]
    public void Min_SingleArray_AutoUnpacks()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(1.0, Call(reg, ctx, Array(3, 1, 2)).AsNumber);
    }

    [Fact]
    public void Min_NonNumberArg_ReturnsError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfNumber(1), HarloweValue.OfString("x"));
      Assert.True(v.IsError);
      Assert.Contains("number", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void Min_NonNumberInsideArray_ReturnsError()
    {
      var (reg, ctx) = Setup();
      var arr = HarloweValue.OfArray(new List<HarloweValue>
        { HarloweValue.OfNumber(1), HarloweValue.OfString("x") });
      var v = Call(reg, ctx, arr);
      Assert.True(v.IsError);
      Assert.Contains("number", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void Min_AnyNaN_ReturnsNaN_OrderIndependent()
    {
      // JS Math.min returns NaN if any argument is NaN, regardless of position.
      // The naive comparison fold (NaN < best is false) would otherwise skip a
      // non-first NaN and leak an order-dependent result: (min: 1, NaN) -> 1.
      var (reg, ctx) = Setup();
      Assert.True(double.IsNaN(Min(reg, ctx, double.NaN, 1).AsNumber));
      Assert.True(double.IsNaN(Min(reg, ctx, 1, double.NaN).AsNumber));
    }

    [Fact]
    public void Min_ErrorArg_PropagatesUnchanged()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfError("bad"));
      Assert.True(v.IsError);
      Assert.Equal("bad", v.ErrorMessage);
    }

    [Fact]
    public void Min_NoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx);
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void Min_EmptyArray_ReturnsError()
    {
      // (min: (a:)) expands to zero numbers — no minimum exists.
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfArray(new List<HarloweValue>()));
      Assert.True(v.IsError);
    }

    [Fact]
    public void Min_EndToEnd_ThroughPrint()
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: (min: 5, 3, 8))"));
      var (reg, ctx) = Setup();
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, reg, ctx).Render(ast);
      Assert.Equal("3", buf.Text);
    }
  }
}
