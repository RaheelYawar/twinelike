using System.Collections.Generic;
using System.Text;
using Harlowe.Runtime;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Host reads of story variables through <see cref="StorySession.TryGetVariable"/>
  /// and <see cref="StorySession.GetVariableNames"/>: read-only, side-effect free,
  /// deep-copied, and reflecting the live store at the documented points in a turn.
  /// </summary>
  public class HostVariableReadTests
  {
    private static HarloweStory Story(params string[] bodies)
    {
      var sb = new StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"1\" ifid=\"TEST-IFID\" creator=\"\" creator-version=\"\">");
      for (int i = 0; i < bodies.Length; i++)
        sb.Append($"<tw-passagedata pid=\"{i + 1}\" name=\"P{i + 1}\" tags=\"\">{bodies[i]}</tw-passagedata>");
      sb.Append("</tw-storydata></body></html>");
      return new HarloweStory(sb.ToString());
    }

    [Fact]
    public void TryGetVariable_ReadsAStoryVariable_WithOrWithoutTheDollar()
    {
      var session = new StorySession(Story("(set: $health to 7)(set: $name to \"Wren\")"));
      session.Render();

      Assert.True(session.TryGetVariable("health", out var health));
      Assert.Equal(7.0, health.AsNumber);
      Assert.True(session.TryGetVariable("$name", out var name));
      Assert.Equal("Wren", name.AsString);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("$missing")]
    [InlineData("$")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGetVariable_ReturnsFalseWhenNothingIsSet(string name)
    {
      var session = new StorySession(Story("(set: $x to 1)"));
      session.Render();

      Assert.False(session.TryGetVariable(name, out var value));
      Assert.Null(value);
    }

    [Fact]
    public void TryGetVariable_DoesNotReadTempVariables()
    {
      var session = new StorySession(Story("(set: _t to 5)"));
      session.Render();

      Assert.False(session.TryGetVariable("_t", out _));
      Assert.False(session.TryGetVariable("t", out _));
    }

    [Fact]
    public void TryGetVariable_ReturnsACopy_SoMutatingItCannotChangeTheStory()
    {
      var session = new StorySession(Story(
        "(set: $inv to (a: \"rope\"))(set: $hero to (dm: \"bag\", (a: \"coin\")))",
        "(print: $inv's length) (print: $hero's bag's length)"));
      session.Render();

      Assert.True(session.TryGetVariable("inv", out var inv));
      inv.AsArray.Add(HarloweValue.OfString("lantern"));
      Assert.True(session.TryGetVariable("hero", out var hero));
      hero.AsDatamap["bag"].AsArray.Clear();

      Assert.True(session.TryGetVariable("inv", out var again));
      Assert.Single(again.AsArray);
      Assert.Equal("1 1", session.Goto("P2").Text);
    }

    [Fact]
    public void Reads_HaveNoSideEffects_OnTheSavedTimeline()
    {
      var story = Story("(set: $x to 1)", "(set: $x to it + 1)(set: $y to (random: 1, 50))", "three");

      var plainStorage = new InMemorySaveStorage();
      var plain = new StorySession(story, 7, plainStorage);
      plain.Render();
      plain.Goto("P2");
      plain.Goto("P3");
      Assert.True(plain.SaveGame("slot"));

      var readStorage = new InMemorySaveStorage();
      var reader = new StorySession(story, 7, readStorage);
      reader.Render();
      ReadAll(reader);
      reader.Goto("P2");
      ReadAll(reader);
      reader.Goto("P3");
      ReadAll(reader);
      Assert.True(reader.SaveGame("slot"));

      string key = SaveKeys.ToStorageKey("TEST-IFID", "slot");
      Assert.True(plainStorage.TryRead(key, out var expected));
      Assert.True(readStorage.TryRead(key, out var actual));
      Assert.Equal(expected, actual);
    }

    private static void ReadAll(StorySession session)
    {
      foreach (var name in session.GetVariableNames())
        Assert.True(session.TryGetVariable(name, out _));
      session.TryGetVariable("missing", out _);
    }

    [Fact]
    public void Reads_FollowTheLiveStore_AcrossUndoAndRender()
    {
      var session = new StorySession(Story("(set: $x to 1)", "(set: $x to 2)"));
      session.Render();
      session.Goto("P2");
      Assert.True(session.TryGetVariable("x", out var x));
      Assert.Equal(2.0, x.AsNumber);

      // Undo restores P1's *start*: its own (set:) hasn't replayed yet.
      Assert.True(session.Undo());
      Assert.False(session.TryGetVariable("x", out _));

      session.Render();
      Assert.True(session.TryGetVariable("x", out x));
      Assert.Equal(1.0, x.AsNumber);
    }

    [Fact]
    public void GetVariableNames_ListsStoryVariablesInOrdinalOrder()
    {
      var session = new StorySession(Story("(set: $b to 1)(set: $a to 2)(set: $B to 3)(set: _t to 4)"));
      session.Render();

      Assert.Equal(new List<string> { "B", "a", "b" }, session.GetVariableNames());
    }

    [Fact]
    public void GetVariableNames_ReturnsAFreshList()
    {
      var session = new StorySession(Story("(set: $a to 1)"));
      session.Render();

      var names = session.GetVariableNames();
      names.Clear();

      Assert.Single(session.GetVariableNames());
      Assert.True(session.TryGetVariable("a", out _));
    }
  }
}
