using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the click/hover macros (<c>(click:)</c>, <c>(click-append:)</c>,
  /// <c>(mouseover-prepend:)</c>, …) and the dispatch loop on
  /// <see cref="StorySession"/>. The harness uses a real session because
  /// dispatch needs the session to keep the live tree + handlers alive
  /// between calls.
  /// </summary>
  public class InteractionMacroTests
  {
    private static Harlowe OnePassage(string body)
    {
      var sb = new System.Text.StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"1\" creator=\"\" creator-version=\"\">");
      sb.Append("<tw-passagedata pid=\"1\" name=\"P1\" tags=\"\">");
      sb.Append(body);
      sb.Append("</tw-passagedata></tw-storydata></body></html>");
      return new Harlowe(sb.ToString());
    }

    private static StorySession Session(string body) => new StorySession(OnePassage(body));

    /// <summary>The first BeginInteractive region's id, or null if none.</summary>
    private static string FirstRegionId(RenderResult result)
    {
      for (int i = 0; i < result.Entries.Count; i++)
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive)
          return result.Entries[i].Region?.Id;
      return null;
    }

    /// <summary>Count entries of a given kind — replaces LINQ Count() under the no-LINQ rule.</summary>
    private static int CountKind(RenderResult r, BufferedRenderOutput.Kind k)
    {
      int n = 0;
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == k) n++;
      return n;
    }

    private static List<InteractiveRegion> Regions(RenderResult r)
    {
      var list = new List<InteractiveRegion>();
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive)
          list.Add(r.Entries[i].Region);
      return list;
    }

    // --- Region emission ---

    [Fact]
    public void Click_WrapsTargetInInteractiveRegion()
    {
      var session = Session("|m>[cake](click: ?m)[surprise]");
      var result = session.Render();

      var regions = Regions(result);
      var region = Assert.Single(regions);
      Assert.Equal(InteractionKind.Click, region.Kind);
      Assert.NotNull(region.Id);
      Assert.Equal("cake", result.Text);
    }

    [Fact]
    public void Click_ComposedWithFalseConditional_RegistersNothing()
    {
      // (if: false) + (click: ?m) — the disabled descriptor must suppress the
      // whole application: no interactive region, no handler armed.
      var session = Session("|m>[cake](if: false) + (click: ?m)[surprise]");
      var result = session.Render();
      Assert.Empty(Regions(result));
      Assert.Equal("cake", result.Text);

      var enabled = Session("|m>[cake](if: true) + (click: ?m)[surprise]");
      Assert.Single(Regions(enabled.Render()));
    }

    [Fact]
    public void Enchant_ComposedWithFalseConditional_AppliesNoStyle()
    {
      // The (change:)/(enchant:) path routes through the same descriptor, so a
      // disabled conditional yields no style layers.
      var session = Session("|m>[cake](enchant: ?m, (if: false) + (text-style: \"bold\"))");
      var result = session.Render();
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.PushStyle));
      Assert.Equal("cake", result.Text);
    }

    [Fact]
    public void Click_BracketBracketsTheRightContent()
    {
      var session = Session("before |m>[cake] after(click: ?m)[x]");
      var result = session.Render();

      // Find indices of BeginInteractive / EndInteractive.
      int begin = -1, end = -1;
      for (int i = 0; i < result.Entries.Count; i++)
      {
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive) begin = i;
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.EndInteractive) end = i;
      }
      Assert.True(begin >= 0 && end > begin);

      // The text node between them holds the target's content.
      var inner = result.Entries[begin + 1];
      Assert.Equal(BufferedRenderOutput.Kind.Text, inner.Kind);
      Assert.Equal("cake", inner.Content);
    }

    [Theory]
    [InlineData("mouseover", InteractionKind.MouseOver)]
    [InlineData("mouseout", InteractionKind.MouseOut)]
    [InlineData("mouseover-append", InteractionKind.MouseOver)]
    [InlineData("mouseout-prepend", InteractionKind.MouseOut)]
    public void HoverMacros_EmitTheirOwnInteractionKind(string macro, InteractionKind kind)
    {
      var session = Session("|m>[c](" + macro + ": ?m)[x]");
      var result = session.Render();
      var region = Assert.Single(Regions(result));
      Assert.Equal(kind, region.Kind);
    }

    [Fact]
    public void Click_MultipleMatches_ShareOneRegionId()
    {
      // Re-resolution at dispatch time hits every current match, so all wraps
      // produced by one (click:) call share one id and one handler.
      var session = Session("|m>[a]|m>[b](click: ?m)[X]");
      var result = session.Render();

      var regions = Regions(result);
      Assert.Equal(2, regions.Count);
      Assert.Equal(regions[0].Id, regions[1].Id);
    }

    // --- Dispatch: plain macros reveal at the macro's position (reference:
    // `[cool]<foo|(click:?foo)[beans]` → click → "coolbeans"), combos splice
    // into the target. ---

    [Fact]
    public void Dispatch_Click_RevealsAttachedHookAtMacroPosition()
    {
      // The target keeps its content and just loses the armed region; the
      // attached hook renders where the (click:) call sits.
      var session = Session("|m>[cake](click: ?m)[surprise]");
      var initial = session.Render();
      Assert.Equal("cake", initial.Text);

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("cakesurprise", after.Text);
    }

    [Fact]
    public void Dispatch_ClickAppend_AddsAfterExistingContent()
    {
      var session = Session("|m>[cake](click-append: ?m)[ and pie]");
      var initial = session.Render();
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("cake and pie", after.Text);
    }

    [Fact]
    public void Dispatch_ClickPrepend_AddsBeforeExistingContent()
    {
      var session = Session("|m>[fox](click-prepend: ?m)[the ]");
      var initial = session.Render();
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("the fox", after.Text);
    }

    [Fact]
    public void Dispatch_HoverAppend_RunsForMouseOverRegion()
    {
      var session = Session("|m>[a](mouseover-append: ?m)[b]");
      var initial = session.Render();
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("ab", after.Text);
    }

    [Fact]
    public void Dispatch_AcrossMultipleMatches_RevealsOnceAndDisenchantsAll()
    {
      // Both matches are armed under one region id; firing it reveals the
      // hook once (at the macro's position) and disenchants every match.
      var session = Session("|m>[a]|m>[b](click: ?m)[X]");
      var initial = session.Render();
      Assert.Equal(2, Regions(initial).Count);

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("abX", after.Text);
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_Click_IsSingleUse()
    {
      var session = Session("|m>[cake](click: ?m)[surprise]");
      var initial = session.Render();
      var id = FirstRegionId(initial);
      session.DispatchEvent(id);

      // Second dispatch of the same id is a no-op — the handler was consumed.
      var second = session.DispatchEvent(id);
      Assert.Equal("cakesurprise", second.Text);
      Assert.Equal(0, CountKind(second, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_ConsumesInteractiveWrap_NoStaleRegions()
    {
      // After a click-append dispatch, the wrap is gone (so further clicks
      // can't re-fire) even though the target's original content is preserved.
      var session = Session("|m>[a](click-append: ?m)[b]");
      var initial = session.Render();
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_UnknownRegionId_ReturnsCurrentViewUnchanged()
    {
      var session = Session("|m>[cake](click: ?m)[surprise]");
      var initial = session.Render();

      var after = session.DispatchEvent("nonsense");
      Assert.Equal(initial.Text, after.Text);
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_BeforeAnyRender_ReturnsEmpty()
    {
      var session = new StorySession(OnePassage("hi"));
      // The session enters the start passage in its constructor but doesn't
      // render until asked. Dispatch before Render has nothing to dispatch on.
      var result = session.DispatchEvent("anything");
      Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void Dispatch_AfterUndo_BeforeRender_IsNoOp()
    {
      // Sequence: render P1 (registers a (click:) handler), Goto P2, Undo
      // back to P1, then DispatchEvent the original P1 region id before the
      // next Render. The live tree from before the Goto is no longer current,
      // so dispatch must not fire the stale handler against it — it returns
      // an empty result, and the consumer is expected to call Render to
      // rebuild the tree (which will re-register handlers from passage source).
      var sb = new System.Text.StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"1\" creator=\"\" creator-version=\"\">");
      sb.Append("<tw-passagedata pid=\"1\" name=\"P1\" tags=\"\">|m>[cake](click: ?m)[surprise]</tw-passagedata>");
      sb.Append("<tw-passagedata pid=\"2\" name=\"P2\" tags=\"\">plain</tw-passagedata>");
      sb.Append("</tw-storydata></body></html>");
      var session = new StorySession(new Harlowe(sb.ToString()));

      var p1 = session.Render();
      var regionId = FirstRegionId(p1);
      Assert.NotNull(regionId);

      session.Goto("P2");
      Assert.True(session.Undo());

      var afterUndo = session.DispatchEvent(regionId);
      Assert.Equal(string.Empty, afterUndo.Text);
      Assert.Equal(0, CountKind(afterUndo, BufferedRenderOutput.Kind.BeginInteractive));

      // Render again — handlers re-register from passage source, and dispatch
      // now works against the freshly-built tree.
      var rerendered = session.Render();
      var freshId = FirstRegionId(rerendered);
      Assert.NotNull(freshId);
      var fired = session.DispatchEvent(freshId);
      Assert.Contains("surprise", fired.Text);
    }

    [Fact]
    public void Click_TargetDeclaredLater_IsClickable()
    {
      // ?cake is declared AFTER the macro. The persistent interaction pass
      // re-resolves the target against the finished tree, so the forward
      // reference is caught (eager apply-time resolution used to miss it) and
      // dispatching the region reveals "surprise" at the macro's position —
      // before the |cake> hook.
      var session = Session("(click: ?cake)[surprise]|cake>[late]");
      var result = session.Render();
      Assert.Equal(1, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
      Assert.Equal("late", result.Text);

      var after = session.DispatchEvent(FirstRegionId(result));
      Assert.Equal("surpriselate", after.Text);
    }

    [Fact]
    public void MouseOver_TargetDeclaredLater_IsHoverable()
    {
      // The forward-reference fix applies to the whole interaction family.
      var session = Session("(mouseover: ?late)[hi]|late>[t]");
      var result = session.Render();
      var region = Assert.Single(Regions(result));
      Assert.Equal(InteractionKind.MouseOver, region.Kind);

      var after = session.DispatchEvent(FirstRegionId(result));
      Assert.Equal("hit", after.Text);
    }

    [Fact]
    public void Click_ComposedStyle_ForwardRef_ArmsUnstyled_RevealsStyled()
    {
      // Composed style + a forward-referenced target: the armed region is
      // unstyled (composed styles belong to the revealed content, reference's
      // renderInto of the descriptor), and the reveal carries the style.
      var session = Session("(click: ?late) + (text-style: \"bold\")[after]|late>[t]");
      var result = session.Render();
      Assert.Collection(result.Entries,
        e => Assert.Equal(BufferedRenderOutput.Kind.BeginInteractive, e.Kind),
        e => { Assert.Equal(BufferedRenderOutput.Kind.Text, e.Kind); Assert.Equal("t", e.Content); },
        e => Assert.Equal(BufferedRenderOutput.Kind.EndInteractive, e.Kind));

      var after = session.DispatchEvent(FirstRegionId(result));
      Assert.Equal("aftert", after.Text);
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
    }

    [Fact]
    public void Dispatch_ClickChain_NestedClickInDeferredHook_Fires()
    {
      // A (click:) inside a click-deferred hook must itself become clickable
      // after the first dispatch — the deferred render records the interaction,
      // and the pass re-resolves its now-spliced-in target. Previously the
      // chain broke after the first dispatch.
      var session = Session("|a>[start](click: ?a)[done |b>[bee](click: ?b)[final]]");
      var first = session.Render();
      Assert.Equal("start", first.Text);

      var second = session.DispatchEvent(FirstRegionId(first));
      Assert.Contains("bee", second.Text);
      Assert.Equal(1, CountKind(second, BufferedRenderOutput.Kind.BeginInteractive));

      var third = session.DispatchEvent(FirstRegionId(second));
      Assert.Contains("final", third.Text);
    }

    [Fact]
    public void Dispatch_OneRegion_LeavesOtherSingleWrapped()
    {
      // Firing one of two interactions strips all wraps and re-wraps only the
      // survivor — exactly one region, single-wrapped (the pass is idempotent,
      // no doubling).
      var session = Session("|a>[A](click: ?a)[x]|b>[B](click: ?b)[y]");
      var first = session.Render();
      Assert.Equal(2, CountKind(first, BufferedRenderOutput.Kind.BeginInteractive));

      var after = session.DispatchEvent(FirstRegionId(first));
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Click_Passage_WrapsWholeRender()
    {
      // ?passage selects the render root; (click: ?passage) makes the entire
      // passage one clickable region. Firing it reveals the hook in place —
      // the passage text survives.
      var session = Session("hello(click: ?passage)[wow]");
      var initial = session.Render();
      Assert.Equal(1, CountKind(initial, BufferedRenderOutput.Kind.BeginInteractive));

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("hellowow", after.Text);
    }

    // --- Composition + enchant interaction ---

    [Fact]
    public void Click_ComposedWithStyle_StylesRevealedContent_NotArmedRegion()
    {
      // Reference applies the descriptor's composed styles when the event's
      // renderInto runs — so (click: ?m)+(text-style:"bold")[after] arms ?m
      // unstyled and reveals a bold "after". (The armed region is styled by
      // the macro's optional second argument instead.)
      var session = Session("|m>[x]"
        + "(click: ?m) + (text-style: \"bold\")[after]");
      var result = session.Render();

      Assert.Collection(result.Entries,
        e => Assert.Equal(BufferedRenderOutput.Kind.BeginInteractive, e.Kind),
        e => { Assert.Equal(BufferedRenderOutput.Kind.Text, e.Kind); Assert.Equal("x", e.Content); },
        e => Assert.Equal(BufferedRenderOutput.Kind.EndInteractive, e.Kind));

      var after = session.DispatchEvent(FirstRegionId(result));
      Assert.Equal("xafter", after.Text);
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
    }

    [Fact]
    public void ClickAppendComposedWithStyle_StylesTheSplicedContent()
    {
      // For a combo, the composed style layer travels with the deferred
      // content into the target: the armed region is unstyled, and after
      // dispatch the appended " added" carries the bold wrap while the
      // original content stays plain.
      var session = Session("|m>[orig](click-append: ?m) + (text-style: \"bold\")[ added]");
      var initial = session.Render();
      Assert.Equal(0, CountKind(initial, BufferedRenderOutput.Kind.PushStyle));

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("orig added", after.Text);
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PopStyle));
    }

    [Fact]
    public void Enchant_AcrossDispatch_StaysSingleWrapped()
    {
      // (enchant:) re-runs after dispatch. Its idempotency (disenchant first)
      // means no double-wrapping after a click mutates the tree.
      var session = Session(
        "|x>[content](enchant: ?x, (text-style: \"bold\"))(click: ?x)[after]");
      var initial = session.Render();
      Assert.Equal(1, CountKind(initial, BufferedRenderOutput.Kind.PushStyle));

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("contentafter", after.Text);
      // Still just one bold wrap — the enchant pass disenchanted before
      // re-applying, so the post-dispatch tree isn't doubled.
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
    }

    [Fact]
    public void Enchant_CatchesClickSplicedContent()
    {
      // The enchantment is registered before any click happens. After the
      // combo click rewrites ?x, the enchant pass catches the new content.
      var session = Session(
        "(enchant: ?x, (text-style: \"bold\"))|x>[old](click-replace: ?x)[NEW]");
      var initial = session.Render();
      Assert.Equal("old", initial.Text);

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("NEW", after.Text);
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
    }

    // --- Errors ---

    [Fact]
    public void Click_NonHookNameTarget_EmitsError()
    {
      var session = Session("(click: 5)[x]");
      var result = session.Render();
      bool found = false;
      for (int i = 0; i < result.Entries.Count; i++)
      {
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && result.Entries[i].Content.Contains("hook name"))
          found = true;
      }
      Assert.True(found, "expected an error entry mentioning 'hook name'");
    }

    // --- Whole-family coverage: plain macros reveal at the macro's position
    // (target keeps its content), combos splice into the target. ---

    [Theory]
    [InlineData("click", "cake", "surprise", "cakesurprise")]
    [InlineData("click-rerun", "cake", "surprise", "cakesurprise")]
    [InlineData("click-replace", "cake", "surprise", "surprise")]
    [InlineData("click-append", "cake", " more", "cake more")]
    [InlineData("click-prepend", "fox", "the ", "the fox")]
    [InlineData("mouseover", "cake", "surprise", "cakesurprise")]
    [InlineData("mouseover-replace", "cake", "surprise", "surprise")]
    [InlineData("mouseover-append", "cake", " more", "cake more")]
    [InlineData("mouseover-prepend", "fox", "the ", "the fox")]
    [InlineData("mouseout", "cake", "surprise", "cakesurprise")]
    [InlineData("mouseout-replace", "cake", "surprise", "surprise")]
    [InlineData("mouseout-append", "cake", " more", "cake more")]
    [InlineData("mouseout-prepend", "fox", "the ", "the fox")]
    public void EveryComboMacro_DispatchesToTheExpectedRevision(string macro, string initial, string deferred, string expected)
    {
      var session = Session("|m>[" + initial + "](" + macro + ": ?m)[" + deferred + "]");
      var first = session.Render();
      var after = session.DispatchEvent(FirstRegionId(first));
      Assert.Equal(expected, after.Text);
    }

    // --- String targets: (click: "text") arms every occurrence, reference's
    // `wow(click:'wow')[ gosh ]wow` spec. ---

    [Fact]
    public void ClickString_ArmsEveryOccurrence_UnderOneRegionId()
    {
      var session = Session("wow(click: \"wow\")[ gosh ]wow");
      var result = session.Render();
      Assert.Equal("wowwow", result.Text);

      var regions = Regions(result);
      Assert.Equal(2, regions.Count);
      Assert.Equal(regions[0].Id, regions[1].Id);
      Assert.Equal(InteractionKind.Click, regions[0].Kind);
    }

    [Fact]
    public void Dispatch_ClickString_RevealsAtMacroPosition_AndDisenchantsAll()
    {
      // Reference: `wow(click:'wow')[ gosh ]wow` → click → "wow gosh wow",
      // zero enchantments left.
      var session = Session("wow(click: \"wow\")[ gosh ]wow");
      var initial = session.Render();

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("wow gosh wow", after.Text);
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_ClickReplaceString_SplicesEveryOccurrence()
    {
      // The combo form rewrites the matched text itself.
      var session = Session("bob(click-replace: \"b\")[x]");
      var initial = session.Render();
      Assert.Equal("bob", initial.Text);
      Assert.Equal(2, Regions(initial).Count);

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("xox", after.Text);
    }

    [Fact]
    public void ClickString_MatchAddedLater_ArmsOnNextPass()
    {
      // The string interaction matches nothing on the first render (no handler
      // registered), then arms the "wow" a later click reveals — reference's
      // "enchants additional matching strings added to the passage".
      var session = Session("(click: \"wow\")[gosh]|a>[A](click: ?a)[wow]");
      var initial = session.Render();
      Assert.Equal("A", initial.Text);
      Assert.Equal(1, CountKind(initial, BufferedRenderOutput.Kind.BeginInteractive));

      var second = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("Awow", second.Text);
      Assert.Equal(1, CountKind(second, BufferedRenderOutput.Kind.BeginInteractive));

      var third = session.DispatchEvent(FirstRegionId(second));
      Assert.Equal("goshAwow", third.Text);
    }

    [Fact]
    public void ClickEmptyString_EmitsError()
    {
      // Reference: "A string given to this (click:) macro was empty."
      var session = Session("(click: \"\")[x]");
      var result = session.Render();
      bool found = false;
      for (int i = 0; i < result.Entries.Count; i++)
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && result.Entries[i].Content.Contains("was empty"))
          found = true;
      Assert.True(found, "expected an error entry mentioning 'was empty'");
    }

    // --- Optional second argument: a changer (or via-lambda) styling the
    // armed region, reference's `(click:?foo, (size: 2*24px))` spec. ---

    [Fact]
    public void Click_SecondArgChanger_StylesTheArmedRegion()
    {
      var session = Session("|m>[x](click: ?m, (text-style: \"bold\"))[y]");
      var result = session.Render();

      Assert.Collection(result.Entries,
        e => { Assert.Equal(BufferedRenderOutput.Kind.PushStyle, e.Kind); Assert.True(e.Style.Bold); },
        e => Assert.Equal(BufferedRenderOutput.Kind.BeginInteractive, e.Kind),
        e => { Assert.Equal(BufferedRenderOutput.Kind.Text, e.Kind); Assert.Equal("x", e.Content); },
        e => Assert.Equal(BufferedRenderOutput.Kind.EndInteractive, e.Kind),
        e => Assert.Equal(BufferedRenderOutput.Kind.PopStyle, e.Kind));

      // The armed styling disenchants with the region when it fires.
      var after = session.DispatchEvent(FirstRegionId(result));
      Assert.Equal("xy", after.Text);
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
    }

    [Fact]
    public void Click_SecondArgViaLambda_StylesPerMatch_WithPos()
    {
      // Reference: `(click:?foo+?bar, via (size: pos*48px))[]` styles each
      // match by its 1-based position.
      var session = Session("|m>[a]|m>[b](click: ?m, via (opacity: pos * 0.25))[y]");
      var result = session.Render();

      var styles = new List<StyleSpec>();
      for (int i = 0; i < result.Entries.Count; i++)
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.PushStyle)
          styles.Add(result.Entries[i].Style);
      Assert.Equal(2, styles.Count);
      Assert.Equal(0.25, styles[0].Opacity);
      Assert.Equal(0.5, styles[1].Opacity);
    }

    [Fact]
    public void Click_SecondArgRevisionChanger_EmitsError()
    {
      // The same notRevisionChanger gate (enchant:) uses.
      var session = Session("|m>[x](click: ?m, (replace: ?z))[y]");
      var result = session.Render();
      bool found = false;
      for (int i = 0; i < result.Entries.Count; i++)
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && result.Entries[i].Content.Contains("can't include a revision"))
          found = true;
      Assert.True(found, "expected an error entry mentioning 'can't include a revision'");
    }

    [Fact]
    public void Click_SecondArgNonChanger_EmitsError()
    {
      var session = Session("|m>[x](click: ?m, \"bold\")[y]");
      var result = session.Render();
      bool found = false;
      for (int i = 0; i < result.Entries.Count; i++)
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && result.Entries[i].Content.Contains("changer or a 'via' lambda"))
          found = true;
      Assert.True(found, "expected an error entry mentioning the second-argument types");
    }

    [Fact]
    public void Click_SecondArgLambdaNonChangerResult_ReplacesMatchWithError()
    {
      // A lambda producing a non-changer replaces the match with the in-prose
      // error and arms nothing (reference's enchantScope failure path).
      var session = Session("|m>[x](click: ?m, via 5)[y]");
      var result = session.Render();
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
      bool found = false;
      for (int i = 0; i < result.Entries.Count; i++)
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && result.Entries[i].Content.Contains("must return a changer"))
          found = true;
      Assert.True(found, "expected an error entry mentioning 'must return a changer'");
    }

    // --- (click-rerun:) — once:false, re-renders per activation ---

    [Fact]
    public void ClickRerun_StaysArmed_AndReRendersEachActivation()
    {
      // Reference: `(click-rerun:?foo)[(set:$b to it+1)$b]` keeps the target
      // armed and replaces the revealed content on every activation.
      var session = Session("(set: $n to 0)|m>[cool](click-rerun: ?m)[(set: $n to $n + 1)$n]");
      var initial = session.Render();
      Assert.Equal("cool", initial.Text);
      var id = FirstRegionId(initial);

      var first = session.DispatchEvent(id);
      Assert.Equal("cool1", first.Text);
      Assert.Equal(1, CountKind(first, BufferedRenderOutput.Kind.BeginInteractive));

      var second = session.DispatchEvent(id);
      Assert.Equal("cool2", second.Text);
      Assert.Equal(1, CountKind(second, BufferedRenderOutput.Kind.BeginInteractive));
    }

    // --- Reveal anchors resolve by tag, not node reference — so a (click:)
    // whose anchor reached the live tree as a clone still reveals. ---

    [Fact]
    public void Dispatch_ClickInsideReplaceHook_RevealsInsideSplicedContent()
    {
      // The (click:) runs inside (replace:)'s detached render; its anchor
      // enters the live tree as a clone via the revision splice. The dispatch
      // finds it by its RevealRegionId tag (clones inherit it) — a held node
      // reference would point at the discarded detached original and the
      // reveal would vanish.
      var session = Session("|t>[old]|a>[A](replace: ?t)[(click: ?a)[X]]");
      var initial = session.Render();
      Assert.Equal("A", initial.Text);
      var id = FirstRegionId(initial);
      Assert.NotNull(id);

      var after = session.DispatchEvent(id);
      Assert.Equal("XA", after.Text);
    }

    [Fact]
    public void Dispatch_ClickInsideMultiTargetReplace_RevealsInEveryClone()
    {
      // Two ?t matches → the revision splices two clones of the deferred
      // content, each carrying an anchor clone with the same tag. The reveal
      // fills every tagged anchor — reference's per-target render.
      var session = Session("|t>[x]|t>[y](replace: ?t)[(click: ?a)[X]]|a>[A]");
      var initial = session.Render();
      Assert.Equal("A", initial.Text);

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("XXA", after.Text);
    }

    [Fact]
    public void Dispatch_ComboString_ViaLambdaFailure_SplicesSurvivingOccurrences()
    {
      // The failed via-lambda replaced the first occurrence wrap with an error
      // node; tag-based resolution simply doesn't find it at dispatch, and the
      // surviving occurrence still splices.
      var session = Session("bob(click-replace: \"b\", via 5)[x]");
      var initial = session.Render();
      Assert.Equal("ob", initial.Text);
      var id = FirstRegionId(initial);
      Assert.NotNull(id);

      var after = session.DispatchEvent(id);
      Assert.Equal("ox", after.Text);
    }

    // --- Empty hooks are never armed (reference's :empty filter) ---

    [Fact]
    public void Click_EmptyHookTarget_IsNotArmed()
    {
      var session = Session("|m>[](click: ?m)[x]");
      var result = session.Render();
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
    }

    // --- ?link styling target (a leaf RenderLinkNode, not a hook) ---
    // (enchant:)/(change:) on ?link work via Changer.ApplyToNode; click/replace on
    // ?link need the container model (BeginLink/EndLink follow-up) and stay no-ops.

    [Fact]
    public void Enchant_Link_StylesTheLink()
    {
      var session = Session("(enchant: ?link, (text-style: \"bold\"))[[Go->P2]]");
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.PushStyle)); // was 0 (silent no-op)
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Link));
    }

    [Fact]
    public void Change_Link_StylesTheLink()
    {
      // (change:) is one-shot, so the link must already be in the tree.
      var session = Session("[[Go->P2]](change: ?link, (text-style: \"bold\"))");
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.PushStyle));
    }
  }
}
