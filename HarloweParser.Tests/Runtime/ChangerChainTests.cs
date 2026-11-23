using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Coverage for changer chains: <c>$var[hook]</c>, which the parser folds into
  /// a <see cref="ChangerChainNode"/>, and <c>(m1)+(m2)+...[hook]</c>, which it
  /// leaves as siblings for the renderer to combine, because only a changer
  /// joins a <c>+</c>. Expected outputs were measured on real Harlowe 3.3.9.
  /// </summary>
  public class ChangerChainTests
  {
    private static PassageBody ParseBody(string source)
      => new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));

    private static BufferedRenderOutput Render(string source)
    {
      var ast = ParseBody(source);
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = registry };
      registry.Context = ctx;
      // Wrap in HtmlRenderOutput so the prose-level HTML assertions below
      // exercise the full pipeline (semantic events → HTML mapping).
      var buf = new BufferedRenderOutput();
      var output = new HtmlRenderOutput(buf);
      new BodyRenderer(output, registry, ctx).Render(ast);
      return buf;
    }

    // ----- Body parser shape -----

    [Fact]
    public void Parser_PlusBetweenMacros_StaysAmongTheSiblings()
    {
      var body = ParseBody("(text-style: \"bold\")+(text-style: \"italic\")[hi]");
      Assert.Equal(3, body.Children.Count);
      Assert.Null(Assert.IsType<MacroNode>(body.Children[0]).AttachedHook);
      Assert.Equal("+", Assert.IsType<TextNode>(body.Children[1]).Content);
      Assert.NotNull(Assert.IsType<MacroNode>(body.Children[2]).AttachedHook);
    }

    [Fact]
    public void Parser_ChainOfThree_KeepsEachMacro()
    {
      var body = ParseBody("(a:1)+(a:2)+(a:3)[x]");
      Assert.Equal(5, body.Children.Count);
      Assert.NotNull(Assert.IsType<MacroNode>(body.Children[4]).AttachedHook);
    }

    [Fact]
    public void Parser_ChainWithoutHook_LeavesTheLastMacroBare()
    {
      var body = ParseBody("(text-style: \"bold\")+(text-style: \"italic\")");
      Assert.Null(Assert.IsType<MacroNode>(body.Children[2]).AttachedHook);
    }

    [Theory]
    [InlineData("(if: true) [hi]", " ")]
    [InlineData("(if: true)\n[hi]", "\n")]
    [InlineData("(if: true)[hi]", null)]
    public void Parser_RecordsTheGapBeforeAHook(string source, string gap)
    {
      Assert.Equal(gap, Assert.IsType<MacroNode>(ParseBody(source).Children[0]).HookGap);
    }

    [Fact]
    public void Parser_RecordsTheGapAfterAVariable()
    {
      Assert.Equal("  ", Assert.IsType<ChangerChainNode>(ParseBody("$x  [hi]").Children[0]).HookGap);
    }

    [Fact]
    public void Parser_VariableThenHook_BuildsChangerChainNode()
    {
      var body = ParseBody("$bold[hi]");
      var chain = Assert.IsType<ChangerChainNode>(body.Children[0]);
      var varRef = Assert.IsType<VariableRefNode>(chain.Expression);
      Assert.Equal("bold", varRef.Name);
      Assert.False(varRef.IsTemporary);
    }

    [Fact]
    public void Parser_TempVariableThenHook_BuildsChangerChainNode()
    {
      var body = ParseBody("_bold[hi]");
      var chain = Assert.IsType<ChangerChainNode>(body.Children[0]);
      var varRef = Assert.IsType<VariableRefNode>(chain.Expression);
      Assert.Equal("bold", varRef.Name);
      Assert.True(varRef.IsTemporary);
    }

    [Fact]
    public void Parser_VariableWithoutHook_StaysAsVariableNode()
    {
      // Unchanged from v1 — bare $var in prose is just an interpolation.
      var body = ParseBody("Hello $name.");
      Assert.Equal(3, body.Children.Count); // Text, Variable, Text
      Assert.IsType<VariableNode>(body.Children[1]);
    }

    [Fact]
    public void Parser_VariableWithSpaceThenHook_StillFolds()
    {
      // Whitespace between $var and [hook] is allowed, matching macro+hook.
      var body = ParseBody("$bold [hi]");
      Assert.IsType<ChangerChainNode>(body.Children[0]);
    }

    [Fact]
    public void Parser_PlusInProse_NotMistakenForChain()
    {
      // A `+` that isn't between two macros must not trigger chain detection.
      var body = ParseBody("(text-style: \"bold\")[hi] + 5");
      // First child: the macro with hook (no chain). Subsequent children:
      // the trailing prose.
      var first = Assert.IsType<MacroNode>(body.Children[0]);
      Assert.Equal("text-style", first.Name);
      Assert.NotNull(first.AttachedHook);
    }

    [Fact]
    public void Parser_MacroPlusText_NotChain()
    {
      // `(macro)+text` (no second macro after the +) doesn't trigger chain.
      var body = ParseBody("(text-style: \"bold\")+something");
      Assert.IsType<MacroNode>(body.Children[0]);
    }

    [Fact]
    public void Render_SetBeforeAPlus_IsNotAChain()
    {
      // (set:) isn't a changer, so the + after it is plain prose.
      var buf = Render("(set: $x to 5) + (print: 42) tail");
      Assert.Equal(" + 42 tail", buf.Text);
      Assert.Empty(buf.Entries.FindAll(e => e.Kind == BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void Render_ChangerPlusAssignment_ErrorsAndStillAssigns()
    {
      // 3.3.9 runs the assignment while evaluating it, then refuses to combine it.
      var buf = Render("(text-style: \"bold\")+(set: $x to 1)$x");
      Assert.Equal("1", buf.Text);
      var error = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("(set:) operation", error.Content);
    }

    [Fact]
    public void Parser_PlainSetMacro_StillParsesAsBodyMacro()
    {
      var body = ParseBody("(set: $x to 1)");
      var macro = Assert.IsType<MacroNode>(body.Children[0]);
      Assert.Equal("set", macro.Name);
    }

    // ----- Body renderer behaviour -----

    [Fact]
    public void Render_CanonicalHarloweInlineSyntax()
    {
      // The exact example from Harlowe's docs.
      var buf = Render("(text-style:\"bold\")+(text-style:\"italic\")[Important]");
      Assert.Equal("<b><i>Important</i></b>", buf.Text);
    }

    [Fact]
    public void Render_StoredChanger_AppliesToHook()
    {
      var buf = Render("(set: $bold to (text-style: \"bold\"))$bold[hi]");
      Assert.Equal("<b>hi</b>", buf.Text);
    }

    [Fact]
    public void Render_StoredChangerWithSpace_StillApplies()
    {
      var buf = Render("(set: $bold to (text-style: \"bold\"))$bold [hi]");
      Assert.Equal("<b>hi</b>", buf.Text);
    }

    [Fact]
    public void Render_StoredComposition_AppliesBothLayers()
    {
      var buf = Render(
        "(set: $combo to (text-style: \"bold\") + (text-style: \"italic\"))$combo[x]");
      Assert.Equal("<b><i>x</i></b>", buf.Text);
    }

    [Fact]
    public void Render_NonChangerVariable_FallsBackToValuePlusHook()
    {
      // Stories that use $var[content] for non-changer variables (rare but
      // legal) should still see the variable's text emitted, then the hook
      // contents — preserves backward compat.
      var buf = Render("(set: $name to \"Bob\")$name[ says hi]");
      Assert.Equal("Bob says hi", buf.Text);
    }

    [Fact]
    public void Render_UnsetVariableInChain_EmitsError()
    {
      var buf = Render("_missing[hi]");
      var err = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
    }

    [Fact]
    public void Render_ChainWithoutHook_NoVisibleOutput()
    {
      var buf = Render("(text-style: \"bold\")+(text-style: \"italic\")");
      Assert.Equal(string.Empty, buf.Text);
      Assert.Single(buf.Entries.FindAll(e => e.Kind == BufferedRenderOutput.Kind.Error));
    }

    [Theory]
    [InlineData("(print: 1)+(print: 2)z", "1+2z")]
    [InlineData("(print: 1) + (print: 2)[hook]", "1 + 2hook")]
    [InlineData("(print: 2)+(text-style:'bold')[hook]", "2+<b>hook</b>")]
    [InlineData("(print: 'x')+[hook]", "x+hook")]
    [InlineData("(set: $c to 5)$c+$c[hook]", "5+5hook")]
    [InlineData("(set: $n to 5)$n+(text-style:'bold')[x]", "5+<b>x</b>")]
    public void Render_APlusAfterANonChanger_IsProse(string source, string expected)
    {
      Assert.Equal(expected, Render(source).Text);
    }

    [Theory]
    [InlineData("(set: $c to (text-style:'bold'))$c+(text-style:'italic')[x]", "<b><i>x</i></b>")]
    [InlineData("(set: $c to (text-style:'bold'))(text-style:'italic')+$c[x]", "<i><b>x</b></i>")]
    [InlineData("(text-style:'bold')\n+\n(text-style:'italic')\n[x]", "<b><i>x</i></b>")]
    [InlineData("(text-style:'bold')+(text-style:'italic')(print: 5)", "<b><i>5</i></b>")]
    [InlineData("(text-style:'bold') + [x]", " + <b>x</b>")]
    [InlineData("(text-style:'bold')+[x]+[y]", "+<b>x</b>+y")]
    public void Render_ChainsThroughVariablesAndCommands(string source, string expected)
    {
      Assert.Equal(expected, Render(source).Text);
    }

    [Theory]
    [InlineData("(text-style:'bold')+(foo:)[x]", "+x", "unknown macro")]
    [InlineData("(text-style:'bold')+$nope", "", "I can't combine")]
    [InlineData("(text-style:'bold')+(print: 2)z", "z", "remove the +")]
    [InlineData("(text-style:'bold') + z", " + z", "should be stored")]
    public void Render_ABrokenChain_ReportsInTheChangersPlace(string source, string expected, string error)
    {
      var buf = Render(source);
      Assert.Equal(expected, buf.Text);
      Assert.Contains(error, buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error).Content);
    }

    [Fact]
    public void Render_ChainOfThree_NestsAllThree()
    {
      // Build via composition: the storage path is more reliable than trying
      // to wire three text-style calls through the inline syntax.
      var buf = Render(
        "(text-style: \"bold\")+(text-style: \"italic\")+(text-style: \"underline\")[x]");
      Assert.Equal("<b><i><u>x</u></i></b>", buf.Text);
    }

    // ----- MarkupPrinter round-trip -----

    [Fact]
    public void Printer_RoundTrips_InlineChain()
    {
      string source = "(text-style: \"bold\")+(text-style: \"italic\")[hi]";
      var ast = ParseBody(source);
      string printed = new MarkupPrinter().Print(ast);
      var reparsed = ParseBody(printed);
      string printedAgain = new MarkupPrinter().Print(reparsed);
      Assert.Equal(printed, printedAgain);
    }

    [Fact]
    public void Printer_RoundTrips_StoredChanger()
    {
      string source = "$bold[hi]";
      var ast = ParseBody(source);
      string printed = new MarkupPrinter().Print(ast);
      var reparsed = ParseBody(printed);
      string printedAgain = new MarkupPrinter().Print(reparsed);
      Assert.Equal(printed, printedAgain);
    }

    [Fact]
    public void Printer_KeepsThePlusAsWritten()
    {
      // The + is prose unless both sides turn out to be changers, so its
      // spacing can show in the page and has to survive a reprint.
      var ast = ParseBody("(text-style:\"bold\") + (text-style:\"italic\") [hi]");
      string printed = new MarkupPrinter().Print(ast);
      Assert.Equal("(text-style: \"bold\") + (text-style: \"italic\") [hi]", printed);
    }
  }
}
