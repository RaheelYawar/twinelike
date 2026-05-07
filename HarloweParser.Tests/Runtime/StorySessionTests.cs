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
      // Undo goes back to P1; then Goto to P3 creates a fresh snapshot.
      // Undo from P3 should go back to P1, not P2.
      var session = new StorySession(ThreePassages("p1", "p2", "p3"));
      session.Goto("P2");
      session.Undo();               // back to P1
      session.Goto("P3");           // snapshot: P1 → P3
      session.Undo();
      Assert.Equal("P1", session.CurrentPassage);
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
  }
}
