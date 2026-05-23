using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  public class StorySessionTests
  {
    // -----------------------------------------------------------------------
    // HTML builder helpers
    // -----------------------------------------------------------------------

    private static Harlowe OnePassage(string body)
      => Story("1", ("1", "P1", body));

    private static Harlowe TwoPassages(string p1, string p2)
      => Story("1", ("1", "P1", p1), ("2", "P2", p2));

    private static Harlowe ThreePassages(string p1, string p2, string p3)
      => Story("1", ("1", "P1", p1), ("2", "P2", p2), ("3", "P3", p3));

    private static Harlowe Story(string startPid, params (string pid, string name, string body)[] passages)
    {
      var sb = new System.Text.StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"");
      sb.Append(startPid);
      sb.Append("\" creator=\"\" creator-version=\"\">");
      for (int i = 0; i < passages.Length; i++)
      {
        sb.Append("<tw-passagedata pid=\"");
        sb.Append(passages[i].pid);
        sb.Append("\" name=\"");
        sb.Append(passages[i].name);
        sb.Append("\" tags=\"\">");
        sb.Append(passages[i].body);
        sb.Append("</tw-passagedata>");
      }
      sb.Append("</tw-storydata></body></html>");
      return new Harlowe(sb.ToString());
    }

    private static int CountKind(RenderResult r, BufferedRenderOutput.Kind k)
    {
      int n = 0;
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == k) n++;
      return n;
    }

    // -----------------------------------------------------------------------
    // Construction and initial state
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_StartsAtStartNodePassage()
    {
      var session = new StorySession(TwoPassages("first", "second"));
      Assert.Equal("P1", session.CurrentPassage);
    }

    [Fact]
    public void Render_ReturnsTextFromCurrentPassage()
    {
      var session = new StorySession(OnePassage("Hello, world."));
      var r = session.Render();
      Assert.Equal("Hello, world.", r.Text);
    }

    [Fact]
    public void Render_PassageNameMatchesCurrentPassage()
    {
      var session = new StorySession(OnePassage("text"));
      var r = session.Render();
      Assert.Equal("P1", r.PassageName);
    }

    [Fact]
    public void Render_EntriesNotNull()
    {
      var session = new StorySession(OnePassage("text"));
      var r = session.Render();
      Assert.NotNull(r.Entries);
    }

    // -----------------------------------------------------------------------
    // Navigation via Goto
    // -----------------------------------------------------------------------

    [Fact]
    public void Goto_UpdatesCurrentPassage()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      Assert.Equal("P2", session.CurrentPassage);
    }

    [Fact]
    public void Goto_ReturnsNewPassageContent()
    {
      var session = new StorySession(TwoPassages("first passage", "second passage"));
      var r = session.Goto("P2");
      Assert.Contains("second passage", r.Text);
    }

    [Fact]
    public void Goto_PassageNameInResultMatchesTarget()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      var r = session.Goto("P2");
      Assert.Equal("P2", r.PassageName);
    }

    [Fact]
    public void Goto_UnknownPassage_ReturnsEmptyResult()
    {
      var session = new StorySession(OnePassage("p1"));
      var r = session.Goto("NoSuchPassage");
      Assert.Equal(string.Empty, r.Text);
      Assert.Empty(r.Entries);
    }

    // -----------------------------------------------------------------------
    // Visit tracking
    // -----------------------------------------------------------------------

    [Fact]
    public void VisitsIdentifier_IsOneAfterFirstEntry()
    {
      var session = new StorySession(OnePassage("(print: visits)"));
      var r = session.Render();
      Assert.Equal("1", r.Text);
    }

    [Fact]
    public void VisitsIdentifier_IncrementsOnRevisit()
    {
      // Navigate away and back — visits for P1 should be 2.
      var session = new StorySession(TwoPassages("(print: visits)", "p2"));
      session.Goto("P2");
      var r = session.Goto("P1");
      Assert.Equal("2", r.Text);
    }

    // -----------------------------------------------------------------------
    // Variable persistence
    // -----------------------------------------------------------------------

    [Fact]
    public void StoryVar_PersistsAcrossPassages()
    {
      // P1 sets $score; P2 reads it.
      var session = new StorySession(TwoPassages("(set: $score to 42)", "$score"));
      session.Render();         // execute P1's (set:)
      var r = session.Goto("P2");
      Assert.Equal("42", r.Text);
    }

    [Fact]
    public void TempVar_ClearedOnNavigation()
    {
      // P1 sets _t; P2 tries to read it — should produce an error.
      var session = new StorySession(TwoPassages("(set: _t to 1)", "_t"));
      session.Render();
      var r = session.Goto("P2");
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
    }

    // -----------------------------------------------------------------------
    // IEvaluationContext identifiers
    // -----------------------------------------------------------------------

    [Fact]
    public void TimeIdentifier_ReturnsNonNegativeNumber()
    {
      var session = new StorySession(OnePassage("(print: time)"));
      var r = session.Render();
      Assert.NotEmpty(r.Text);
      // Text is a stringified non-negative integer (milliseconds).
      Assert.True(double.TryParse(r.Text, out double ms));
      Assert.True(ms >= 0);
    }

    [Fact]
    public void PassageIdentifier_NameFieldMatchesCurrentPassage()
    {
      // passage's name entry is a datamap; (print: passage's name) round-trips
      // through the datamap accessor — deferred to v2. Instead test the
      // IEvaluationContext.Passage property directly via a known identifier
      // in expression position. For now verify via the session's own property.
      var session = new StorySession(OnePassage("text"));
      session.Render();
      var pv = ((IEvaluationContext)session).Passage;
      Assert.Equal(HarloweValueKind.Datamap, pv.Kind);
      Assert.True(pv.AsDatamap.TryGetValue("name", out var name));
      Assert.Equal("P1", name.AsString);
    }

    [Fact]
    public void PassageIdentifier_TagsField_EmptyWhenAttributeAbsent()
    {
      var session = new StorySession(OnePassage("text"));
      session.Render();
      var pv = ((IEvaluationContext)session).Passage;
      Assert.True(pv.AsDatamap.TryGetValue("tags", out var tags));
      Assert.Equal(HarloweValueKind.Array, tags.Kind);
      Assert.Empty(tags.AsArray);
    }

    [Fact]
    public void PassageIdentifier_TagsField_ContainsParsedTags()
    {
      // Build a story where the start passage carries two tags.
      var html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"P1\" tags=\"intro  hidden\">text</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var session = new StorySession(new Harlowe(html));
      session.Render();
      var pv = ((IEvaluationContext)session).Passage;
      Assert.True(pv.AsDatamap.TryGetValue("tags", out var tags));
      Assert.Equal(HarloweValueKind.Array, tags.Kind);
      Assert.Equal(2, tags.AsArray.Count);
      Assert.Equal("intro", tags.AsArray[0].AsString);
      Assert.Equal("hidden", tags.AsArray[1].AsString);
    }

    // -----------------------------------------------------------------------
    // Automatic (goto:) following
    // -----------------------------------------------------------------------

    [Fact]
    public void PendingGoto_AutoFollowedDuringRender()
    {
      // P1 has (goto: "P2"); render should land on P2.
      var session = new StorySession(TwoPassages("(goto: \"P2\")", "arrived"));
      var r = session.Render();
      Assert.Equal("P2", session.CurrentPassage);
      Assert.Contains("arrived", r.Text);
    }

    [Fact]
    public void PendingGoto_AutoFollowedDuringGoto()
    {
      // P2 immediately redirects to P3.
      var session = new StorySession(ThreePassages("p1", "(goto: \"P3\")", "final"));
      var r = session.Goto("P2");
      Assert.Equal("P3", session.CurrentPassage);
      Assert.Contains("final", r.Text);
    }

    [Fact]
    public void PendingGoto_CircularChain_EmitsError()
    {
      // P1 → P2 → P1 → … should not stack-overflow; emits an error entry.
      var session = new StorySession(TwoPassages("(goto: \"P2\")", "(goto: \"P1\")"));
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
    }

    // -----------------------------------------------------------------------
    // (display:) integration
    // -----------------------------------------------------------------------

    [Fact]
    public void Display_InlinesOtherPassageText()
    {
      var session = new StorySession(TwoPassages("(display: \"P2\")", "inlined content"));
      var r = session.Render();
      Assert.Contains("inlined content", r.Text);
    }

    [Fact]
    public void Display_UnknownPassage_ProducesError()
    {
      var session = new StorySession(OnePassage("(display: \"NoSuchPassage\")"));
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void Display_PropagatesLinksFromDisplayedPassage()
    {
      // Regression: previously (display:) flattened the inlined passage to its
      // .Text and dropped structured entries. A [[Next]] inside the displayed
      // passage must surface as a Link entry on the parent render result.
      var session = new StorySession(TwoPassages("before (display: \"P2\") after", "[[Next->P1]]"));
      var r = session.Render();
      bool foundLink = false;
      for (int i = 0; i < r.Entries.Count; i++)
      {
        var e = r.Entries[i];
        if (e.Kind == BufferedRenderOutput.Kind.Link && e.Target == "P1" && e.Content == "Next")
        { foundLink = true; break; }
      }
      Assert.True(foundLink, "expected Link entry from the displayed passage to reach the parent output");
    }

    [Fact]
    public void Display_PropagatesErrorsFromDisplayedPassage()
    {
      // A runtime error inside the displayed passage must surface as an Error
      // entry on the parent output, not be swallowed by the buffer flattening.
      var session = new StorySession(TwoPassages("(display: \"P2\")", "$missing"));
      var r = session.Render();
      Assert.True(CountKind(r, BufferedRenderOutput.Kind.Error) >= 1);
    }

    [Fact]
    public void Display_SelfReference_EmitsErrorInsteadOfStackOverflow()
    {
      // A passage displaying itself must terminate with an in-prose error
      // rather than recursing until the process stack overflows.
      var session = new StorySession(OnePassage("(display: \"P1\")"));
      var r = session.Render();
      Assert.True(CountKind(r, BufferedRenderOutput.Kind.Error) >= 1);
    }

    [Fact]
    public void Display_TwoPassageCycle_EmitsErrorInsteadOfStackOverflow()
    {
      // Two passages displaying each other still hit the same depth ceiling —
      // the guard counts nested invocations regardless of which passage they
      // target.
      var session = new StorySession(TwoPassages("(display: \"P2\")", "(display: \"P1\")"));
      var r = session.Render();
      Assert.True(CountKind(r, BufferedRenderOutput.Kind.Error) >= 1);
    }

    [Fact]
    public void Display_MaxDepth_IsConfigurable()
    {
      // Authors building modular UIs out of nested (display:) calls must be
      // able to raise/lower the ceiling. With a chain P1 → P2 → P3 and the
      // ceiling at 1, the second-level (display:) inside P2 trips the limit
      // even though P3 itself contains no further nesting.
      var story = ThreePassages("(display: \"P2\")", "(display: \"P3\")", "ok");
      var session = new StorySession(story) { MaxDisplayDepth = 1 };
      var r = session.Render();
      Assert.True(CountKind(r, BufferedRenderOutput.Kind.Error) >= 1);
    }

    [Fact]
    public void Display_DeepLegitimateChain_SucceedsWhenCeilingRaised()
    {
      // With the same chain but ceiling raised, the displays go through.
      var story = ThreePassages("(display: \"P2\")", "(display: \"P3\")", "ok");
      var session = new StorySession(story) { MaxDisplayDepth = 10 };
      var r = session.Render();
      Assert.Equal(0, CountKind(r, BufferedRenderOutput.Kind.Error));
      Assert.Contains("ok", r.Text);
    }

    // -----------------------------------------------------------------------
    // Undo
    // -----------------------------------------------------------------------

    [Fact]
    public void Undo_ReturnsFalseWithNoHistory()
    {
      var session = new StorySession(OnePassage("text"));
      Assert.False(session.Undo());
    }

    [Fact]
    public void Undo_ReturnsTrueAfterGoto()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      Assert.True(session.Undo());
    }

    [Fact]
    public void Undo_RestoresCurrentPassage()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      session.Undo();
      Assert.Equal("P1", session.CurrentPassage);
    }

    [Fact]
    public void Undo_RestoredPassage_RendersCorrectly()
    {
      var session = new StorySession(TwoPassages("original text", "other"));
      session.Goto("P2");
      session.Undo();
      var r = session.Render();
      Assert.Contains("original text", r.Text);
    }

    [Fact]
    public void Undo_RestoresStoryVariable()
    {
      // P2 sets $x to 99; after undo, P1 re-renders and $x should be unset.
      var session = new StorySession(TwoPassages("$x", "(set: $x to 99)"));
      session.Goto("P2");  // sets $x = 99 in store
      session.Undo();
      // After undo, the store is restored to pre-goto state ($x unset).
      // Render P1 which references $x — should produce an error (unset).
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void Undo_RestoresVisitCount()
    {
      // After going to P2 and undoing, P2's visit count should be gone.
      // Verify by navigating to P2 again — visits should be 1, not 2.
      var session = new StorySession(TwoPassages("p1", "(print: visits)"));
      session.Goto("P2");
      session.Undo();
      var r = session.Goto("P2");
      Assert.Equal("1", r.Text);
    }

    [Fact]
    public void Undo_SecondCall_ReturnsFalse()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      session.Undo();
      Assert.False(session.Undo());
    }

    [Fact]
    public void Goto_AfterUndo_NewSnapshotReplacesPrior()
    {
      // Undo goes back to P1 (popping the prior snapshot); then Goto to P3
      // pushes a fresh snapshot. Undo from P3 should go back to P1, not P2.
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Undo();               // back to P1, stack now empty
      session.Goto("P3");           // pushes P1
      session.Undo();
      Assert.Equal("P1", session.CurrentPassage);
    }

    // -----------------------------------------------------------------------
    // Multi-step undo (slice B)
    // -----------------------------------------------------------------------

    [Fact]
    public void Undo_WalksBackThroughMultipleGotos()
    {
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Goto("P3");
      Assert.True(session.Undo());
      Assert.Equal("P2", session.CurrentPassage);
      Assert.True(session.Undo());
      Assert.Equal("P1", session.CurrentPassage);
    }

    [Fact]
    public void Undo_AllStepsThenStackEmpty()
    {
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Goto("P3");
      session.Undo();
      session.Undo();
      Assert.False(session.Undo());
      Assert.Equal("P1", session.CurrentPassage);
    }

    [Fact]
    public void Undo_RestoresStoryVariablesAcrossMultipleSteps()
    {
      // P1 reads $x; P2 sets $x=1; P3 sets $x=2. Step back to recover prior
      // values: undo from P3 → $x=1; undo again → $x unset.
      var html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"P1\" tags=\"\">$x</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"P2\" tags=\"\">(set: $x to 1)$x</tw-passagedata>"
        + "<tw-passagedata pid=\"3\" name=\"P3\" tags=\"\">(set: $x to 2)$x</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var session = new StorySession(new Harlowe(html));
      session.Render();             // P1 — error (unset), but we don't care
      session.Goto("P2");           // $x = 1
      session.Goto("P3");           // $x = 2

      session.Undo();               // pop snapshot taken before entering P3 — $x = 1
      var r1 = session.Render();
      Assert.Equal("1", r1.Text);

      session.Undo();               // pop snapshot taken before entering P2 — $x unset
      var r2 = session.Render();
      Assert.Equal(1, CountKind(r2, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void Undo_RestoresVisitCountsAcrossMultipleSteps()
    {
      // Visit P2, P3, P2 — visits[P2]=2. Undo three times, then revisit P2 fresh.
      var session = new StorySession(ThreePassages("p1", "(print: visits)", "p3"));
      session.Goto("P2");           // visits[P2] = 1
      session.Goto("P3");
      session.Goto("P2");           // visits[P2] = 2

      session.Undo();               // back to P3; visits[P2] = 1
      session.Undo();               // back to P2; visits[P2] = 1
      session.Undo();               // back to P1; visits cleared for P2

      var r = session.Goto("P2");
      Assert.Equal("1", r.Text);
    }

    [Fact]
    public void Undo_InterleavedWithGoto()
    {
      // Goto P2, Goto P3, Undo (back to P2), Goto P3 again, Undo (back to P2),
      // Undo (back to P1). Verifies stack push/pop semantics under mixed use.
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Goto("P3");
      session.Undo();
      Assert.Equal("P2", session.CurrentPassage);
      session.Goto("P3");
      session.Undo();
      Assert.Equal("P2", session.CurrentPassage);
      session.Undo();
      Assert.Equal("P1", session.CurrentPassage);
      Assert.False(session.Undo());
    }

    // -----------------------------------------------------------------------
    // (history:) (slice C)
    // -----------------------------------------------------------------------

    [Fact]
    public void History_EmptyOnInitialRender()
    {
      var session = new StorySession(OnePassage("text"));
      session.Render();
      var h = ((IEvaluationContext)session).History;
      Assert.Equal(HarloweValueKind.Array, h.Kind);
      Assert.Empty(h.AsArray);
    }

    [Fact]
    public void History_AfterGoto_ContainsPriorPassage()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      var h = ((IEvaluationContext)session).History;
      Assert.Single(h.AsArray);
      Assert.Equal("P1", h.AsArray[0].AsString);
    }

    [Fact]
    public void History_ExcludesCurrentPassage()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      var h = ((IEvaluationContext)session).History;
      for (int i = 0; i < h.AsArray.Count; i++)
        Assert.NotEqual("P2", h.AsArray[i].AsString);
    }

    [Fact]
    public void History_OldestFirstAcrossMultipleGotos()
    {
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Goto("P3");
      var h = ((IEvaluationContext)session).History;
      Assert.Equal(2, h.AsArray.Count);
      Assert.Equal("P1", h.AsArray[0].AsString);
      Assert.Equal("P2", h.AsArray[1].AsString);
    }

    [Fact]
    public void History_AllowsDuplicatesOnRevisit()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      session.Goto("P1");
      session.Goto("P2");
      var h = ((IEvaluationContext)session).History;
      Assert.Equal(3, h.AsArray.Count);
      Assert.Equal("P1", h.AsArray[0].AsString);
      Assert.Equal("P2", h.AsArray[1].AsString);
      Assert.Equal("P1", h.AsArray[2].AsString);
    }

    [Fact]
    public void History_ShrinksOnUndo()
    {
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Goto("P3");
      session.Undo();  // back to P2; history should shrink to ["P1"]
      var h = ((IEvaluationContext)session).History;
      Assert.Single(h.AsArray);
      Assert.Equal("P1", h.AsArray[0].AsString);
    }

    [Fact]
    public void History_MacroRendersAsCommaJoinedList()
    {
      // (print: (history:)) round-trips through the array's ToHarloweString,
      // which joins with commas. End-to-end check that the macro is wired.
      var session = new StorySession(ThreePassages("p1", "p2", "(print: (history:))"));
      session.Goto("P2");
      var r = session.Goto("P3");
      Assert.Equal("P1,P2", r.Text);
    }

    // -----------------------------------------------------------------------
    // Integration test against real test fixture
    // -----------------------------------------------------------------------

    [Fact]
    public void Integration_Disclaimer_To_FirstPassage_Playthrough()
    {
      var story = TestFixture.LoadTestFile();
      var session = new StorySession(story);

      // Initial render — should be at Disclaimer.
      Assert.Equal("1", story.StartNode);
      var r1 = session.Render();
      Assert.Equal("Disclaimer", session.CurrentPassage);
      Assert.False(string.IsNullOrWhiteSpace(r1.Text));

      // Navigate to FirstPassage.
      var r2 = session.Goto("FirstPassage");
      Assert.Equal("FirstPassage", session.CurrentPassage);
      Assert.False(string.IsNullOrWhiteSpace(r2.Text));

      // Links to the three affirmative responses should be present.
      bool foundYes = false, foundAgree = false, foundNod = false;
      for (int i = 0; i < r2.Entries.Count; i++)
      {
        var e = r2.Entries[i];
        if (e.Kind != BufferedRenderOutput.Kind.Link) continue;
        if (e.Target == "FirstPassage - Yes") foundYes = true;
        if (e.Target == "FirstPassage - Agree") foundAgree = true;
        if (e.Target == "FirstPassage - Nod") foundNod = true;
      }
      Assert.True(foundYes);
      Assert.True(foundAgree);
      Assert.True(foundNod);

      // Undo returns to Disclaimer.
      Assert.True(session.Undo());
      Assert.Equal("Disclaimer", session.CurrentPassage);
    }

    // -----------------------------------------------------------------------
    // Live render state hygiene (H1) — every top-level render resets the live
    // tree so a failed render can't leave DispatchEvent operating on the old
    // passage's tree under the new passage name.
    // -----------------------------------------------------------------------

    [Fact]
    public void GotoMissingPassage_ClearsLiveStateSoDispatchIsNoOp()
    {
      // Set up a session with a clickable region in the first passage.
      var story = TwoPassages(
        "|m>[cake](click: ?m)[surprise]",
        "second");
      var session = new StorySession(story);
      var first = session.Render();

      // Capture the region id from the first render.
      string regionId = null;
      for (int i = 0; i < first.Entries.Count; i++)
        if (first.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive)
          regionId = first.Entries[i].Region?.Id;
      Assert.NotNull(regionId);

      // Goto a passage that doesn't exist. The session moves to that name
      // but no tree is produced for it.
      var failed = session.Goto("DoesNotExist");
      Assert.Equal("DoesNotExist", session.CurrentPassage);
      Assert.Equal(string.Empty, failed.Text);

      // DispatchEvent must NOT mutate the previous passage's tree (which it
      // would, pre-fix, because _liveRoot still pointed at it). The result
      // should be empty / inert.
      var dispatched = session.DispatchEvent(regionId);
      Assert.Equal(string.Empty, dispatched.Text);
      Assert.Empty(dispatched.Entries);
    }

    [Fact]
    public void RenderWithMissingPassage_ClearsLiveStateFromPriorRender()
    {
      // Same scenario but via direct EnterPassage state: render the first
      // passage, then construct a fresh session pointed at a missing start.
      var story = OnePassage("hello (click: ?passage)[wow]");
      var session = new StorySession(story);
      var initial = session.Render();
      Assert.NotEmpty(initial.Entries);

      // Now manually point at a missing passage and render again.
      session.Goto("Phantom");
      var blank = session.Render();
      Assert.Equal(string.Empty, blank.Text);

      // Dispatch (any id) is inert because there is no live tree.
      var inert = session.DispatchEvent("r-0");
      Assert.Equal(string.Empty, inert.Text);
    }
  }
}
