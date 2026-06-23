using System.Text;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Saving
{
  /// <summary>
  /// End-to-end tests of the author-facing macros: <c>(save-game:)</c>,
  /// <c>(load-game:)</c>, <c>(saved-games:)</c>, plus the infinite-loop guard.
  /// </summary>
  public class SaveLoadMacroTests
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

    private static bool HasError(RenderResult r)
    {
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.Error) return true;
      return false;
    }

    [Fact]
    public void SaveGameMacro_ReturnsTrue_AndPersists()
    {
      var session = new StorySession(Story("(print: (save-game: \"slot1\"))"));
      Assert.Equal("true", session.Render().Text);
      Assert.True(session.LoadGame("slot1")); // round-trips via the session API
    }

    [Fact]
    public void SaveGameMacro_BoolUsableInIf()
    {
      var session = new StorySession(Story("(if: (save-game: \"s\"))[saved]"));
      Assert.Equal("saved", session.Render().Text);
    }

    [Fact]
    public void SavedGamesMacro_ListsSlotAndFilename()
    {
      var session = new StorySession(Story(
        "(save-game: \"slot1\", \"My File\")",
        "(print: (saved-games:))"));
      session.Render();                       // P1 saves
      var text = session.Goto("P2").Text;     // P2 prints the datamap
      Assert.Contains("slot1", text);
      Assert.Contains("My File", text);
    }

    [Fact]
    public void LoadGameMacro_RestoresStateAndNavigatesToLoadedPassage()
    {
      var session = new StorySession(Story(
        "(set: $x to 5)(save-game: \"s\")",   // P1: set, save
        "(set: $x to 99)(load-game: \"s\")",  // P2: mutate, load
        "(print: $x)"));                       // P3
      session.Render();                         // P1: $x = 5, saved
      session.Goto("P2");                       // P2 loads "s" -> navigates back to P1
      Assert.Equal("P1", session.CurrentPassage);
      Assert.Equal("5", session.Goto("P3").Text); // $x restored to 5, not 99
    }

    [Fact]
    public void LoadGameMacro_DirectReload_NoOps_NoInfiniteLoop()
    {
      // P1 saves itself then immediately reloads; without the guard this loops forever.
      var session = new StorySession(Story("(save-game: \"s\")(load-game: \"s\")end"));
      var r = session.Render();
      Assert.Equal("P1", session.CurrentPassage);
      Assert.Contains("end", r.Text); // the loaded render completed — the guard broke the loop
    }

    [Fact]
    public void LoadGameMacro_AfterNavigation_GuardCleared_ReloadFires()
    {
      // After a load, navigating away clears the guard, so a (load-game:) in the new
      // passage fires (isn't no-op'd): load -> goto -> load is permitted.
      var session = new StorySession(Story(
        "(save-game: \"s\")",                 // P1
        "(load-game: \"s\")landed"));         // P2 reloads "s"
      session.Render();                         // P1: save
      session.LoadGame("s");                    // arm the guard, land on P1
      session.Render();                         // render loaded P1 (guard = true)
      session.Goto("P2");                       // navigate (clears guard); P2 reloads -> back to P1
      Assert.Equal("P1", session.CurrentPassage); // the reload fired (not stuck on P2)
    }

    [Fact]
    public void LoadGameMacro_MissingSlot_RendersError_SetsLastLoadError()
    {
      var session = new StorySession(Story("(load-game: \"nope\")", "p2"));
      var r = session.Render();
      Assert.True(HasError(r));
      Assert.NotNull(session.LastLoadError);
      Assert.Equal("P1", session.CurrentPassage); // no navigation on failure
    }
  }
}
