using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for <c>(str-nth: Number)</c> / <c>(string-nth:)</c> — the English
  /// ordinal abbreviation of a whole number (reference <c>ts/macrolib/values.ts</c>:
  /// <c>nth(parseInt(num))</c>). The number truncates toward zero; the teen
  /// special-case (11/12/13 → "th") and sign are handled.
  /// </summary>
  public class StrNthMacroTests
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

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(101, "101st")]
    [InlineData(111, "111th")]
    [InlineData(0, "0th")]
    [InlineData(-7, "-7th")]
    [InlineData(-1, "-1st")]
    [InlineData(-11, "-11th")]
    [InlineData(2.7, "2nd")] // truncates toward zero
    [InlineData(-3.9, "-3rd")]
    public void StrNth(double input, string expected)
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "str-nth", HarloweValue.OfNumber(input));
      Assert.Equal(HarloweValueKind.String, v.Kind);
      Assert.Equal(expected, v.AsString);
    }

    [Fact]
    public void StrNth_Alias_StringNth()
    {
      var (reg, ctx) = Setup();
      Assert.Equal("3rd", Call(reg, ctx, "string-nth", HarloweValue.OfNumber(3)).AsString);
    }

    [Fact]
    public void StrNth_NonNumberArg_ReturnsError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "str-nth", HarloweValue.OfString("x"));
      Assert.True(v.IsError);
      Assert.Contains("number", v.ErrorMessage.ToLowerInvariant());
    }

    [Fact]
    public void StrNth_NoArgs_ReturnsArityError()
    {
      var (reg, ctx) = Setup();
      var v = Call(reg, ctx, "str-nth");
      Assert.True(v.IsError);
      Assert.Contains("argument", v.ErrorMessage);
    }
  }
}
