using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the <c>(round:)</c> maths macro — rounds a single number to the
  /// nearest whole number, halves going <b>up</b> (toward +infinity), matching
  /// reference Harlowe's <c>Math.round</c> (<c>ts/macrolib/values.ts</c>:
  /// <c>round: [round, Number]</c> with <c>round</c> destructured from
  /// <c>Math</c>).
  ///
  /// <para>The negative-half cases guard against C#'s default banker's rounding
  /// (<c>2.5</c> → <c>2</c>) and <c>MidpointRounding.AwayFromZero</c> (<c>-1.5</c>
  /// → <c>-2</c>) — both wrong. The <c>0.49999999999999994</c> case guards the
  /// opposite trap: <c>Math.Floor(x + 0.5)</c> would yield <c>1</c> there (the
  /// <c>+ 0.5</c> rounds up to <c>1.0</c>), so the implementation compares the
  /// fractional part instead. <c>MidpointRounding.ToPositiveInfinity</c> would
  /// match JS but isn't available on <c>netstandard2.0</c>.</para>
  /// </summary>
  public class RoundMacroTests
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
      => reg.Invoke("round", new List<HarloweValue>(args), ctx);

    [Theory]
    // Positive halves round up; ordinary nearest-rounding otherwise.
    [InlineData(1.5, 2.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(2.5, 3.0)]
    [InlineData(2.4, 2.0)]
    [InlineData(2.6, 3.0)]
    // FP corner: the largest double below 0.5. JS Math.round gives 0 (it's below
    // 0.5), but Math.Floor(x + 0.5) gives 1 because x + 0.5 rounds up to exactly
    // 1.0 in IEEE-754. Guards against the +0.5 form. (ECMAScript's Math.round
    // note calls out this exact case.)
    [InlineData(0.49999999999999994, 0.0)]
    // Negative halves go toward +infinity, NOT away from zero — the fidelity
    // guard. AwayFromZero would give -2 / -3 here; banker's would give -2 / -2.
    [InlineData(-0.5, 0.0)]
    [InlineData(-1.5, -1.0)]
    [InlineData(-2.5, -2.0)]
    [InlineData(-2.6, -3.0)]
    [InlineData(-3.9, -4.0)]
    // Whole numbers pass through unchanged.
    [InlineData(5.0, 5.0)]
    [InlineData(-7.0, -7.0)]
    [InlineData(0.0, 0.0)]
    public void Round_RoundsHalfTowardPositiveInfinity(double input, double expected)
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfNumber(input));
      Assert.Equal(HarloweValueKind.Number, v.Kind);
      Assert.Equal(expected, v.AsNumber);
    }

    [Fact]
    public void Round_NonNumberArg_ReturnsDatatypeError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfString("x"));
      Assert.True(v.IsError);
      Assert.Contains("number", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void Round_ErrorArg_PropagatesUnchanged()
    {
      // The registry's central error gate returns the error arg as-is (no masking).
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfError("bad"));
      Assert.True(v.IsError);
      Assert.Equal("bad", v.ErrorMessage);
    }

    [Fact]
    public void Round_NoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx);
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void Round_TwoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfNumber(1.5), HarloweValue.OfNumber(2.5));
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void Round_EndToEnd_ThroughPrint()
    {
      // The author-facing path: (print: (round: 2.5)) renders "3".
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: (round: 2.5))"));
      var (reg, ctx) = Setup();
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, reg, ctx).Render(ast);
      Assert.Equal("3", buf.Text);
    }
  }
}
