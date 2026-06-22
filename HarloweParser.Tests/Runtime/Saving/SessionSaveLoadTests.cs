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
  }
}
