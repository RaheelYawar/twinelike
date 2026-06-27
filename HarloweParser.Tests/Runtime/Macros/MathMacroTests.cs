using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the single-argument maths macros sharing <c>MathMacro</c>:
  /// <c>(floor:)</c>, <c>(ceil:)</c>, <c>(trunc:)</c>, <c>(abs:)</c>,
  /// <c>(sign:)</c> (reference <c>ts/macrolib/values.ts</c>, each <c>[fn, Number]</c>
  /// over a <c>Math.*</c> call). <c>(round:)</c> is tested separately — it isn't a
  /// direct <c>Math.*</c> wrapper.
  /// </summary>
  public class MathMacroTests
  {
    private static (MacroRegistry reg, MacroContext ctx) Setup()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = reg };
      reg.Context = ctx;
      return (reg, ctx);
    }

    private static HarloweValue Call(MacroRegistry reg, MacroContext ctx, string name, params HarloweValue[] args)
      => reg.Invoke(name, new List<HarloweValue>(args), ctx);

    private static HarloweValue Call1(MacroRegistry reg, MacroContext ctx, string name, double n)
      => Call(reg, ctx, name, HarloweValue.OfNumber(n));

    [Theory]
    [InlineData("floor", 1.99, 1.0)]
    [InlineData("floor", -1.1, -2.0)]
    [InlineData("floor", 3.0, 3.0)]
    [InlineData("ceil", 1.1, 2.0)]
    [InlineData("ceil", -1.9, -1.0)] // toward +infinity
    [InlineData("ceil", 3.0, 3.0)]
    [InlineData("trunc", 1.5, 1.0)]
    // Toward zero. Reference's own doc example says "(trunc: -3.9) produces 3" —
    // that's a typo in their docs; the real toward-zero result is -3.
    [InlineData("trunc", -3.9, -3.0)]
    [InlineData("trunc", 3.0, 3.0)]
    [InlineData("abs", -4.0, 4.0)]
    [InlineData("abs", 4.0, 4.0)]
    [InlineData("abs", -0.5, 0.5)]
    [InlineData("abs", 0.0, 0.0)]
    [InlineData("sign", -4.0, -1.0)]
    [InlineData("sign", 4.0, 1.0)]
    [InlineData("sign", 0.0, 0.0)]
    [InlineData("sign", -2.5, -1.0)]
    public void Computes(string name, double input, double expected)
    {
      var (reg, ctx) = Setup();
      var v = Call1(reg, ctx, name, input);
      Assert.Equal(HarloweValueKind.Number, v.Kind);
      Assert.Equal(expected, v.AsNumber);
    }

    [Fact]
    public void Sign_NaN_ReturnsNaN_DoesNotThrow()
    {
      // C# Math.Sign throws ArithmeticException on NaN; the macro must instead
      // return NaN (JS Math.sign(NaN) is NaN), honouring the no-throw policy.
      var (reg, ctx) = Setup();
      Assert.True(double.IsNaN(Call1(reg, ctx, "sign", double.NaN).AsNumber));
    }

    [Fact]
    public void Floor_NaN_PropagatesNaN()
    {
      var (reg, ctx) = Setup();
      Assert.True(double.IsNaN(Call1(reg, ctx, "floor", double.NaN).AsNumber));
    }

    [Fact]
    public void Abs_NegativeInfinity_ReturnsPositiveInfinity()
    {
      var (reg, ctx) = Setup();
      Assert.True(double.IsPositiveInfinity(Call1(reg, ctx, "abs", double.NegativeInfinity).AsNumber));
    }

    [Theory]
    [InlineData("floor")]
    [InlineData("ceil")]
    [InlineData("trunc")]
    [InlineData("abs")]
    [InlineData("sign")]
    public void NonNumberArg_ReturnsError(string name)
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, name, HarloweValue.OfString("x"));
      Assert.True(v.IsError);
      Assert.Contains("number", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void ErrorArg_PropagatesUnchanged()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "floor", HarloweValue.OfError("bad"));
      Assert.True(v.IsError);
      Assert.Equal("bad", v.ErrorMessage);
    }

    [Fact]
    public void NoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "abs");
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void TwoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "abs", HarloweValue.OfNumber(1), HarloweValue.OfNumber(2));
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void EndToEnd_FloorThroughPrint()
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: (floor: 3.7))"));
      var (reg, ctx) = Setup();
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, reg, ctx).Render(ast);
      Assert.Equal("3", buf.Text);
    }
  }
}
