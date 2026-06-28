using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for <c>(words: String) → Array</c> — splits on whitespace runs,
  /// dropping empties (reference <c>ts/macrolib/values.ts</c>:
  /// <c>split(realWhitespace+).filter(Boolean)</c>).
  /// </summary>
  public class WordsMacroTests
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
      => reg.Invoke("words", new List<HarloweValue>(args), ctx);

    private static List<HarloweValue> Words(MacroRegistry reg, MacroContext ctx, string s)
      => Call(reg, ctx, HarloweValue.OfString(s)).AsArray;

    [Fact]
    public void Words_ReferenceExample()
    {
      var (reg, ctx) = Setup();
      var w = Words(reg, ctx, "god-king Torment's peril");
      Assert.Equal(3, w.Count);
      Assert.Equal("god-king", w[0].AsString);
      Assert.Equal("Torment's", w[1].AsString);
      Assert.Equal("peril", w[2].AsString);
    }

    [Fact]
    public void Words_CollapsesWhitespaceRuns()
    {
      var (reg, ctx) = Setup();
      var w = Words(reg, ctx, "  a \t b\n c  ");
      Assert.Equal(3, w.Count);
      Assert.Equal("a", w[0].AsString);
      Assert.Equal("c", w[2].AsString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\n ")]
    public void Words_EmptyOrWhitespaceOnly_EmptyArray(string s)
    {
      var (reg, ctx) = Setup();
      Assert.Empty(Words(reg, ctx, s));
    }

    [Fact]
    public void Words_SingleWord_OneElement()
    {
      var (reg, ctx) = Setup();
      var w = Words(reg, ctx, "solo");
      Assert.Single(w);
      Assert.Equal("solo", w[0].AsString);
    }

    [Fact]
    public void Words_NonStringArg_ReturnsError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, HarloweValue.OfNumber(5));
      Assert.True(v.IsError);
      Assert.Contains("string", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void Words_NoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx);
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }

    [Fact]
    public void Words_EndToEnd_IndexableArray()
    {
      // (words: "a b c")'s 2nd is "b" — confirms it returns a usable Array.
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: (words: \"a b c\")'s 2nd)"));
      var (reg, ctx) = Setup();
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, reg, ctx).Render(ast);
      Assert.Equal("b", buf.Text);
    }
  }
}
