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
  /// Coverage for the ChangerChainNode pipeline (v2.1A.1): body parser
  /// detection, renderer behaviour for both Changer and non-Changer values,
  /// and MarkupPrinter round-trip. Exercises both shapes the parser folds:
  /// <c>(m1)+(m2)+...[hook]</c> chains and <c>$var[hook]</c> stored-changer
  /// applications.
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
    public void Parser_ChainOfTwoMacros_BuildsChangerChainNode()
    {
      var body = ParseBody("(text-style: \"bold\")+(text-style: \"italic\")[hi]");
      Assert.Single(body.Children);
      var chain = Assert.IsType<ChangerChainNode>(body.Children[0]);
      var bin = Assert.IsType<BinaryOpNode>(chain.Expression);
      Assert.Equal("+", bin.Operator);
      Assert.IsType<MacroCallNode>(bin.Left);
      Assert.IsType<MacroCallNode>(bin.Right);
      Assert.NotNull(chain.AttachedHook);
    }

    [Fact]
    public void Parser_ChainOfThree_LeftAssociative()
    {
      // (a)+(b)+(c) parses as ((a + b) + c) because + is left-associative.
      var body = ParseBody("(a:1)+(a:2)+(a:3)[x]");
      var chain = (ChangerChainNode)body.Children[0];
      var outer = (BinaryOpNode)chain.Expression;
      var inner = (BinaryOpNode)outer.Left;
      Assert.IsType<MacroCallNode>(inner.Left);
      Assert.IsType<MacroCallNode>(inner.Right);
      Assert.IsType<MacroCallNode>(outer.Right);
    }

    [Fact]
    public void Parser_ChainWithoutHook_StillBuildsChangerChain()
    {
      // Authored tail-less chains are unusual but legal — the chain still
      // evaluates, just produces no visible output (changer with null hook).
      var body = ParseBody("(text-style: \"bold\")+(text-style: \"italic\")");
      var chain = Assert.IsType<ChangerChainNode>(body.Children[0]);
      Assert.Null(chain.AttachedHook);
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
    public void Parser_SetAsFirstChainComponent_Rejected()
    {
      // (set:) is an assignment macro — letting it ride into a chain would
      // mutate state on evaluation before producing the inevitable chain-
      // type error at render time. The body parser recovers per-node, so the
      // misuse surfaces as a ParseErrorNode in the AST rather than escaping
      // as an exception — but the diagnostic still fires before the AST
      // node carrying the (set:) MacroCallNode is produced, so no eval can
      // touch the store.
      var body = ParseBody("(set: $x to 1)+(text-style: \"bold\")[hi]");
      var err = Assert.IsType<ParseErrorNode>(body.Children[body.Children.Count - 1]);
      Assert.Contains("set", err.Message);
    }

    [Fact]
    public void Parser_PutAsContinuationChainComponent_Rejected()
    {
      var body = ParseBody("(text-style: \"bold\")+(put: $x into 1)[hi]");
      var err = Assert.IsType<ParseErrorNode>(body.Children[body.Children.Count - 1]);
      Assert.Contains("put", err.Message);
    }

    [Fact]
    public void Parser_PlainSetMacro_StillParsesAsBodyMacro()
    {
      // Regression check: rejecting (set:) in chains must not affect the
      // plain-body (set:) call shape.
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
      var buf = Render("$missing[hi]");
      var err = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
    }

    [Fact]
    public void Render_ChainWithoutHook_NoVisibleOutput()
    {
      var buf = Render("(text-style: \"bold\")+(text-style: \"italic\")");
      Assert.Equal(string.Empty, buf.Text);
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
    public void Printer_PrintsExpressionThenHook()
    {
      // Locks the canonical printed form so a future formatter change is
      // visible. The expression printer adds spaces around `+`.
      var ast = ParseBody("(text-style:\"bold\")+(text-style:\"italic\")[hi]");
      string printed = new MarkupPrinter().Print(ast);
      Assert.Equal("(text-style: \"bold\") + (text-style: \"italic\")[hi]", printed);
    }
  }
}
