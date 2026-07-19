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

    // Property assignment ----------------------------------------------------

    [Fact]
    public void PropertyAssignment_SetThenPrint()
    {
      var h = Render("(set: $dm to (dm: \"hp\", 10))(set: $dm's hp to it - 1)HP: (print: $dm's hp)");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("HP: 9", h.Buf.Text);
    }

    [Fact]
    public void PropertyAssignment_CanSetFailure_RendersErrorAndContinues()
    {
      // The canSet error surfaces in-prose at the (set:) and the rest of the
      // passage still renders.
      var h = Render("(set: $arr to (a: 1))(set: $arr's length to 5)after");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("after", h.Buf.Text);
    }

    // (move:) ----------------------------------------------------------------

    [Fact]
    public void Move_EndToEnd_DeckDraw()
    {
      var h = Render("(set: $deck to (a: 5, 6, 7))(move: $deck's 1st into $card)(print: $card)-(print: $deck's length)");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("5-2", h.Buf.Text);
    }

    [Fact]
    public void Move_Error_PassageContinues()
    {
      var h = Render("(move: 5 into $x)after");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("after", h.Buf.Text);
    }

    [Fact]
    public void Random_MoveDeckDraw_EndToEnd()
    {
      var h = Render("(set: $deck to (a: 1, 2, 3))(move: $deck's random into $card)(print: $deck's length)");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("2", h.Buf.Text);
    }

    // (unpack:) --------------------------------------------------------------

    [Fact]
    public void Unpack_EndToEnd()
    {
      var h = Render("(set: $roll to (a: 3, 4))(unpack: $roll into (a: $first, $second))(print: $first + $second)");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("7", h.Buf.Text);
    }

    [Fact]
    public void MovePattern_EndToEnd()
    {
      // The reference doc's move-unpacking idiom: both drawn cards land and
      // the deck shrinks by two.
      var h = Render("(set: $deck to (a: 5, 6, 7))(move: $deck into (a: $c1, $c2))(print: $c1)-(print: $c2)-(print: $deck's length)");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("5-6-1", h.Buf.Text);
    }

    [Fact]
    public void Unpack_GateError_PassageContinues()
    {
      // The dest-shape gate fires before anything evaluates.
      var h = Render("(unpack: (a: 1) into $x)after");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("after", h.Buf.Text);
      Assert.Null(h.Store.Get("x", false));
    }

    // Assignment operator discipline (reference's typeChecker pre-pass) ------

    [Fact]
    public void Set_IntoOperator_Errors_NothingAssigned()
    {
      // The pre-scan runs before any argument evaluates, so the valid first
      // argument doesn't land either.
      var h = Render("(set: $a to 1, 5 into $b)done");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Null(h.Store.Get("a", false));
      Assert.Null(h.Store.Get("b", false));
      Assert.Contains("done", h.Buf.Text);
    }

    [Fact]
    public void Put_ToOperator_Errors()
    {
      var h = Render("(put: $x to 5)");
      var err = h.Buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
      Assert.Equal("Please say 'into' when using the (put:) or (unpack:) macro.", err.Content);
    }

    [Fact]
    public void Move_ToOperator_Errors()
    {
      var h = Render("(move: $x to 5)");
      var err = h.Buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
      Assert.Equal("Please say 'into' when using the (move:) macro.", err.Content);
    }

    [Fact]
    public void Set_NonAssignmentArg_Errors()
    {
      // Previously a silent no-op; reference rejects a non-assignment
      // argument in the type check.
      var h = Render("(set: 5)");
      var err = h.Buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
      Assert.Contains("requires a 'to' assignment", err.Content);
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

    // Case/dash-insensitive macro names -------------------------------------

    [Fact]
    public void MacroNames_CaseInsensitiveConditionalPairing()
    {
      // (If:)/(Else:) must dispatch AND pair like (if:)/(else:) — name
      // normalization applies to the renderer's conditional detection too.
      var h = Render("(If: false)[A](Else:)[B]");
      Assert.Equal("B", h.Buf.Text);
    }

    [Fact]
    public void MacroNames_CaseInsensitiveAssignmentMacro()
    {
      // (SET:) is recognized as an assignment macro (so `to` parses) and
      // dispatches to SetMacro.
      var h = Render("(SET: $x to 5)(print: $x)");
      Assert.Equal("5", h.Buf.Text);
    }

    [Fact]
    public void MacroNames_DashInsensitiveGoto()
    {
      // (go-to:) is the manual's spelling; it normalizes to the registered
      // "goto" and sets the pending navigation.
      var h = Render("(go-to: \"Next\")");
      Assert.Equal("Next", h.Ctx.PendingGoto);
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
    public void Else_AfterIntervening_Set_StillPairs()
    {
      // Reference Harlowe only updates the conditional pairing when a hook is
      // shown/hidden by an attached expression; a plain command macro like
      // (set:) leaves it untouched, so the (else:) still pairs with the
      // failing (if:) and renders.
      var h = Render("(if: false)[A](set: $x to 1)(else:)[B]");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("B", h.Buf.Text);
    }

    // Conditional changers ----------------------------------------------------
    // (if:)/(unless:)/(else-if:)/(else:) return Changers (reference model), so
    // they compose with styling changers via + and store in variables.

    [Fact]
    public void If_ComposedWithStyle_True_RendersStyledHook()
    {
      var h = Render("(if: true) + (text-style: \"bold\")[X]");
      Assert.Equal("X", h.Buf.Text);
      var push = h.Buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.PushStyle);
      Assert.NotNull(push);
      Assert.True(push.Style.Bold);
    }

    [Fact]
    public void If_ComposedWithStyle_False_RendersNothing()
    {
      var h = Render("(if: false) + (text-style: \"bold\")[X]");
      Assert.Equal("", h.Buf.Text);
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.PushStyle));
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void If_StoredComposedChanger_AppliesViaVariable()
    {
      // The former high-severity divergence: a conditional composed with a
      // styling changer, stored, and attached — reference's headline idiom.
      var shown = Render("(set: $c to (if: true) + (text-style: \"bold\"))$c[content]");
      Assert.Equal("content", shown.Buf.Text);
      var push = shown.Buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.PushStyle);
      Assert.NotNull(push);
      Assert.True(push.Style.Bold);

      var hidden = Render("(set: $c to (if: false) + (text-style: \"bold\"))$c[content]");
      Assert.Equal("", hidden.Buf.Text);
      Assert.Equal(0, CountKind(hidden.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void Else_PairsAcrossComposedConditionalHook()
    {
      var hidden = Render("(if: false) + (text-style: \"bold\")[A](else:)[B]");
      Assert.Equal("B", hidden.Buf.Text);
      var shown = Render("(if: true) + (text-style: \"bold\")[A](else:)[B]");
      Assert.Equal("A", shown.Buf.Text);
    }

    [Fact]
    public void NestedIf_InExpression_DoesNotCorruptPairing()
    {
      // A nested (if:) evaluated inside (set:) args creates a changer but must
      // not touch the pairing — the (else:) still pairs with the shown
      // (if: true) hook. (The old Bool model set the pairing at invoke time,
      // so the nested call wrongly flipped it and B rendered.)
      var h = Render("(if: true)[A](set: $x to (if: false))(else:)[B]");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Equal("A", h.Buf.Text);
    }

    [Fact]
    public void StoredConditional_AttachedViaVariable_Hides_PreservesPairing()
    {
      // Reference gates the pairing's false-write on the applying expression's
      // front macro name (section.ts's [`if`,`elseif`,`unless`,`else`] list) —
      // a hide from a stored changer in a variable preserves the prior value,
      // so the (else:) still pairs with the shown (if: true) and stays hidden.
      var h = Render("(if: true)[A](set: $c to (if: false))$c[B](else:)[C]");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Equal("A", h.Buf.Text);

      // With nothing pairable before it, the variable-attached hide leaves the
      // pairing unset and the (else:) errors, exactly as reference does.
      var stray = Render("(set: $c to (if: false))$c[A](else:)[B]");
      Assert.Equal("", stray.Buf.Text);
      Assert.Equal(1, CountKind(stray.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void ComposedHide_NonConditionalFront_PreservesPairing()
    {
      // (text-style:)+(if:false) hides its hook, but the expression's front
      // macro isn't a conditional, so the pairing keeps the (if: true) hook's
      // "shown" and the (else:) stays hidden.
      var h = Render("(if: true)[A](text-style: \"bold\")+(if: false)[B](else:)[C]");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Equal("A", h.Buf.Text);
    }

    [Fact]
    public void UnattachedConditional_ErrorsInProse()
    {
      // Reference: "The (if:) changer should be stored in a variable or
      // attached to a hook." With no pairing written, the (else:) errors too
      // rather than pairing with the dropped (if:).
      var h = Render("(if: false)(else:)[B]");
      Assert.Equal("", h.Buf.Text);
      Assert.Equal(2, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void AttachedBoolean_HidesHook_AndFeedsPairing()
    {
      // Reference section.ts: "Attached false values hide hooks as well...
      // an (else:) that follows this will pass." The boolean itself prints
      // nothing when attached.
      var hidden = Render("(set: $x to false)$x[secret](else:)[shown]");
      Assert.Equal("shown", hidden.Buf.Text);

      var shownHook = Render("(set: $x to true)$x[content](else:)[other]");
      Assert.Equal("content", shownHook.Buf.Text);
    }

    [Fact]
    public void Else_AfterIntervening_ErroringMacro_StillPairs()
    {
      // An intervening macro whose argument errors must behave the same as one
      // that succeeds: neither breaks the pairing. Previously the error path
      // early-returned before a reset, making the outcome depend on whether
      // the intervening macro happened to error.
      var h = Render("(if: false)[A](print: $undef)(else:)[B]");
      Assert.Contains("B", h.Buf.Text);
    }

    [Fact]
    public void Else_AfterInterveningChangerHook_Hidden()
    {
      // A non-conditional changer that SHOWS its hook records "a hook was
      // shown", so a following (else:) sees a shown hook and hides — matching
      // reference, where any enabled attached-expression hook sets
      // lastHookShown=true.
      var h = Render("(if: false)[A](text-style: \"bold\")[B](else:)[C]");
      Assert.Contains("B", h.Buf.Text);
      Assert.DoesNotContain("C", h.Buf.Text);
    }

    [Fact]
    public void Else_NestedConditionalInHook_DoesNotLeak()
    {
      // A conditional nested inside a shown hook is its own scope: it must not
      // pair with an (else:) outside that hook. Here the outer (if: true) shows
      // its hook, so the trailing (else:) must hide regardless of the inner
      // (if: false).
      var h = Render("(if: true)[before(if: false)[X]after](else:)[Y]");
      Assert.Contains("before", h.Buf.Text);
      Assert.Contains("after", h.Buf.Text);
      Assert.DoesNotContain("X", h.Buf.Text);
      Assert.DoesNotContain("Y", h.Buf.Text);
    }

    [Fact]
    public void Else_StrayWithNoPrecedingConditional_Errors()
    {
      var h = Render("(else:)[B]");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.DoesNotContain("B", h.Buf.Text);
    }

    [Fact]
    public void Else_AfterIntervening_Text_StillPairs()
    {
      // Plain text between (if:) and (else:) should not break the pair.
      var h = Render("(if: false)[A] some text (else:)[B]");
      Assert.Contains("B", h.Buf.Text);
    }

    // (else-if:) -------------------------------------------------------------

    [Fact]
    public void ElseIf_AfterFailingIf_TrueCond_Renders()
    {
      var h = Render("(if: false)[A](else-if: true)[B]");
      Assert.Equal("B", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_AfterFailingIf_FalseCond_Skipped()
    {
      var h = Render("(if: false)[A](else-if: false)[B]");
      Assert.Equal(string.Empty, h.Buf.Text);
    }

    [Fact]
    public void ElseIf_AfterSucceedingIf_Skipped_RegardlessOfCond()
    {
      // The (if:) already matched, so the (else-if:) hides even though its own
      // condition is true.
      var h = Render("(if: true)[A](else-if: true)[B]");
      Assert.Equal("A", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_HidePreservesChainState_TrailingElseDoesNotRender()
    {
      // The load-bearing special case: when (if:) matched and a non-matching
      // (else-if:) hides, the trailing (else:) must STILL see "a branch already
      // matched" and stay hidden. Without (else-if:) preserving LastConditional
      // on hide, this would wrongly render "AC".
      var h = Render("(if: true)[A](else-if: true)[B](else:)[C]");
      Assert.Equal("A", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_Ladder_MiddleBranchMatches()
    {
      var h = Render("(if: false)[A](else-if: true)[B](else:)[C]");
      Assert.Equal("B", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_Ladder_ElseBranchMatches()
    {
      var h = Render("(if: false)[A](else-if: false)[B](else:)[C]");
      Assert.Equal("C", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_Ladder_MultipleElseIfs_OnlyFirstTrueRenders()
    {
      var h = Render("(if: false)[A](else-if: false)[B](else-if: true)[C](else-if: true)[D](else:)[E]");
      Assert.Equal("C", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_UnhyphenatedSpelling_AlsoWorks()
    {
      var h = Render("(if: false)[A](elseif: true)[B]");
      Assert.Equal("B", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_StrayWithNoPrecedingConditional_Errors()
    {
      var h = Render("(else-if: true)[B]");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.DoesNotContain("B", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_AfterInterveningSet_StillChains()
    {
      // A plain (set:) between (if:) and (else-if:) does not break the chain
      // (reference only updates the pairing on hook show/hide), so the
      // (else-if: true) chains off the failing (if:) and renders.
      var h = Render("(if: false)[A](set: $x to 1)(else-if: true)[B]");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Contains("B", h.Buf.Text);
    }

    [Fact]
    public void ElseIf_NonBooleanCondition_Errors()
    {
      var h = Render("(if: false)[A](else-if: 5)[B]");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.DoesNotContain("B", h.Buf.Text);
    }

    [Theory]
    [InlineData("giant", "rumble")]
    [InlineData("big", "growl")]
    [InlineData("small", "gurgle")]
    public void ElseIf_CanonicalDocLadder(string size, string expected)
    {
      // The (else-if:) documentation's own worked example.
      var src =
        "(set: $size to \"" + size + "\")" +
        "(if: $size is \"giant\")[rumble]" +
        "(else-if: $size is \"big\")[growl]" +
        "(else:)[gurgle]";
      var h = Render(src);
      Assert.Equal(expected, h.Buf.Text);
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
      // `(notamacro: $x to 5)` is rejected at parse time: `to`/`into` are
      // only allowed at the top of (set:)/(put:) arg positions, so the parser
      // refuses to build the MacroCallNode. With per-node parser recovery
      // the rejection surfaces as an in-prose Error entry on the render
      // buffer rather than a thrown exception — but the (notamacro:) call is
      // never evaluated, so $x stays untouched (the invariant this test
      // really cares about).
      var h = Render("(notamacro: $x to 5)");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.Null(h.Store.Get("x", isTemporary: false));
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
    public void RandomFractionalBound_TruncatesTowardZero()
    {
      // Fractional bounds are truncated toward zero (reference's parseInt
      // coercion), not rejected: (random: 1.5, 1.9) behaves like (random: 1, 1)
      // → deterministically 1, with no error.
      var h = Render("(random: 1.5, 1.9)");
      Assert.Equal("1", h.Buf.Text);
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void RandomMaxInt32Bound_StaysInRange_NoError()
    {
      // hi == int.MaxValue used to trip a guard (Random.Next(lo, hi + 1) would
      // overflow). The double/long scale now spans the full range, so this
      // returns a value in [0, int.MaxValue] with no error.
      var h = Render("(random: 0, 2147483647)");
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
      Assert.True(long.TryParse(h.Buf.Text, out var n) && n >= 0 && n <= 2147483647,
        $"expected an integer in [0, 2147483647], got '{h.Buf.Text}'");
    }

    [Fact]
    public void RandomHugeBound_EmitsInProseError()
    {
      // 1e12 is finite but well outside Int32 — the (int) cast would have
      // produced garbage. The bound validator catches it (truncation toward
      // zero doesn't bring it back into range).
      var h = Render("(random: 0, 1000000000000)");
      Assert.Contains(h.Buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                       && e.Content.Contains("finite number"));
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

    // Hook-scoped temp variables ---------------------------------------------

    [Fact]
    public void TempVar_FreshDeclarationInHook_DoesNotLeak()
    {
      // Matches reference Harlowe semantics: (set: _y to 2) inside a hook
      // is local to that hook. After the hook, _y is undefined and reading
      // it surfaces an in-prose error rather than the leaked value.
      var h = Render("[(set: _y to 2)](print: _y)");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void TempVar_InnerSetOfOuter_ModifiesOuter()
    {
      // Reference's "inner hooks can modify outer hooks' values" — a (set:)
      // inside a hook of a temp var declared outside writes to the outer
      // declaration. After the hook, the outer var has the new value.
      var h = Render("(set: _x to 1)[(set: _x to 2)](print: _x)");
      Assert.Contains("2", h.Buf.Text);
    }

    [Fact]
    public void TempVar_NestedHooks_LocalDeclarationDiesAtHookExit()
    {
      var h = Render("[(set: _a to 1)[(set: _b to 2)](print: _b)]");
      // _b is local to the inner hook; reading it in the outer hook (after
      // the inner closes) errors.
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void TempVar_ForLoopBody_DoesNotLeakLocalDeclarations()
    {
      // The (for:) body is rendered as a hook per iteration; a fresh
      // declaration inside should die at iteration end and not be visible
      // after the loop.
      var h = Render("(for: each _i, 1, 2, 3)[(set: _double to _i * 2)](print: _double)");
      Assert.Equal(1, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void TempVar_StoryVarUnaffectedByHookScopes()
    {
      // Story-scoped $vars are not subject to hook scoping; (set: $x ...)
      // inside a hook persists after the hook exits.
      var h = Render("[(set: $x to 5)](print: $x)");
      Assert.Contains("5", h.Buf.Text);
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    // `=` as `to` alias ------------------------------------------------------

    [Fact]
    public void Set_EqualsSignAliasForTo()
    {
      // Reference Harlowe shorthand: `(set: $x = 5)` is identical to
      // `(set: $x to 5)`. ts/markup/patterns.ts — the `to` pattern is
      // `either('to'+wb, '=')`.
      var h = Render("(set: $x = 5)(print: $x)");
      Assert.Contains("5", h.Buf.Text);
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void BarePipe_InProse_StaysProse()
    {
      // A `|` that doesn't start a hook name (`|name>`) is ordinary prose.
      var h = Render("a | b");
      Assert.Equal("a | b", h.Buf.Text);
    }

    [Fact]
    public void AttachedBool_OnMacroCall_True_ShowsHook()
    {
      // A macro call (not a variable) returning a Bool with an attached hook —
      // the MacroNode twin of the `$x[hook]` variable path.
      var h = Render("(either: true)[shown]");
      Assert.Equal("shown", h.Buf.Text);
    }

    [Fact]
    public void AttachedBool_OnMacroCall_False_HidesHook_AndPairsWithElse()
    {
      var h = Render("(either: false)[hidden](else:)[else-ran]");
      Assert.Equal("else-ran", h.Buf.Text);
      Assert.Equal(0, CountKind(h.Buf, BufferedRenderOutput.Kind.Error));
    }
  }
}
