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
    public void Goto_UnknownPassage_IsRefused_AndLeavesTheSessionUntouched()
    {
      // Goto to a passage that doesn't exist used to strand the session on that
      // name — which then went into the timeline, into the save, and made the
      // blob permanently unloadable (LoadGame validates every passage). It is now
      // refused outright: the current view comes back with an error appended and
      // no session state moves.
      var session = new StorySession(OnePassage("p1"));
      session.Render();

      var r = session.Goto("NoSuchPassage");

      Assert.True(CountKind(r, BufferedRenderOutput.Kind.Error) >= 1);
      Assert.Contains("p1", r.Text);                    // still showing the current passage
      Assert.Equal("P1", session.CurrentPassage);       // navigation did not happen
      Assert.Equal(1, session.Turns.AsNumber);          // no turn was recorded
    }

    [Fact]
    public void SaveGame_RefusesATimelineWhosePassageWasRemoved()
    {
      // Goto validates its destination, but the departure passage is baked into
      // the Moment by FinalizePresent — and the editing API can remove it out
      // from under a live session. Writing that timeline would produce a blob
      // LoadGame rejects forever ("saved passage ... no longer exists"), so the
      // save must refuse instead of poisoning the slot.
      var story = TwoPassages("p1", "p2");
      var session = new StorySession(story);
      session.Render();
      Assert.True(story.RemovePassage("P1"));
      session.Goto("P2");

      Assert.False(session.SaveGame("slot"));
      Assert.Contains("'P1'", session.LastSaveError);
    }

    [Fact]
    public void DanglingStartNode_FirstGotoStartsTheTimeline_AndSavesLoad()
    {
      // A hand-built story that never sets StartNode keeps the "0" default while
      // synthesized pids start at 1 — so the session opens with no current
      // passage. The first Goto must not finalise that nameless present into the
      // past (a Moment with passage "" makes every later save unloadable).
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage { Name = "P1", Body = "hello" });
      var session = new StorySession(story);
      session.Render();

      var r = session.Goto("P1");
      Assert.Contains("hello", r.Text);
      Assert.Equal(1, session.Turns.AsNumber);          // the first real turn, not the second

      Assert.True(session.SaveGame("slot"), session.LastSaveError);
      Assert.True(session.LoadGame("slot"), session.LastLoadError);
    }

    // -----------------------------------------------------------------------
    // Non-ASCII digit loader robustness — a non-ASCII decimal digit (which
    // char.IsDigit accepts) used where a number is expected must not throw a
    // FormatException out of double.Parse and abort the whole story load.
    // -----------------------------------------------------------------------

    [Fact]
    public void NonAsciiDigit_InExpression_DoesNotCrashLoad_RendersInProseError()
    {
      // ٥ is Arabic-Indic five. Constructing the session parses the body;
      // pre-fix this threw and never returned. Now it loads and the passage
      // surfaces an in-prose error instead.
      var session = new StorySession(OnePassage("(print: ٥)"));
      var r = session.Render();
      Assert.True(CountKind(r, BufferedRenderOutput.Kind.Error) >= 1);
    }

    [Fact]
    public void NonAsciiDigit_InBodyProse_RendersAsText()
    {
      var session = new StorySession(OnePassage("You have ٥ coins"));
      var r = session.Render();
      Assert.Contains("٥", r.Text);
    }

    [Fact]
    public void UnterminatedLink_FollowingMacrosStillExecute()
    {
      // A single missing `]]` must not disable the rest of the passage. The
      // (set:) and (print:) after the unterminated `[[` should run, so the
      // rendered text shows the assigned value. Pre-fix, everything after `[[`
      // became one phantom-link Text token and no macro executed.
      var session = new StorySession(OnePassage("[[Shop\n(set: $g to 7)(print: $g)"));
      var r = session.Render();
      Assert.Contains("7", r.Text);
    }

    [Fact]
    public void Random_SameSeed_ReproducibleAcrossSessions()
    {
      // A seeded session must produce a reproducible RNG sequence. Pre-fix the
      // session created a fresh tick-seeded Random per render leg, so even with
      // a seed the output wasn't controllable.
      var story = OnePassage("(print: (random: 1, 1000000000))");
      var a = new StorySession(story, 12345).Render().Text;
      var b = new StorySession(story, 12345).Render().Text;
      Assert.Equal(a, b);
    }

    [Fact]
    public void Random_AcrossGotoChain_DrawsFromOneContinuousStream()
    {
      // The (random:) in the goto'd passage continues the same stream as the
      // first passage's draw, so the two values differ. Pre-fix each leg got a
      // fresh Random; on a tick-seeded runtime both legs in the same tick drew
      // the identical first value, making $a equal the second draw.
      var story = TwoPassages(
        "(set: $a to (random: 1, 1000000000))(goto: \"P2\")",
        "(print: $a)/(print: (random: 1, 1000000000))");
      var text = new StorySession(story, 12345).Render().Text;
      var parts = text.Split('/');
      Assert.Equal(2, parts.Length);
      Assert.NotEqual(parts[0], parts[1]);
    }

    [Fact]
    public void NonAsciiDigit_InOnePassage_DoesNotBreakSiblingPassages()
    {
      // The headline symptom was a whole-load abort: one bad passage took down
      // the entire story. A sibling passage must remain fully usable.
      var session = new StorySession(TwoPassages("(set: $x to ５)", "intact"));
      session.Render();                 // P1 — renders an error, must not throw
      var r = session.Goto("P2");
      Assert.Contains("intact", r.Text);
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
    public void TurnsIdentifier_StartsAtOneOnFirstRender()
    {
      // Reference: turns is 1-indexed and counts the current passage. After
      // the first render, turns == 1.
      var session = new StorySession(OnePassage("(print: turns)"));
      var r = session.Render();
      Assert.Equal("1", r.Text);
    }

    [Fact]
    public void TurnsIdentifier_IncrementsOnGoto()
    {
      var session = new StorySession(TwoPassages("(print: turns)", "(print: turns)"));
      var first = session.Render();
      Assert.Equal("1", first.Text);
      var second = session.Goto("P2");
      Assert.Equal("2", second.Text);
    }

    [Fact]
    public void TurnIdentifier_SynonymOfTurns()
    {
      // Reference: `turns` and `turn` are aliases.
      var session = new StorySession(OnePassage("(print: turn)"));
      var r = session.Render();
      Assert.Equal("1", r.Text);
    }

    [Fact]
    public void TurnsIdentifier_NoStartPassage_IsZero()
    {
      // With no start passage configured, _currentPassage stays empty after
      // construction and turns reports 0. (When a start passage exists,
      // EnterPassage runs in the constructor — see the "starts at one"
      // test above.)
      var session = new StorySession(new Harlowe());
      var v = ((IEvaluationContext)session).Turns;
      Assert.Equal(0, v.AsNumber);
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

    [Fact]
    public void PendingGoto_MacroToMissingPassage_EmitsErrorAndStaysPut()
    {
      // (goto:) to a passage that doesn't exist surfaces an in-prose error
      // rather than silently navigating to a blank result. The session stays
      // on the current passage and the rest of the passage keeps rendering.
      var session = new StorySession(OnePassage("before (goto: \"Ghost\") after"));
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
      Assert.Equal("P1", session.CurrentPassage);
      Assert.Contains("before", r.Text);
      Assert.Contains("after", r.Text);
    }

    [Fact]
    public void PendingGoto_MacroToMissingPassage_ErrorNamesTheTarget()
    {
      var session = new StorySession(OnePassage("(goto: \"Ghost\")"));
      var r = session.Render();
      string errText = null;
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.Error) errText = r.Entries[i].Content;
      Assert.NotNull(errText);
      Assert.Contains("Ghost", errText);
    }

    [Fact]
    public void PendingGoto_MacroToExistingPassage_StillNavigates()
    {
      // Regression guard: validation must not break the happy path.
      var session = new StorySession(TwoPassages("(goto: \"P2\")", "arrived"));
      var r = session.Render();
      Assert.Equal(0, CountKind(r, BufferedRenderOutput.Kind.Error));
      Assert.Equal("P2", session.CurrentPassage);
      Assert.Contains("arrived", r.Text);
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

    [Fact]
    public void Display_MaxDepthSetToZero_ClampsToOneSoSingleDisplayStillSucceeds()
    {
      // The docstring promises "Values < 1 are clamped to 1 (a single
      // (display:) call always succeeds)." A previous version had no clamping
      // so MaxDisplayDepth=0 caused the very first (display:) to error out.
      var story = ThreePassages("(display: \"P2\")", "ok", "unused");
      var session = new StorySession(story) { MaxDisplayDepth = 0 };
      var r = session.Render();
      Assert.Equal(0, CountKind(r, BufferedRenderOutput.Kind.Error));
      Assert.Contains("ok", r.Text);
      Assert.Equal(1, session.MaxDisplayDepth);
    }

    [Fact]
    public void Display_MaxDepthSetNegative_ClampsToOne()
    {
      var session = new StorySession(OnePassage("hi")) { MaxDisplayDepth = -5 };
      Assert.Equal(1, session.MaxDisplayDepth);
    }

    [Fact]
    public void Display_MaxDepthSetHuge_ClampsToAbsoluteCeiling()
    {
      // High values previously passed through; a consumer setting
      // int.MaxValue thinking "no limit" would let recursive (display:) blow
      // the .NET stack with StackOverflowException — uncatchable and fatal
      // for the host process. The setter now caps at a safe absolute ceiling
      // so the in-prose error path still fires before the real stack does.
      var session = new StorySession(OnePassage("hi")) { MaxDisplayDepth = int.MaxValue };
      Assert.True(session.MaxDisplayDepth <= 1024, "absolute ceiling should bound the configured depth");
      Assert.True(session.MaxDisplayDepth >= 20, "ceiling should still leave generous headroom over the default");
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
    public void Undo_RestoresVarFromEarlierTurn_AcrossUntouchedTurns()
    {
      // $x is set in P2 and never touched in P3/P4. Undoing back through P4 and
      // P3 must still see $x = 7 — reconstruction flattens P2's delta even
      // though the intervening turns changed nothing (the delta-compression
      // path that a full per-turn snapshot got for free).
      var session = new StorySession(Story("1",
        ("1", "P1", "$x"),
        ("2", "P2", "(set: $x to 7)$x"),
        ("3", "P3", "$x"),
        ("4", "P4", "$x")));
      session.Render();
      session.Goto("P2");           // $x = 7
      session.Goto("P3");           // $x untouched
      session.Goto("P4");           // $x untouched

      session.Undo();               // back to P3
      Assert.Equal("7", session.Render().Text);
      session.Undo();               // back to P2
      Assert.Equal("7", session.Render().Text);
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
    // Redo
    // -----------------------------------------------------------------------

    [Fact]
    public void Redo_ReturnsFalseWithNothingUndone()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      Assert.False(session.Redo());
    }

    [Fact]
    public void Redo_AfterUndo_RestoresPassage()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      session.Undo();
      Assert.Equal("P1", session.CurrentPassage);
      Assert.True(session.Redo());
      Assert.Equal("P2", session.CurrentPassage);
    }

    [Fact]
    public void Redo_RestoresStoryVariable()
    {
      // P2 sets and prints $x; after undo it's unset, redo brings it back.
      var session = new StorySession(TwoPassages("$x", "(set: $x to 99)$x"));
      Assert.Equal("99", session.Goto("P2").Text);
      session.Undo();
      session.Redo();
      Assert.Equal("99", session.Render().Text);
    }

    [Fact]
    public void Redo_RestoresVisitCount()
    {
      var session = new StorySession(TwoPassages("p1", "(print: visits)"));
      Assert.Equal("1", session.Goto("P2").Text);
      session.Undo();
      session.Redo();
      Assert.Equal("1", session.Render().Text);
    }

    [Fact]
    public void UndoRedo_RandomDraw_ReproducesOnReplay()
    {
      // The `random` data name draws from the session RNG stream, so the
      // Moment's recorded RNG state makes an undone turn's replay re-draw the
      // exact same card.
      var session = new StorySession(TwoPassages(
        "(set: $deck to (a: 10, 20, 30, 40, 50))",
        "(move: $deck's random into $card)(print: $card)"));
      session.Render();
      string drawn = session.Goto("P2").Text;
      session.Undo();
      session.Redo();
      Assert.Equal(drawn, session.Render().Text);
    }

    [Fact]
    public void UndoRedo_MovePatternTurn_RestoresDeckAndCards()
    {
      // P2 draws two cards with a pattern-destination (move:). Undo restores
      // the full deck; the replay re-draws both exactly once.
      var session = new StorySession(ThreePassages(
        "(set: $deck to (a: 5, 6, 7))(print: $deck's length)",
        "(move: $deck into (a: $c1, $c2))(print: $c1 + $c2)-(print: $deck's length)",
        "(print: $deck's length)"));
      Assert.Equal("3", session.Render().Text);
      Assert.Equal("11-1", session.Goto("P2").Text);
      Assert.Equal("1", session.Goto("P3").Text);

      session.Undo();
      Assert.Equal("11-1", session.Render().Text);
      session.Undo();
      Assert.Equal("3", session.Render().Text);
      session.Redo();
      Assert.Equal("11-1", session.Render().Text);
    }

    [Fact]
    public void UndoRedo_MoveTurn_RestoresSourceAndDest()
    {
      // P2 draws a card off the deck with (move:). Undo restores the turn's
      // start (full deck, no card); replaying it re-draws the same card once.
      var session = new StorySession(ThreePassages(
        "(set: $deck to (a: 5, 6, 7))(print: $deck's length)",
        "(move: $deck's 1st into $card)(print: $card)-(print: $deck's length)",
        "(print: $deck's length)"));
      Assert.Equal("3", session.Render().Text);
      Assert.Equal("5-2", session.Goto("P2").Text);
      Assert.Equal("2", session.Goto("P3").Text);

      session.Undo();
      Assert.Equal("5-2", session.Render().Text);   // P2 replayed from the full deck
      session.Undo();
      Assert.Equal("3", session.Render().Text);
      session.Redo();
      Assert.Equal("5-2", session.Render().Text);
    }

    [Fact]
    public void UndoRedo_PropertyMutation_RestoresCollectionState()
    {
      // P2 mutates one datamap slot (property assignment, a relative `it - 1`).
      // The write routes through the store's Set, so the per-turn delta model
      // sees it: each undo restores the turn's start state and the replay
      // re-applies the decrement exactly once.
      var session = new StorySession(ThreePassages(
        "(set: $dm to (dm: \"hp\", 10))(print: $dm's hp)",
        "(set: $dm's hp to it - 1)(print: $dm's hp)",
        "(print: $dm's hp)"));
      Assert.Equal("10", session.Render().Text);
      Assert.Equal("9", session.Goto("P2").Text);
      Assert.Equal("9", session.Goto("P3").Text);

      session.Undo();
      Assert.Equal("9", session.Render().Text);   // P2 replayed from hp=10
      session.Undo();
      Assert.Equal("10", session.Render().Text);  // P1 replayed from scratch
      session.Redo();
      Assert.Equal("9", session.Render().Text);
    }

    [Fact]
    public void Goto_AfterUndo_ClearsRedoFuture()
    {
      // Undo populates the future; a forward Goto abandons it, so redo is gone.
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Undo();          // future = [P2]
      session.Goto("P3");      // clears future
      Assert.False(session.Redo());
      Assert.Equal("P3", session.CurrentPassage);
    }

    [Fact]
    public void UndoRedo_RoundTripsMultipleSteps()
    {
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Goto("P3");
      session.Undo();
      session.Undo();
      Assert.Equal("P1", session.CurrentPassage);
      Assert.True(session.Redo());
      Assert.Equal("P2", session.CurrentPassage);
      Assert.True(session.Redo());
      Assert.Equal("P3", session.CurrentPassage);
      Assert.False(session.Redo());
    }

    [Fact]
    public void Turns_DropsOnUndo_RestoredByRedo()
    {
      // Turns counts _past + present, excluding the redo future.
      var session = new StorySession(ThreePassages("(print: turns)", "(print: turns)", "p3"));
      Assert.Equal("1", session.Render().Text);
      Assert.Equal("2", session.Goto("P2").Text);
      session.Undo();
      Assert.Equal("1", session.Render().Text);   // future excluded
      session.Redo();
      Assert.Equal("2", session.Render().Text);
    }

    [Fact]
    public void RewindAndFastForward_AreUndoRedoAliases()
    {
      var session = new StorySession(TwoPassages("p1", "p2"));
      session.Goto("P2");
      Assert.True(session.Rewind());
      Assert.Equal("P1", session.CurrentPassage);
      Assert.True(session.FastForward());
      Assert.Equal("P2", session.CurrentPassage);
    }

    [Fact]
    public void UndoRedo_AcrossVarSettingTurn_ReadInLaterPassage_PreservesVar()
    {
      // Regression (code review): re-finalising a restored Moment used to read the
      // cleared dirty-set and clobber its real delta with {}. So consecutive
      // undo/redo across a turn that set a variable read in a *different* passage
      // lost it. P2 sets $x; P3 only prints it.
      var session = new StorySession(ThreePassages("p1", "(set: $x to 9)", "$x"));
      session.Goto("P2");   // $x = 9
      session.Goto("P3");
      session.Undo();       // -> P2
      session.Undo();       // -> P1 (must not clobber P2's {x:9})
      session.Redo();       // -> P2
      session.Redo();       // -> P3
      Assert.Equal("9", session.Render().Text);
    }

    // -----------------------------------------------------------------------
    // RNG state across undo/redo (slice 2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Undo_IntoRandomPassage_ReproducesTheRoll()
    {
      // The RNG is restored to the start of P2, so re-rendering re-rolls the same
      // value instead of drawing further along the advanced stream.
      var story = ThreePassages("p1", "(print: (random: 1, 1000000))", "p3");
      var session = new StorySession(story, 42);
      var roll = session.Goto("P2").Text;
      session.Goto("P3");
      session.Undo();
      Assert.Equal(roll, session.Render().Text);
    }

    [Fact]
    public void Redo_IntoRandomPassage_ReproducesTheRoll()
    {
      var story = ThreePassages("p1", "(print: (random: 1, 1000000))", "p3");
      var session = new StorySession(story, 7);
      var roll = session.Goto("P2").Text;
      session.Goto("P3");
      session.Undo();   // P2
      session.Undo();   // P1
      session.Redo();   // P2
      Assert.Equal(roll, session.Render().Text);
    }

    [Fact]
    public void UndoRedo_AcrossTwoRandomTurns_EachTurnReproducesOwnRoll()
    {
      // Distinct ranges so the two turns can't coincide — proves the RNG restores
      // to each turn's *own* start, not merely some earlier position.
      var story = ThreePassages("p1", "(print: (random: 1, 5))", "(print: (random: 100, 200))");
      var session = new StorySession(story, 99);
      var r2 = session.Goto("P2").Text;
      var r3 = session.Goto("P3").Text;
      session.Undo();
      Assert.Equal(r2, session.Render().Text);
      session.Redo();
      Assert.Equal(r3, session.Render().Text);
    }

    [Fact]
    public void Goto_AfterUndoWithoutRender_NewTurnRngStaysConsistent()
    {
      // After an undo with no intervening re-render, the live RNG sits at the
      // restored turn's start. A forward Goto must still start the new turn from
      // the prior turn's *end* (derived from the timeline), or the new turn's draw
      // won't survive a later undo+render.
      var story = Story("1",
        ("1", "P1", "p1"),
        ("2", "P2", "(print: (random: 1, 1000000))"),
        ("3", "P3", "p3"),
        ("4", "P4", "(print: (random: 1, 1000000))"));
      var session = new StorySession(story, 42);
      session.Goto("P2");
      session.Goto("P3");
      session.Undo();                          // -> P2, no render
      var firstP4 = session.Goto("P4").Text;   // new turn draws
      session.Goto("P3");                       // move forward so P4 can be undone to
      session.Undo();                           // -> P4
      Assert.Equal(firstP4, session.Render().Text);
    }

    [Fact]
    public void Undo_IntoRedirectTurn_RestingPassageRandomReproduces()
    {
      // P2 draws then redirects to P3, which also draws; P3's draw originally
      // happens at the later stream position. On undo the whole turn should replay
      // from P2 so P3 re-rolls the same value.
      var story = ThreePassages("p1",
        "(set: $j to (random: 1, 1000000))(goto: \"P3\")",
        "(print: (random: 1, 1000000))");
      var session = new StorySession(story, 42);
      var roll = session.Goto("P2").Text;   // lands on P3, prints its roll
      session.Goto("P1");
      session.Undo();                       // -> the P2->P3 turn (resting P3)
      Assert.Equal(roll, session.Render().Text);
    }

    // -----------------------------------------------------------------------
    // Redirect trail: derived visits / (history:) (slice 2c)
    // -----------------------------------------------------------------------

    [Fact]
    public void History_IncludesRedirectDepartures()
    {
      // P2 auto-(goto:)s to P3, so the P2->P3 turn's trail puts P2 (the redirect
      // departure) into history — not just the resting passage P3.
      var session = new StorySession(ThreePassages("start", "(goto: \"P3\")", "end"));
      session.Goto("P2");
      Assert.Equal("P3", session.CurrentPassage);
      var h = ((IEvaluationContext)session).History.AsArray;
      Assert.Equal(2, h.Count);
      Assert.Equal("P1", h[0].AsString);
      Assert.Equal("P2", h[1].AsString);
    }

    [Fact]
    public void Visits_RedirectTarget_CountsAcrossRevisits()
    {
      // P3 is reached only via P2's redirect; its visit count still accumulates.
      var session = new StorySession(ThreePassages("start", "(goto: \"P3\")", "(print: visits)"));
      Assert.Equal("1", session.Goto("P2").Text);   // 1st arrival at P3 (via redirect)
      session.Goto("P1");
      Assert.Equal("2", session.Goto("P2").Text);   // 2nd arrival at P3
    }

    [Fact]
    public void History_RedirectTrail_ShrinksOnUndo()
    {
      var session = new StorySession(ThreePassages("start", "(goto: \"P3\")", "end"));
      session.Goto("P2");        // present = P2->P3 turn; history = [P1, P2]
      session.Undo();            // back to the P1 turn; history = []
      Assert.Empty(((IEvaluationContext)session).History.AsArray);
    }

    [Fact]
    public void UndoIntoRedirectTurn_VisitsGatedRedirect_ReplaysIdentically()
    {
      // The replay of a restored redirect turn seeds the trail with the entry
      // passage, so `visits` mid-replay reads 1 exactly as it did live and the
      // gated redirect re-fires. (It once read 0 — the restored Moment's
      // PassageName is the *resting* P3 — so the replay rested on P2 instead.)
      var session = new StorySession(ThreePassages(
        "start",
        "(if: visits is 1)[(goto: \"P3\")]stayed",
        "end"));
      session.Goto("P2");                    // 1st visit of P2 -> redirects
      Assert.Equal("P3", session.CurrentPassage);
      session.Goto("P1");
      session.Undo();                        // -> the P2->P3 turn
      var r = session.Render();              // replay from the entry passage
      Assert.Equal("P3", session.CurrentPassage);   // redirect re-fired
      Assert.Equal("end", r.Text);
    }

    [Fact]
    public void SecondRender_PreservesRedirectTrail()
    {
      // A plain re-render must not wipe the present turn's redirect trail — the
      // resting passage doesn't re-fire the entry's redirect, so nulling the
      // trail would silently change visits/(history:) and later saves.
      var session = new StorySession(ThreePassages("(goto: \"P2\")", "end", "unused"));
      session.Render();                      // start turn redirects P1 -> P2
      var h1 = ((IEvaluationContext)session).History.AsArray;
      Assert.Single(h1);
      Assert.Equal("P1", h1[0].AsString);

      session.Render();                      // documented state-preserving re-render
      var h2 = ((IEvaluationContext)session).History.AsArray;
      Assert.Single(h2);                     // trail intact, not wiped
      Assert.Equal("P1", h2[0].AsString);
    }

    [Fact]
    public void GotoLoop_DepthCeiling_ErrorsAndRestsAtTurnEntry()
    {
      // P2 and P3 (goto:) each other forever. The abort must roll the turn back
      // to its entry — each hop commits to the redirect trail before the next
      // frame's ceiling check, so without the rollback the session strands
      // wherever the loop was cut.
      var session = new StorySession(ThreePassages("start", "(goto: \"P3\")", "(goto: \"P2\")"));
      var r = session.Goto("P2");
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
      Assert.Equal("P2", session.CurrentPassage);
      Assert.Equal("P2", r.PassageName);
    }

    [Fact]
    public void GotoLoop_DepthCeiling_VisitsAndHistoryUnpolluted()
    {
      // The aborted turn contributes only its entry to the derived counts:
      // visits of P2 reads 1 and history is [P1] — no hop survives the abort.
      var session = new StorySession(ThreePassages("start", "(goto: \"P3\")", "(goto: \"P2\")"));
      session.Goto("P2");
      Assert.Equal(1, ((IEvaluationContext)session).Visits.AsNumber);
      var h = ((IEvaluationContext)session).History.AsArray;
      Assert.Single(h);
      Assert.Equal("P1", h[0].AsString);
    }

    [Fact]
    public void GotoLoop_DepthCeiling_SaveRoundTrips()
    {
      // The blob records a clean single-passage turn at the entry (the polluted
      // trail used to be persisted verbatim); loading it reproduces the abort.
      var session = new StorySession(ThreePassages("start", "(goto: \"P3\")", "(goto: \"P2\")"));
      session.Goto("P2");
      Assert.True(session.SaveGame("slot"));
      Assert.True(session.LoadGame("slot"));
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
      Assert.Equal("P2", session.CurrentPassage);
    }

    [Fact]
    public void GotoLoop_DepthCeiling_SelfLoopOnFirstRender_RestsAtStart()
    {
      // A self-redirecting start passage aborts on the very first Render();
      // the turn's entry snapshot (not a prior Goto) is what the rollback uses.
      var session = new StorySession(OnePassage("(goto: \"P1\")"));
      var r = session.Render();
      Assert.Equal(1, CountKind(r, BufferedRenderOutput.Kind.Error));
      Assert.Equal("P1", session.CurrentPassage);
      Assert.Equal(1, ((IEvaluationContext)session).Visits.AsNumber);
    }

    // -----------------------------------------------------------------------
    // Store restored to turn-start, symmetric with the RNG (review fixes)
    // -----------------------------------------------------------------------

    [Fact]
    public void Undo_IntoRelativeSetPassage_ReappliesOnce_NoDoubling()
    {
      // P2 accumulates; on undo the store is restored to the turn's START so the
      // re-render re-applies the set exactly once (was doubling — store restored
      // to the turn's end, then the set re-ran on top → "12").
      var session = new StorySession(ThreePassages("(set: $x to 10)", "(set: $x to $x + 1)$x", "p3"));
      session.Render();                            // P1: $x = 10
      Assert.Equal("11", session.Goto("P2").Text);
      session.Goto("P3");
      session.Undo();                              // -> P2
      Assert.Equal("11", session.Render().Text);   // not "12"
    }

    [Fact]
    public void UndoThenContinue_DoesNotInflateAccumulatingVar()
    {
      // The natural undo flow: undo a choice, re-render, then go forward. The
      // accumulating var must not carry an inflated value into the new branch.
      var session = new StorySession(ThreePassages("(set: $x to 10)", "(set: $x to $x + 1)", "$x"));
      session.Render();              // P1: $x = 10
      session.Goto("P2");            // $x = 11
      session.Goto("P3");            // (must be past P2 so Undo lands on it)
      session.Undo();                // -> P2 (store at turn start = 10)
      session.Render();              // re-applies once: $x = 11
      Assert.Equal("11", session.Goto("P3").Text);   // continue forward; $x stays 11, not 12
    }

    [Fact]
    public void UndoWithoutRenderThenGoto_CarriesTurnsMutationForward()
    {
      // Undo into a var-setting turn, then navigate forward WITHOUT the documented
      // re-render: the new turn still sees the turn's mutation (Goto rebuilds the
      // live store to the previous turn's end), staying symmetric with the RNG.
      var session = new StorySession(ThreePassages("(set: $x to 10)", "(set: $x to $x + 1)", "$x"));
      session.Render();              // P1: $x = 10
      session.Goto("P2");            // $x = 11
      session.Goto("P3");
      session.Undo();                // -> P2, no render
      Assert.Equal("11", session.Goto("P3").Text);   // forward w/o rendering P2; $x = 11 carried
    }

    [Fact]
    public void CurrentPassage_AfterUndoIntoRedirectTurn_IsRestingNotEntry()
    {
      // Between Undo() and the replay Render(), CurrentPassage reports the turn's
      // resting passage (P3), not the entry it will replay from (P2).
      var session = new StorySession(ThreePassages("start", "(goto: \"P3\")", "end"));
      session.Goto("P2");            // redirects to P3
      session.Goto("P1");
      session.Undo();                // -> the P2->P3 turn, no render yet
      Assert.Equal("P3", session.CurrentPassage);
    }

    [Fact]
    public void UndoIntoRedirectTurnThenGotoWithoutRender_NavigatesToTarget()
    {
      // Undo into a redirect turn sets _replayFrom = the entry passage. A Goto with
      // no intervening render must not let that stale marker hijack the new turn —
      // it once replayed the old entry's redirect and silently landed on P3.
      var session = new StorySession(Story("1",
        ("1", "P1", "p1"),
        ("2", "P2", "(goto: \"P3\")"),
        ("3", "P3", "PASSAGE-THREE"),
        ("4", "P4", "p4"),
        ("5", "P5", "PASSAGE-FIVE")));
      session.Render();              // P1
      session.Goto("P2");            // redirect turn -> rests on P3
      session.Goto("P4");
      session.Undo();                // -> the P2->P3 turn (_replayFrom = P2), no render
      var result = session.Goto("P5");   // forward without rendering the restored turn
      Assert.Equal("P5", session.CurrentPassage);
      Assert.Equal("PASSAGE-FIVE", result.Text);
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
    public void GotoMissingPassage_IsRefused_SoThePassageStaysLiveAndClickable()
    {
      // The inverse of the old contract, and the point of refusing the Goto: the
      // navigation never happened, so we're still *on* the first passage — its
      // tree is still live and its armed regions still fire. Previously the
      // session walked off to the missing name and the whole passage went inert.
      var story = TwoPassages(
        "|m>[cake](click: ?m)[surprise]",
        "second");
      var session = new StorySession(story);
      var first = session.Render();

      string regionId = null;
      for (int i = 0; i < first.Entries.Count; i++)
        if (first.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive)
          regionId = first.Entries[i].Region?.Id;
      Assert.NotNull(regionId);

      var failed = session.Goto("DoesNotExist");
      Assert.Equal("P1", session.CurrentPassage);
      Assert.True(CountKind(failed, BufferedRenderOutput.Kind.Error) >= 1);
      Assert.Contains("cake", failed.Text);

      // Still armed — the click works exactly as if the bad Goto never happened.
      var dispatched = session.DispatchEvent(regionId);
      Assert.Contains("surprise", dispatched.Text);
    }

    [Fact]
    public void RenderWithMissingPassage_ClearsLiveStateFromPriorRender()
    {
      // A render whose current passage has vanished must clear the live tree, so
      // a dispatch can't fire handlers against a stale one under a passage name
      // that no longer resolves. Goto can no longer strand us on a missing
      // passage (it refuses), so the way to reach this state is the editing API:
      // remove the passage out from under a live session.
      var story = OnePassage("hello (click: ?passage)[wow]");
      var session = new StorySession(story);
      var initial = session.Render();
      Assert.NotEmpty(initial.Entries);

      Assert.True(story.RemovePassage("P1"));
      var blank = session.Render();
      Assert.Equal(string.Empty, blank.Text);

      // Dispatch (any id) is inert because there is no live tree.
      var inert = session.DispatchEvent("r-0");
      Assert.Equal(string.Empty, inert.Text);
    }

    [Fact]
    public void Render_StoryWithNoPassages_ReturnsEmptyResult()
    {
      // A freshly-constructed story has no start passage, so the session has
      // no current passage — Render must return an empty result, not throw.
      var session = new StorySession(new Harlowe());
      var result = session.Render();
      Assert.Equal(string.Empty, result.Text);
      Assert.Empty(result.Entries);
    }
  }
}
