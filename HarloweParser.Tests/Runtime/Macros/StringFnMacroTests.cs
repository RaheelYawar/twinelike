using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the single-argument String→String macros sharing
  /// <c>StringFnMacro</c>: <c>(uppercase:)</c>, <c>(lowercase:)</c>,
  /// <c>(str-reversed:)</c>/<c>(string-reversed:)</c>, <c>(upperfirst:)</c>,
  /// <c>(lowerfirst:)</c>. Case ops use invariant culture; reversal and
  /// case-first operate by Unicode code point. (upperfirst:)/(lowerfirst:) case
  /// only the first <b>alphanumeric</b> code point (a leading digit is a no-op),
  /// and deliberately do NOT title-case the whole word (diverging from reference).
  /// </summary>
  public class StringFnMacroTests
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

    private static string Str(MacroRegistry reg, MacroContext ctx, string name, string s)
      => Call(reg, ctx, name, HarloweValue.OfString(s)).AsString;

    [Theory]
    [InlineData("uppercase", "hi", "HI")]
    [InlineData("uppercase", "GrImAcE", "GRIMACE")]
    [InlineData("uppercase", "", "")]
    [InlineData("lowercase", "HI", "hi")]
    [InlineData("lowercase", "GrImAcE", "grimace")]
    [InlineData("str-reversed", "sknahT", "Thanks")]
    [InlineData("str-reversed", "abc", "cba")]
    [InlineData("str-reversed", "a", "a")]
    [InlineData("str-reversed", "", "")]
    [InlineData("string-reversed", "sknahT", "Thanks")]
    // upperfirst/lowerfirst: only the first alphanumeric code point changes.
    [InlineData("upperfirst", "  college B", "  College B")]
    [InlineData("upperfirst", "hELLO there", "HELLO there")]
    [InlineData("upperfirst", "McDonald", "McDonald")]
    [InlineData("upperfirst", "4ever", "4ever")] // leading digit is uncaseable
    [InlineData("lowerfirst", "COLLEGE", "cOLLEGE")]
    [InlineData("lowerfirst", "Hello", "hello")]
    public void Transforms(string name, string input, string expected)
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, name, HarloweValue.OfString(input));
      Assert.Equal(HarloweValueKind.String, v.Kind);
      Assert.Equal(expected, v.AsString);
    }

    [Fact]
    public void StrReversed_AstralCharacter_StaysIntact()
    {
      // "a😀b" reversed must be "b😀a" (the emoji is one code point / surrogate
      // pair) — a UTF-16-char reversal would corrupt the surrogate pair.
      var (reg, ctx) = Setup();
      Assert.Equal("b\U0001F600a", Str(reg, ctx, "str-reversed", "a\U0001F600b"));
    }

    [Theory]
    [InlineData("uppercase")]
    [InlineData("lowercase")]
    [InlineData("str-reversed")]
    [InlineData("upperfirst")]
    [InlineData("lowerfirst")]
    public void NonStringArg_ReturnsError(string name)
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, name, HarloweValue.OfNumber(5));
      Assert.True(v.IsError);
      Assert.Contains("string", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void ErrorArg_PropagatesUnchanged()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "uppercase", HarloweValue.OfError("bad"));
      Assert.True(v.IsError);
      Assert.Equal("bad", v.ErrorMessage);
    }

    [Fact]
    public void NoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "uppercase");
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void TwoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "uppercase", HarloweValue.OfString("a"), HarloweValue.OfString("b"));
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void EndToEnd_UppercaseThroughPrint()
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: (uppercase: \"hi\"))"));
      var (reg, ctx) = Setup();
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, reg, ctx).Render(ast);
      Assert.Equal("HI", buf.Text);
    }
  }
}
