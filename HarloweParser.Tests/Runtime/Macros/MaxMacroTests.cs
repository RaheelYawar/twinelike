using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the <c>(max:)</c> maths macro — variadic, evaluates to the highest
  /// of the given numbers (reference <c>ts/macrolib/values.ts</c>:
  /// <c>max: [max, rest(Number)]</c>). Shares its implementation with
  /// <c>(min:)</c> (one parameterized class), so the unpack / NaN / validation
  /// behaviour mirrors <see cref="MinMacroTests"/>; these confirm the parameterised
  /// class behaves identically for the max direction.
  /// </summary>
  public class MaxMacroTests
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
      => reg.Invoke("max", new List<HarloweValue>(args), ctx);

    private static HarloweValue Max(MacroRegistry reg, MacroContext ctx, params double[] nums)
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
    public void Max_ReferenceExample()
    {
      var (reg, ctx) = Setup();
      var v = Max(reg, ctx, 2, -5, 2, 7, 0.1);
      Assert.Equal(HarloweValueKind.Number, v.Kind);
      Assert.Equal(7.0, v.AsNumber);
    }

    [Fact]
    public void Max_TwoArgs()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(3.0, Max(reg, ctx, 3, 1).AsNumber);
    }

    [Fact]
    public void Max_SingleArg_ReturnsIt()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(5.0, Max(reg, ctx, 5).AsNumber);
    }

    [Fact]
    public void Max_AllNegative()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(-1.0, Max(reg, ctx, -3, -1, -2).AsNumber);
    }

    [Fact]
    public void Max_ClampIdiom_FloorsValue()
    {
      // (max: $x, 0) floors $x at 0 — the canonical clamping use.
      var (reg, ctx) = Setup();
      Assert.Equal(0.0, Max(reg, ctx, -5, 0).AsNumber);
    }

    [Fact]
    public void Max_SingleArray_AutoUnpacks()
    {
      var (reg, ctx) = Setup();
      Assert.Equal(3.0, Call(reg, ctx, Array(3, 1, 2)).AsNumber);
    }

    [Fact]
    public void Max_AnyNaN_ReturnsNaN_OrderIndependent()
    {
      var (reg, ctx) = Setup();
      Assert.True(double.IsNaN(Max(reg, ctx, double.NaN, 1).AsNumber));
      Assert.True(double.IsNaN(Max(reg, ctx, 1, double.NaN).AsNumber));
    }

    [Fact]
    public void Max_NonNumberArg_ReturnsError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfNumber(1), HarloweValue.OfString("x"));
      Assert.True(v.IsError);
      Assert.Contains("number", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void Max_ErrorArg_PropagatesUnchanged()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfError("bad"));
      Assert.True(v.IsError);
      Assert.Equal("bad", v.ErrorMessage);
    }

    [Fact]
    public void Max_NoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx);
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void Max_EmptyArray_ReturnsError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfArray(new List<HarloweValue>()));
      Assert.True(v.IsError);
    }

    [Fact]
    public void Max_EndToEnd_ThroughPrint()
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: (max: 5, 3, 8))"));
      var (reg, ctx) = Setup();
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, reg, ctx).Render(ast);
      Assert.Equal("8", buf.Text);
    }
  }
}
