using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// The typed Colour value: literal lexing (built-in names + hex), the
  /// <c>(rgb:)</c>/<c>(hsl:)</c> constructors, <c>+</c> mixing, data-name
  /// access, equality, and save/load source round-tripping. Reference:
  /// <c>ts/datatypes/colour.ts</c> and the colour macros in
  /// <c>ts/macrolib/values.ts</c>.
  /// </summary>
  public class ColourValueTests
  {
    private static (MacroRegistry reg, MacroContext ctx) Setup()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = reg };
      reg.Context = ctx;
      return (reg, ctx);
    }

    private static HarloweValue Eval(string expr)
    {
      var (reg, ctx) = Setup();
      var tokens = new HarloweTokenizer().Tokenize("(_:" + expr + ")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var node = new HarloweExpressionParser().ParseExpression(cursor);
      return new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker).Evaluate(node);
    }

    // --- Literals ---

    [Fact]
    public void NamedColour_LexesAsColourValue()
    {
      var v = Eval("red");
      Assert.Equal(HarloweValueKind.Colour, v.Kind);
      // Reference's table: red is #e61919.
      Assert.Equal(0xe6, v.AsColour.R);
      Assert.Equal(0x19, v.AsColour.G);
      Assert.Equal(0x19, v.AsColour.B);
      Assert.Equal(1, v.AsColour.A);
    }

    [Theory]
    [InlineData("aqua", "cyan")]
    [InlineData("fuchsia", "magenta")]
    [InlineData("grey", "gray")]
    public void NamedColour_AliasPairs_AreEqual(string a, string b)
    {
      Assert.True(Eval(a).Equals(Eval(b)));
    }

    [Fact]
    public void NamedColour_IsCaseInsensitive()
    {
      Assert.True(Eval("NAVY").Equals(Eval("navy")));
    }

    [Fact]
    public void Transparent_HasZeroAlpha()
    {
      Assert.Equal(0, Eval("transparent").AsColour.A);
    }

    [Fact]
    public void HexColour_SixDigit_Parses()
    {
      var c = Eval("#691212").AsColour;
      Assert.Equal(0x69, c.R);
      Assert.Equal(0x12, c.G);
      Assert.Equal(0x12, c.B);
    }

    [Fact]
    public void HexColour_ThreeDigit_ExpandsEachDigit()
    {
      // #a4e is #aa44ee.
      var c = Eval("#a4e").AsColour;
      Assert.Equal(0xaa, c.R);
      Assert.Equal(0x44, c.G);
      Assert.Equal(0xee, c.B);
    }

    [Fact]
    public void HexColour_IsCaseInsensitive()
    {
      Assert.True(Eval("#ABCDEF").Equals(Eval("#abcdef")));
    }

    [Fact]
    public void ColourName_AsString_IsStillAString()
    {
      // Only the bare keyword is a colour — quoting it keeps a plain string,
      // as in reference (where `(bg: "blue")` is an image path).
      var v = Eval("\"red\"");
      Assert.Equal(HarloweValueKind.String, v.Kind);
    }

    // --- (rgb:) / (hsl:) ---

    [Fact]
    public void Rgb_BuildsColour()
    {
      var c = Eval("(rgb: 255, 0, 47)").AsColour;
      Assert.Equal(255, c.R);
      Assert.Equal(0, c.G);
      Assert.Equal(47, c.B);
      Assert.Equal(1, c.A);
    }

    [Fact]
    public void Rgb_OptionalAlpha()
    {
      Assert.Equal(0.6, Eval("(rgb: 178, 229, 178, 0.6)").AsColour.A, 3);
    }

    [Fact]
    public void Rgba_Alias_Works()
    {
      Assert.True(Eval("(rgba: 1, 2, 3)").Equals(Eval("(rgb: 1, 2, 3)")));
    }

    [Fact]
    public void Rgb_FractionalComponent_Allowed()
    {
      // Reference: "Former versions did not allow fractional values, but that
      // restriction is no longer present."
      Assert.Equal(12.5, Eval("(rgb: 12.5, 0, 0)").AsColour.R, 3);
    }

    [Theory]
    [InlineData("(rgb: 256, 0, 0)")]
    [InlineData("(rgb: -1, 0, 0)")]
    public void Rgb_OutOfRangeComponent_Errors(string src)
    {
      var v = Eval(src);
      Assert.True(v.IsError);
      Assert.Contains("between 0 and 255", v.ErrorMessage);
    }

    [Fact]
    public void Rgb_OutOfRangeAlpha_Errors()
    {
      var v = Eval("(rgb: 0, 0, 0, 1.5)");
      Assert.True(v.IsError);
      Assert.Contains("alpha", v.ErrorMessage);
    }

    [Fact]
    public void Hsl_BuildsColour()
    {
      // The CSSWG hsl-to-rgb algorithm reference runs gives (25, 230, 25) here
      // — which is exactly its own `green` table entry, #19e619. (Reference's
      // (hsl:) doc example text says rgb(25,229,25); that prose is off by one
      // against both the algorithm and its colour table.)
      var c = Eval("(hsl: 120, 0.8, 0.5)").AsColour;
      Assert.Equal(25, c.R);
      Assert.Equal(230, c.G);
      Assert.Equal(25, c.B);
    }

    [Fact]
    public void Hsl_HueWrapsPastThreeSixty()
    {
      // Reference: "a value of 380 will become 20". Cycling counters must work.
      Assert.True(Eval("(hsl: 380, 1, 0.5)").Equals(Eval("(hsl: 20, 1, 0.5)")));
    }

    [Fact]
    public void Hsl_NegativeHue_Wraps()
    {
      Assert.True(Eval("(hsl: -20, 1, 0.5)").Equals(Eval("(hsl: 340, 1, 0.5)")));
    }

    [Fact]
    public void Hsl_OutOfRangeSaturation_Errors()
    {
      var v = Eval("(hsl: 120, 1.5, 0.5)");
      Assert.True(v.IsError);
      Assert.Contains("saturation", v.ErrorMessage);
    }

    [Fact]
    public void Hsla_Alias_Works()
    {
      Assert.True(Eval("(hsla: 120, 0.8, 0.5)").Equals(Eval("(hsl: 120, 0.8, 0.5)")));
    }

    [Fact]
    public void NamedColours_DeriveFromTheirHslDefinitions()
    {
      // The built-in table is hsl(hue, 0.8, 0.5) — reference's own derivation,
      // and running (hsl:) reproduces the table's hex for these hues.
      Assert.True(Eval("green").Equals(Eval("(hsl: 120, 0.8, 0.5)")));
      Assert.True(Eval("red").Equals(Eval("(hsl: 0, 0.8, 0.5)")));

      // Not every entry does: reference's hand-authored table rounds `blue` to
      // #197fe6 (g = 127) where the live conversion of hsl(210, 0.8, 0.5) lands
      // on g = 128 — a boundary .5 rounded the other way. We keep the table's
      // value for the keyword and the algorithm's value for the macro, exactly
      // as reference does, rather than "fixing" either into disagreement.
      Assert.Equal(127, Eval("blue").AsColour.G);
      Assert.Equal(128, Eval("(hsl: 210, 0.8, 0.5)").AsColour.G);
    }

    // --- Data names ---

    [Fact]
    public void Colour_RgbaDataNames()
    {
      Assert.Equal(90, Eval("(rgb: 90, 0, 0)'s r").AsNumber);
      Assert.Equal(47, Eval("(rgb: 0, 47, 0)'s g").AsNumber);
      Assert.Equal(3, Eval("(rgb: 0, 0, 3)'s b").AsNumber);
      Assert.Equal(0.5, Eval("(rgb: 0, 0, 0, 0.5)'s a").AsNumber, 3);
    }

    [Fact]
    public void Colour_HslDataNames()
    {
      // Reference's example: (hsl: 28, 1, 0.4)'s h is 28.
      Assert.Equal(28, Eval("(hsl: 28, 1, 0.4)'s h").AsNumber);
      Assert.Equal(1, Eval("(hsl: 28, 1, 0.4)'s s").AsNumber, 2);
      Assert.Equal(0.4, Eval("(hsl: 28, 1, 0.4)'s l").AsNumber, 2);
    }

    [Fact]
    public void Colour_UnknownDataName_Errors()
    {
      var v = Eval("red's z");
      Assert.True(v.IsError);
      Assert.Contains("no property", v.ErrorMessage);
    }

    // --- Mixing ---

    [Fact]
    public void Colour_Plus_MixesAdditively()
    {
      // Reference's "+": min(round((l + r) * 0.6), 255) per channel.
      // red (230,25,25) + white (255,255,255) → (255, 168, 168).
      var c = Eval("red + white").AsColour;
      Assert.Equal(255, c.R);
      Assert.Equal(168, c.G);
      Assert.Equal(168, c.B);
    }

    [Fact]
    public void Colour_Plus_AveragesAlpha()
    {
      // Mixing with `transparent` (alpha 0) halves the alpha — reference's
      // documented way to make a colour partly transparent.
      Assert.Equal(0.5, Eval("red + transparent").AsColour.A, 3);
    }

    [Fact]
    public void Colour_PlusNonColour_Errors()
    {
      Assert.True(Eval("red + 5").IsError);
    }

    // --- Equality, printing, source ---

    [Fact]
    public void Colour_Equality_IsByComponent()
    {
      Assert.True(Eval("#ff0000").Equals(Eval("(rgb: 255, 0, 0)")));
      Assert.False(Eval("red").Equals(Eval("blue")));
    }

    [Fact]
    public void Colour_IsOperator_Works()
    {
      Assert.True(Eval("red is (hsl: 0, 0.8, 0.5)").AsBool);
      Assert.True(Eval("red is not blue").AsBool);
    }

    [Fact]
    public void Colour_PrintsAsCssString()
    {
      Assert.Equal("rgba(230, 25, 25, 1)", Eval("red").ToHarloweString());
    }

    [Fact]
    public void Colour_ToSource_RoundTrips()
    {
      var v = Eval("(rgb: 12, 34, 56, 0.5)");
      string src = v.ToSource();
      Assert.Equal("(rgb:12,34,56,0.5)", src);
      Assert.True(Eval(src).Equals(v));
    }

    [Fact]
    public void Colour_ToSource_TransparentIsNamed()
    {
      // Reference's toSource() special-cases zero alpha as `transparent`.
      Assert.Equal("transparent", Eval("transparent").ToSource());
    }

    [Fact]
    public void Colour_ToSource_OmitsFullAlpha()
    {
      Assert.Equal("(rgb:255,0,0)", Eval("#f00").ToSource());
    }

    // --- Round-trip through the printer ---

    [Theory]
    [InlineData("(set: $c to red)")]
    [InlineData("(set: $c to #a4e)")]
    [InlineData("(text-colour: navy)[hi]")]
    public void ColourLiteral_RoundTripsThroughMarkupPrinter(string source)
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));
      Assert.Equal(source, new MarkupPrinter().Print(ast));
    }

    // --- Storage ---

    [Fact]
    public void Colour_FlowsThroughVariables()
    {
      // Colours are immutable, so the store may share one instance freely —
      // what matters is that the value survives assignment intact.
      var (reg, ctx) = Setup();
      var ast = new HarloweBodyParser().Parse(
        new HarloweTokenizer().Tokenize("(set: $a to red)(set: $b to $a)"));
      new BodyRenderer(new BufferedRenderOutput(), reg, ctx).Render(ast);

      Assert.True(ctx.Store.Get("a", false).Equals(ctx.Store.Get("b", false)));
      Assert.Equal(HarloweValueKind.Colour, ctx.Store.Get("b", false).Kind);
    }

    [Fact]
    public void Colour_PropertyNamesAreNotStolenByTheColourLexer()
    {
      // A colour name in property position is a data key, not a value —
      // reference captures the name inside its possessive token. Likewise `a`
      // (a colour's alpha) must not lex as the `a` word-operator.
      Assert.Equal(1, Eval("(dm: \"red\", 1)'s red").AsNumber);
      Assert.Equal(2, Eval("red of (dm: \"red\", 2)").AsNumber);
      Assert.Equal(0.5, Eval("(rgb: 0, 0, 0, 0.5)'s a").AsNumber, 3);
    }
  }
}
