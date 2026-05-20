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

    // --- Dispatch revisions ---

    [Fact]
    public void Dispatch_Click_ReplacesTarget()
    {
      var session = Session("|m>[cake](click: ?m)[surprise]");
      var initial = session.Render();
      Assert.Equal("cake", initial.Text);

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("surprise", after.Text);
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
    public void Dispatch_AcrossMultipleMatches_RewritesAll()
    {
      var session = Session("|m>[a]|m>[b](click: ?m)[X]");
      var initial = session.Render();
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("XX", after.Text);
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
      Assert.Equal("surprise", second.Text);
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
    public void Click_TargetNotYetRendered_IsNoOp()
    {
      // ?cake is declared after the macro — no wrap registered.
      var session = Session("(click: ?cake)[surprise]|cake>[late]");
      var result = session.Render();
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
      Assert.Equal("late", result.Text);
    }

    [Fact]
    public void Click_Passage_WrapsWholeRender()
    {
      // ?passage selects the render root; (click: ?passage) makes the entire
      // passage one clickable region.
      var session = Session("hello(click: ?passage)[wow]");
      var initial = session.Render();
      Assert.Equal(1, CountKind(initial, BufferedRenderOutput.Kind.BeginInteractive));

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("wow", after.Text);
    }

    // --- Composition + enchant interaction ---

    [Fact]
    public void Click_ComposedWithStyle_StyleWrapsTheRegion()
    {
      var session = Session("|m>[x]"
        + "(click: ?m) + (text-style: \"bold\")[after]");
      var result = session.Render();

      // Expected order: PushStyle(bold), BeginInteractive, Text(x), EndInteractive, PopStyle.
      Assert.Collection(result.Entries,
        e => Assert.Equal(BufferedRenderOutput.Kind.PushStyle, e.Kind),
        e => Assert.Equal(BufferedRenderOutput.Kind.BeginInteractive, e.Kind),
        e => { Assert.Equal(BufferedRenderOutput.Kind.Text, e.Kind); Assert.Equal("x", e.Content); },
        e => Assert.Equal(BufferedRenderOutput.Kind.EndInteractive, e.Kind),
        e => Assert.Equal(BufferedRenderOutput.Kind.PopStyle, e.Kind));
    }

    [Fact]
    public void Enchant_AcrossDispatch_StaysSingleWrapped()
    {
      // (enchant:) re-runs after dispatch. Its idempotency (disenchant first)
      // means no double-wrapping after a click rewrites the target.
      var session = Session(
        "|x>[content](enchant: ?x, (text-style: \"bold\"))(click: ?x)[after]");
      var initial = session.Render();
      Assert.Equal(1, CountKind(initial, BufferedRenderOutput.Kind.PushStyle));

      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("after", after.Text);
      // Still just one bold wrap — the enchant pass disenchanted before
      // re-applying, so the post-dispatch tree isn't doubled.
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
    }

    [Fact]
    public void Enchant_CatchesClickSplicedContent()
    {
      // The enchantment is registered before any click happens. After the
      // click rewrites ?x, the enchant pass catches the new content.
      var session = Session(
        "(enchant: ?x, (text-style: \"bold\"))|x>[old](click: ?x)[NEW]");
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

    // --- Combo coverage ---

    [Theory]
    [InlineData("click", "cake", "surprise", "surprise")]
    [InlineData("click-replace", "cake", "surprise", "surprise")]
    [InlineData("click-append", "cake", " more", "cake more")]
    [InlineData("click-prepend", "fox", "the ", "the fox")]
    [InlineData("mouseover", "cake", "surprise", "surprise")]
    [InlineData("mouseover-replace", "cake", "surprise", "surprise")]
    [InlineData("mouseover-append", "cake", " more", "cake more")]
    [InlineData("mouseover-prepend", "fox", "the ", "the fox")]
    [InlineData("mouseout", "cake", "surprise", "surprise")]
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
  }
}
