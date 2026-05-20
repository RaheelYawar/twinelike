using System;
using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  public class BodyRendererTests
  {
    private class Harness
    {
      public BufferedRenderOutput Buf;
      public MacroContext Ctx;
      public MacroRegistry Registry;
      public HarloweVariableStore Store;
    }

    private static Harness Render(string source, Action<MacroContext> configure = null)
    {
      var tokenizer = new HarloweTokenizer();
      var bodyParser = new HarloweBodyParser();
      var ast = bodyParser.Parse(tokenizer.Tokenize(source));

      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);

      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = registry };
      registry.Context = ctx;

      configure?.Invoke(ctx);

      var buf = new BufferedRenderOutput();
      var renderer = new BodyRenderer(buf, registry, ctx);
      renderer.Render(ast);

      return new Harness { Buf = buf, Ctx = ctx, Registry = registry, Store = store };
    }

    private static int CountKind(BufferedRenderOutput buf, BufferedRenderOutput.Kind k)
    {
      int n = 0;
      foreach (var e in buf.Entries) if (e.Kind == k) n++;
      return n;
    }

    // Plain content ----------------------------------------------------------

    [Fact]
    public void PlainText_RendersUnchanged()
    {
      var h = Render("Hello, world.");
      Assert.Equal("Hello, world.", h.Buf.Text);
    }

    [Fact]
    public void Newline_EmittedAsLineBreak()
    {
      var h = Render("a\nb");
      Assert.Equal("a\nb", h.Buf.Text);
    }

    [Fact]
    public void HtmlTag_RoutedThroughHtmlChannel()
    {
      var h = Render("<b>bold</b>");
      Assert.Equal(2, CountKind(h.Buf, BufferedRenderOutput.Kind.Html));
      Assert.Contains("<b>", h.Buf.Text);
    }

    [Fact]
    public void Link_RoutedThroughLinkChannel()
    {
      var h = Render("[[Continue->Next]]");
      var link = h.Buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Link);
      Assert.NotNull(link);
      Assert.Equal("Continue", link.Content);
      Assert.Equal("Next", link.Target);
    }

    // Variables --------------------------------------------------------------

    [Fact]
    public void Variable_InterpolatedFromStore()
    {
      var h = Render("HP: $hp", ctx => ctx.Store.Set("hp", false, HarloweValue.OfNumber(10)));
      Assert.Contains("HP: 10", h.Buf.Text);
    }

    [Fact]
    public void Variable_Unset_RoutesError()
    {
      var h = Render("$missing");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void Variable_Unset_DoesNotAbortRest()
    {
      var h = Render("before $missing after");
      Assert.Contains("before ", h.Buf.Text);
      Assert.Contains(" after", h.Buf.Text);
    }

    // (set:) / (put:) --------------------------------------------------------

    [Fact]
    public void Set_MutatesStoreAndProducesNoVisibleOutput()
    {
      var h = Render("(set: $hp to 10)");
      Assert.Equal(10, h.Store.Get("hp", false).AsNumber);
      Assert.Equal(string.Empty, h.Buf.Text);
    }

    [Fact]
    public void Set_FollowedByVariableInterpolation()
    {
      var h = Render("(set: $hp to 10)$hp");
      Assert.Equal("10", h.Buf.Text);
    }

    [Fact]
    public void Put_AssignsViaIntoSyntax()
    {
      var h = Render("(put: 7 into $x)$x");
      Assert.Equal("7", h.Buf.Text);
    }

    // (print:) ---------------------------------------------------------------

    [Fact]
    public void Print_OutputsString()
    {
      var h = Render("(print: \"hello\")");
      Assert.Equal("hello", h.Buf.Text);
    }

    [Fact]
    public void Print_OfNumber_StringifiedInvariant()
    {
      var h = Render("(print: 1.5)");
      Assert.Equal("1.5", h.Buf.Text);
    }

    [Fact]
    public void Print_OfVariable()
    {
      var h = Render("(set: $x to 42)(print: $x)");
      Assert.Equal("42", h.Buf.Text);
    }

    // Conditionals -----------------------------------------------------------

    [Fact]
    public void If_TrueRendersHook()
    {
      var h = Render("(if: true)[yes]");
      Assert.Equal("yes", h.Buf.Text);
    }

    [Fact]
    public void If_FalseSkipsHook()
    {
      var h = Render("(if: false)[yes]");
      Assert.Equal(string.Empty, h.Buf.Text);
    }

    [Fact]
    public void Unless_FalseRendersHook()
    {
      var h = Render("(unless: false)[yes]");
      Assert.Equal("yes", h.Buf.Text);
    }

    [Fact]
    public void Unless_TrueSkipsHook()
    {
      var h = Render("(unless: true)[yes]");
      Assert.Equal(string.Empty, h.Buf.Text);
    }

    [Fact]
    public void Else_AfterFailingIf_Renders()
    {
      var h = Render("(if: false)[A](else:)[B]");
      Assert.Equal("B", h.Buf.Text);
    }

    [Fact]
    public void Else_AfterSucceedingIf_Skipped()
    {
      var h = Render("(if: true)[A](else:)[B]");
      Assert.Equal("A", h.Buf.Text);
    }

    [Fact]
    public void Else_AfterIntervening_Set_Skipped()
    {
      // (set:) resets LastConditional, so a following (else:) sees no
      // preceding conditional and renders nothing.
      var h = Render("(if: false)[A](set: $x to 1)(else:)[B]");
      Assert.Equal(string.Empty, h.Buf.Text);
    }

    [Fact]
    public void Else_AfterIntervening_Text_StillPairs()
    {
      // Plain text between (if:) and (else:) should not break the pair.
      var h = Render("(if: false)[A] some text (else:)[B]");
      Assert.Contains("B", h.Buf.Text);
    }

    [Fact]
    public void If_WithVariableCondition()
    {
      var h = Render("(set: $hp to 5)(if: $hp > 0)[alive]",
                     ctx => { /* store wired by harness */ });
      Assert.Equal("alive", h.Buf.Text);
    }

    [Fact]
    public void If_AttachedHookSpacedAndNewline()
    {
      // Body parser already attaches hooks across whitespace/newlines; renderer
      // should honour the same attachment.
      var h = Render("(if: true)\n[hi]");
      Assert.Equal("hi", h.Buf.Text);
    }

    // (goto:) ----------------------------------------------------------------

    [Fact]
    public void Goto_SetsPendingGotoFlag()
    {
      var h = Render("(goto: \"Next\")");
      Assert.Equal("Next", h.Ctx.PendingGoto);
    }

    [Fact]
    public void Goto_AbortsFurtherNodes()
    {
      var h = Render("before (goto: \"Next\") after");
      Assert.Contains("before ", h.Buf.Text);
      Assert.DoesNotContain("after", h.Buf.Text);
    }

    [Fact]
    public void Goto_AbortsHookContents()
    {
      var h = Render("(if: true)[A (goto: \"X\") B]suffix");
      Assert.Contains("A ", h.Buf.Text);
      Assert.DoesNotContain("B", h.Buf.Text);
      Assert.DoesNotContain("suffix", h.Buf.Text);
    }

    // (display:) -------------------------------------------------------------

    [Fact]
    public void Display_DelegatesToRenderPassageCallback()
    {
      // In body position the renderer hands its own output to the callback —
      // the callback writes into it and returns an empty string; DisplayMacro
      // emits no further text on top.
      var h = Render("(display: \"Other\")",
        ctx => ctx.RenderPassage = (name, output) =>
        {
          output.Text($"<<{name}>>");
          return HarloweValue.OfString(string.Empty);
        });
      Assert.Equal("<<Other>>", h.Buf.Text);
    }

    [Fact]
    public void Display_NoCallback_ProducesError()
    {
      var h = Render("(display: \"Other\")");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    // Errors -----------------------------------------------------------------

    [Fact]
    public void MacroArgError_RoutedAndStops()
    {
      // $missing → Error → routed via Error channel; macro never invoked, no goto, render continues for siblings.
      var h = Render("a (set: $x to $missing) b");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("a ", h.Buf.Text);
      Assert.Contains(" b", h.Buf.Text);
    }

    [Fact]
    public void UnknownMacro_RoutesError()
    {
      var h = Render("(notamacro: 1)");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void UnknownMacro_DoesNotEvaluateAssignmentArg()
    {
      // `(notamacro: $x to 5)` is now a parse error rather than an evaluation
      // error: `to`/`into` are only allowed at the top of (set:)/(put:) arg
      // positions, so the parser rejects this before any evaluation can leak
      // an assignment. The store is never touched.
      var ex = Assert.Throws<HarloweParseException>(() => Render("(notamacro: $x to 5)"));
      Assert.Contains("to", ex.Message);
    }

    [Fact]
    public void IfNonBoolArg_RoutesError_NoHookRender()
    {
      var h = Render("(if: 1)[yes]");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.DoesNotContain("yes", h.Buf.Text);
    }

    // Value macros at body position -----------------------------------------

    [Fact]
    public void RandomAtBodyPosition_PrintsValue()
    {
      var h = Render("(random: 1, 1)");  // tight range → deterministic 1
      Assert.Equal("1", h.Buf.Text);
    }

    [Fact]
    public void AMacroAtBodyPosition_PrintsCommaJoined()
    {
      var h = Render("(a: 1, 2, 3)");
      Assert.Equal("1,2,3", h.Buf.Text);
    }

    // Hooks ------------------------------------------------------------------

    [Fact]
    public void StandaloneHook_RendersContent()
    {
      var h = Render("[hello]");
      Assert.Equal("hello", h.Buf.Text);
    }

    [Fact]
    public void NestedHooks_RenderInOrder()
    {
      var h = Render("[outer [inner] tail]");
      Assert.Equal("outer inner tail", h.Buf.Text);
    }

    [Fact]
    public void NamedHook_RendersContent()
    {
      // |greeting>[hi] — right-anchored named hook; v1 just renders content.
      var h = Render("|g>[hi]");
      Assert.Equal("hi", h.Buf.Text);
    }

    // Mixed pipelines --------------------------------------------------------

    [Fact]
    public void MixedShape_FullSlice()
    {
      var h = Render(
        "(set: $hp to 10)" +
        "You have $hp HP.\n" +
        "(if: $hp > 5)[Strong.](else:)[Weak.]\n" +
        "[[Continue->Next]]");
      Assert.Contains("You have 10 HP.", h.Buf.Text);
      Assert.Contains("Strong.", h.Buf.Text);
      Assert.DoesNotContain("Weak.", h.Buf.Text);
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Link));
    }
  }
}
