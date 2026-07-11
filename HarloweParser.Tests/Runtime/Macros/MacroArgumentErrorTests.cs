using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Sweep of the per-macro argument-validation branches: every macro that
  /// type-checks its arguments must return the documented in-prose error —
  /// never throw, never coerce. One test per branch, invoked directly through
  /// the registry so the assertion pins the exact diagnostic text.
  /// </summary>
  public class MacroArgumentErrorTests
  {
    private static string ErrorOf(string name, params HarloweValue[] args)
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var ctx = new MacroContext { Store = new HarloweVariableStore() };
      reg.Context = ctx;
      var v = reg.Invoke(name, new List<HarloweValue>(args), ctx);
      Assert.True(v.IsError, $"expected ({name}:) to produce an error");
      return v.ErrorMessage;
    }

    private static readonly HarloweValue Five = HarloweValue.OfNumber(5);
    private static readonly HarloweValue Word = HarloweValue.OfString("word");
    private static readonly HarloweValue Inf = HarloweValue.OfNumber(double.PositiveInfinity);

    [Fact]
    public void Unless_NonBool_Errors()
      => Assert.Contains("requires a Boolean", ErrorOf("unless", Five));

    [Fact]
    public void Display_NonString_Errors()
      => Assert.Contains("requires a String", ErrorOf("display", Five));

    [Fact]
    public void Modulo_NonNumber_Errors()
      => Assert.Contains("requires two Number", ErrorOf("modulo", Word, Five));

    [Fact]
    public void LoadGame_NonStringSlot_Errors()
      => Assert.Contains("string slot name", ErrorOf("load-game", Five));

    [Fact]
    public void Random_NonNumber_Errors()
      => Assert.Contains("requires Number arguments", ErrorOf("random", Word));

    [Fact]
    public void Random_NonFinite_Errors()
      => Assert.Contains("finite", ErrorOf("random", Inf));

    [Theory]
    [InlineData("all-pass")]
    [InlineData("some-pass")]
    [InlineData("none-pass")]
    [InlineData("altered")]
    public void LambdaMacros_NonLambdaFirstArg_Errors(string name)
      => Assert.Contains("must be a lambda", ErrorOf(name, Five, Five));

    [Fact]
    public void Substring_NonNumberPositions_Errors()
      => Assert.Contains("Number positions", ErrorOf("substring", Word, Word, Five));

    [Fact]
    public void StrNth_NonFinite_Errors()
      => Assert.Contains("finite", ErrorOf("str-nth", Inf));

    [Fact]
    public void StrRepeated_NonNumberCount_Errors()
      => Assert.Contains("Number count", ErrorOf("str-repeated", Word, Word));

    [Fact]
    public void Background_NonString_Errors()
      => Assert.Contains("requires a String", ErrorOf("background", Five));

    [Fact]
    public void Opacity_NonNumber_Errors()
      => Assert.Contains("requires a Number", ErrorOf("opacity", Word));

    [Fact]
    public void Link_NonChangerSecondArg_Errors()
      => Assert.Contains("requires a changer", ErrorOf("link", Word, Five));

    [Fact]
    public void TextColor_CarriageReturnInValue_ErrorNamesEscapedChar()
      => Assert.Contains("\\r", ErrorOf("text-color", HarloweValue.OfString("red\rblue")));

    [Fact]
    public void For_EachLambdaWithoutName_Errors()
    {
      // The parser always names `each` parameters, but the AST is public — a
      // hand-built each-lambda with no name must error, not bind nothing.
      var eachWithoutName = HarloweValue.OfLambda(
        new LambdaValue { Node = new Ast.Expression.LambdaNode { IsEach = true } });
      Assert.Contains("iteration variable", ErrorOf("for", eachWithoutName, Five));
    }
  }
}
