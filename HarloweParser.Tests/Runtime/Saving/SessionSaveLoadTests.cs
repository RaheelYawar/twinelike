using System.Text;
using Harlowe.Runtime;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Saving
{
  /// <summary>End-to-end tests of the session save/load engine surface over a real timeline.</summary>
  public class SessionSaveLoadTests
  {
    private static Harlowe Story(params string[] bodies)
    {
      var sb = new StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"1\" ifid=\"TEST-IFID\" creator=\"\" creator-version=\"\">");
      for (int i = 0; i < bodies.Length; i++)
        sb.Append($"<tw-passagedata pid=\"{i + 1}\" name=\"P{i + 1}\" tags=\"\">{bodies[i]}</tw-passagedata>");
      sb.Append("</tw-storydata></body></html>");
      return new Harlowe(sb.ToString());
    }

    [Fact]
    public void SaveThenLoad_RestoresVariableAndPassage()
    {
      var session = new StorySession(Story("(set: $x to 1)", "(set: $x to 2)$x", "(set: $x to 99)$x"), 42);
      session.Render();                               // P1: $x = 1
      Assert.Equal("2", session.Goto("P2").Text);     // $x = 2
      Assert.True(session.SaveGame("slot1"));
      Assert.Equal("99", session.Goto("P3").Text);    // mutate away

      Assert.True(session.LoadGame("slot1"));
      Assert.Equal("P2", session.CurrentPassage);     // back at the saved turn
      Assert.Equal("2", session.Render().Text);       // saved $x restored, not 99
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPropertyMutatedCollection()
    {
      // The P2 turn mutates a datamap slot via property assignment. The write
      // routes through Set, so the turn delta carries the rebuilt datamap;
      // the save serialises it as (dm:)-source and the load re-renders P2
      // from its start state, reproducing the decrement.
      var session = new StorySession(Story(
        "(set: $dm to (dm: \"hp\", 10))",
        "(set: $dm's hp to it - 1)(print: $dm's hp)",
        "(print: $dm's hp)"));
      session.Render();                               // P1: $dm = {hp: 10}
      Assert.Equal("9", session.Goto("P2").Text);
      Assert.True(session.SaveGame("slot"));
      Assert.Equal("9", session.Goto("P3").Text);     // wander off

      Assert.True(session.LoadGame("slot"));
      Assert.Equal("P2", session.CurrentPassage);
      Assert.Equal("9", session.Render().Text);
    }

    [Fact]
    public void SaveThenLoad_RandomDraw_Reproduces()
    {
      // The save records the RNG stream position at the turn's start, so the
      // load's replay re-draws the same `random` card.
      var session = new StorySession(Story(
        "(set: $deck to (a: 10, 20, 30, 40, 50))",
        "(move: $deck's random into $card)(print: $card)",
        "p3"));
      session.Render();
      string drawn = session.Goto("P2").Text;
      Assert.True(session.SaveGame("slot"));
      session.Goto("P3");

      Assert.True(session.LoadGame("slot"));
      Assert.Equal(drawn, session.Render().Text);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsUnpackTurn()
    {
      // Unpack's writes route through Set like every assignment, so the turn
      // delta carries both destructured variables and the replay reproduces
      // them on load.
      var session = new StorySession(Story(
        "(set: $roll to (a: 3, 4))",
        "(unpack: $roll into (a: $first, $second))(print: $first + $second)",
        "p3"));
      session.Render();
      Assert.Equal("7", session.Goto("P2").Text);
      Assert.True(session.SaveGame("slot"));
      session.Goto("P3");

      Assert.True(session.LoadGame("slot"));
      Assert.Equal("7", session.Render().Text);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMoveTurn()
    {
      // A (move:) routes both the destination write and the source deletion
      // through the store's Set, so the turn delta carries both collections
      // and the load's replay reproduces the draw.
      var session = new StorySession(Story(
        "(set: $deck to (a: 5, 6, 7))",
        "(move: $deck's 1st into $card)(print: $card)-(print: $deck's length)",
        "p3"));
      session.Render();
      Assert.Equal("5-2", session.Goto("P2").Text);
      Assert.True(session.SaveGame("slot"));
      session.Goto("P3");

      Assert.True(session.LoadGame("slot"));
      Assert.Equal("5-2", session.Render().Text);
    }

    [Fact]
    public void LoadMissingSlot_ReturnsFalse_SetsError()
    {
      var session = new StorySession(Story("p1"));
      Assert.False(session.LoadGame("nope"));
      Assert.NotNull(session.LastLoadError);
    }

    [Fact]
    public void SavedGames_ListsSlotsAndFilenames()
    {
      var session = new StorySession(Story("p1"));
      session.SaveGame("slotA", "My Save");
      session.SaveGame("slotB"); // filename defaults to the slot
      var games = session.SavedGames();
      Assert.Equal(2, games.Count);
      Assert.Equal("My Save", games["slotA"]);
      Assert.Equal("slotB", games["slotB"]);
    }

    [Fact]
    public void NullBackend_DisablesSaving()
    {
      var session = new StorySession(Story("p1"), (ISaveStorage)null);
      Assert.False(session.SaveGame("slot1"));
      Assert.NotNull(session.LastSaveError);
      Assert.Empty(session.SavedGames());
    }

    [Fact]
    public void SavedGames_IfidLessStory_DoesNotListForeignStoriesSaves()
    {
      // A shared backend already holding an IFID'd story's save.
      var storage = new InMemorySaveStorage();
      storage.TryWrite(SaveKeys.ToStorageKey("SOME-IFID", "theirs"), "blob", "Theirs");

      // An IFID-less story must list only its own (bare-key) saves, not the foreign one.
      var noIfid = new Harlowe("<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"P1\" tags=\"\">p1</tw-passagedata>"
        + "</tw-storydata></body></html>");
      var session = new StorySession(noIfid, storage);
      session.SaveGame("mine");

      var games = session.SavedGames();
      Assert.True(games.ContainsKey("mine"));
      Assert.False(games.ContainsKey("theirs"));
      Assert.Single(games);
    }

    [Fact]
    public void TamperedBlob_FailedLoad_CannotMutateLiveStore()
    {
      // Deserialisation evaluates saved source in a sandboxed context: a
      // tampered blob whose value-source is an assignment must not write
      // through to the live store when the load fails later in the blob —
      // atomicity means a failed load leaves NO trace, not just no timeline.
      var storage = new InMemorySaveStorage();
      var session = new StorySession(Story("(set: $gold to 5)", "(print: $gold)"), storage);
      session.Render();                                   // $gold = 5

      string blob = "{\"version\":1,\"moments\":["
        + "{\"passage\":\"P1\",\"vars\":{\"evil\":\"$gold to 999\"}},"
        + "\"Vanished\"]}";                               // second moment fails validation
      storage.TryWrite(SaveKeys.ToStorageKey("TEST-IFID", "t"), blob, "t");

      Assert.False(session.LoadGame("t"));
      Assert.NotNull(session.LastLoadError);
      Assert.Equal("5", session.Goto("P2").Text);         // live $gold untouched, not 999
    }

    [Fact]
    public void SharedBackend_SaveInOneSession_LoadInAnother()
    {
      // The realistic save-to-disk shape: one backend, two sessions of the same story.
      var storage = new InMemorySaveStorage();
      var story = Story("(set: $x to 7)$x", "p2");

      var s1 = new StorySession(story, storage);
      s1.Render();
      Assert.True(s1.SaveGame("slot"));

      var s2 = new StorySession(story, storage);
      Assert.True(s2.LoadGame("slot"));
      Assert.Equal("7", s2.Render().Text);
    }

    /// <summary>A backend that refuses every write — the quota-exceeded shape.</summary>
    private sealed class RejectingStorage : ISaveStorage
    {
      public bool TryWrite(string key, string blob, string filename) => false;
      public bool TryRead(string key, out string blob) { blob = null; return false; }
      public bool TryDelete(string key) => false;
      public System.Collections.Generic.IEnumerable<SavedGameInfo> Enumerate()
        => new SavedGameInfo[0];
    }

    [Fact]
    public void RejectingBackend_SaveGameFalse_SetsError()
    {
      var session = new StorySession(Story("p1"), new RejectingStorage());
      session.Render();
      Assert.False(session.SaveGame("slot"));
      Assert.Contains("rejected", session.LastSaveError);
    }

    [Fact]
    public void LoadGame_NullBackend_ReturnsFalse_SetsError()
    {
      var session = new StorySession(Story("p1"), (ISaveStorage)null);
      Assert.False(session.LoadGame("slot"));
      Assert.Contains("disabled", session.LastLoadError);
    }

    [Fact]
    public void SeededCtor_WithBackend_IsDeterministic()
    {
      // The (story, seed, storage) overload: same seed → same (random:) draws.
      var story = Story("(print: (random: 1, 1000000))");
      var a = new StorySession(story, 42, new InMemorySaveStorage()).Render().Text;
      var b = new StorySession(story, 42, new InMemorySaveStorage()).Render().Text;
      Assert.Equal(a, b);
    }
  }
}
