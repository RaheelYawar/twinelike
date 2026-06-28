using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for <c>(substring: String, Number, Number)</c> — a code-point
  /// substring between two inclusive 1-based positions (reference
  /// <c>subset()</c> in <c>ts/utils/operationutils.ts</c>). Negatives count from
  /// the end, descending ranges swap, fractional positions truncate toward zero,
  /// and a 0/NaN position errors. An out-of-range start yields <c>""</c>, not a
  /// clamped character.
  /// </summary>
  public class SubstringMacroTests
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
      => reg.Invoke("substring", new List<HarloweValue>(args), ctx);

    private static HarloweValue Sub(MacroRegistry reg, MacroContext ctx, string s, double a, double b)
      => Call(reg, ctx, HarloweValue.OfString(s), HarloweValue.OfNumber(a), HarloweValue.OfNumber(b));

    [Theory]
    [InlineData("growl", 3, 5, "owl")]
    [InlineData("hewed", 4, 2, "ewe")]   // descending range swaps
    [InlineData("abc", 1, 3, "abc")]
    [InlineData("abc", 2, 2, "b")]
    [InlineData("abc", 1, -1, "abc")]    // -1 = last
    [InlineData("abcde", 2, -2, "bcd")]  // -2 = 2ndlast
    [InlineData("abc", 5, 6, "")]        // out-of-range start -> "" (not clamped char)
    [InlineData("abc", -9, 2, "ab")]     // big-negative start clamps to position 1
    [InlineData("abcde", 1.9, 3.9, "abc")] // fractional positions truncate toward zero
    public void Substring(string s, double a, double b, string expected)
    {
      var (reg, ctx) = Setup();
      var v = Sub(reg, ctx, s, a, b);
      Assert.Equal(HarloweValueKind.String, v.Kind);
      Assert.Equal(expected, v.AsString);
    }

    [Fact]
    public void Substring_AstralCharacters_AreOnePositionEach()
    {
      // "a😀b" is 3 code points; positions 1..2 are "a" and the emoji.
      var (reg, ctx) = Setup();
      Assert.Equal("a\U0001F600", Sub(reg, ctx, "a\U0001F600b", 1, 2).AsString);
    }

    [Fact]
    public void Substring_ZeroPosition_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Sub(reg, ctx, "abc", 0, 2);
      Assert.True(v.IsError);
      Assert.Contains("must not be", v.ErrorMessage);
    }

    [Fact]
    public void Substring_NaNPosition_ErrorsReportingNaN()
    {
      var (reg, ctx) = Setup();
      var v = Sub(reg, ctx, "abc", 1, double.NaN);
      Assert.True(v.IsError);
      Assert.Contains("NaN", v.ErrorMessage);
    }

    [Fact]
    public void Substring_NonStringFirstArg_Errors()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfNumber(1), HarloweValue.OfNumber(1), HarloweValue.OfNumber(2));
      Assert.True(v.IsError);
      Assert.Contains("String", v.ErrorMessage);
    }

    [Fact]
    public void Substring_ErrorArg_Propagates()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfError("bad"), HarloweValue.OfNumber(1), HarloweValue.OfNumber(2));
      Assert.True(v.IsError);
      Assert.Equal("bad", v.ErrorMessage);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Substring_WrongArity_Errors(int argCount)
    {
      var (reg, ctx) = Setup();
      var args = new HarloweValue[argCount];
      args[0] = HarloweValue.OfString("abc");
      for (int i = 1; i < argCount; i++) args[i] = HarloweValue.OfNumber(1);
      var v = Call(reg, ctx, args);
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void Substring_EndToEnd_ThroughPrint()
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: (substring: \"hello\", 1, 3))"));
      var (reg, ctx) = Setup();
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, reg, ctx).Render(ast);
      Assert.Equal("hel", buf.Text);
    }
  }
}
