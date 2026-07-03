using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for <c>(str-repeated: Number, String)</c> / <c>(string-repeated:)</c>
  /// — repeats a string <c>count</c> times (reference <c>ts/macrolib/values.ts</c>).
  /// Count must be a non-negative whole number; the empty-string check fires
  /// first (so <c>(str-repeated: 0, "")</c> errors, it does not return <c>""</c>).
  /// </summary>
  public class StrRepeatedMacroTests
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

    private static HarloweValue Rep(MacroRegistry reg, MacroContext ctx, double count, string s)
      => Call(reg, ctx, "str-repeated", HarloweValue.OfNumber(count), HarloweValue.OfString(s));

    [Fact]
    public void StrRepeated_ReferenceExample()
      => RunSetup((reg, ctx) => Assert.Equal("Fool! Fool! Fool! Fool! Fool! ", Rep(reg, ctx, 5, "Fool! ").AsString));

    [Fact]
    public void StrRepeated_ZeroCount_NonEmptyString_ReturnsEmpty()
      => RunSetup((reg, ctx) => Assert.Equal("", Rep(reg, ctx, 0, "x").AsString));

    [Fact]
    public void StrRepeated_ZeroCount_EmptyString_Errors()
      => RunSetup((reg, ctx) =>
      {
        // Empty-string check precedes the count==0 path — must error, not return "".
        var v = Rep(reg, ctx, 0, "");
        Assert.True(v.IsError);
        Assert.Contains("empty", v.ErrorMessage.ToLowerInvariant());
      });

    [Fact]
    public void StrRepeated_EmptyString_Errors()
      => RunSetup((reg, ctx) =>
      {
        var v = Rep(reg, ctx, 3, "");
        Assert.True(v.IsError);
        Assert.Contains("empty", v.ErrorMessage.ToLowerInvariant());
      });

    [Theory]
    [InlineData(-1.0)]
    [InlineData(2.5)]
    public void StrRepeated_NegativeOrFractionalCount_Errors(double count)
      => RunSetup((reg, ctx) =>
      {
        var v = Rep(reg, ctx, count, "x");
        Assert.True(v.IsError);
      });

    [Fact]
    public void StrRepeated_Alias_StringRepeated()
      => RunSetup((reg, ctx) =>
        Assert.Equal("yoyo", Call(reg, ctx, "string-repeated", HarloweValue.OfNumber(2), HarloweValue.OfString("yo")).AsString));

    [Fact]
    public void StrRepeated_NonStringSecondArg_Errors()
      => RunSetup((reg, ctx) =>
      {
        var v = Call(reg, ctx, "str-repeated", HarloweValue.OfNumber(2), HarloweValue.OfNumber(3));
        Assert.True(v.IsError);
      });

    [Theory]
    [InlineData(400000000.0, "abcdefghij")] // 4e9 chars once int-overflowed the capacity math and threw
    [InlineData(3000000000.0, "x")]         // count > int.MaxValue once hit an unspecified (int) cast
    public void StrRepeated_HugeResult_ErrorsInsteadOfThrowing(double count, string s)
      => RunSetup((reg, ctx) =>
      {
        var v = Rep(reg, ctx, count, s);
        Assert.True(v.IsError);
        Assert.Contains("longer", v.ErrorMessage);
      });

    [Fact]
    public void StrRepeated_WrongArity_Errors()
      => RunSetup((reg, ctx) =>
      {
        var v = Call(reg, ctx, "str-repeated", HarloweValue.OfNumber(2));
        Assert.True(v.IsError);
        Assert.Contains("argument", v.ErrorMessage);
      });

    private static void RunSetup(System.Action<MacroRegistry, MacroContext> body)
    {
      var (reg, ctx) = Setup();
      body(reg, ctx);
    }
  }
}
