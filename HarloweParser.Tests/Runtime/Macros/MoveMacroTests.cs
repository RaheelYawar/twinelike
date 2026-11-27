using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// <c>(move:)</c> as real Harlowe runs it. Every Harlowe 3 expectation here is
  /// what the real 3.3.9 build rendered, including where that departs from its
  /// manual: a whole variable is deleted, and moving something that can't be
  /// deleted just copies it.
  /// </summary>
  public class MoveMacroTests
  {
    private static HarloweStory Story(string version, params (string name, string body)[] passages)
    {
      var story = new HarloweStory { FormatVersion = version };
      foreach (var (name, body) in passages)
        story.AddPassage(new HarlowePassage { Name = name, RawBody = body });
      story.StartNode = story.GetPassage(passages[0].name).Pid;
      return story;
    }

    private static RenderResult Render(string version, string body)
      => new StorySession(Story(version, ("Start", body))).Render();

    private static int Errors(RenderResult result)
    {
      int count = 0;
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) count++;
      return count;
    }

    [Theory]
    [InlineData("(set: $p to 5)(move: $p into $q)$p $q", "0 5")]
    [InlineData("(set: $p to (a: 1))(move: $p into $q)(print: $p) (print: $q)", "0 1")]
    [InlineData("(set: $p to 5)(move: $p into $p)$p", "0")]
    [InlineData("(set: $p to 5)(move: $p into _q)$p _q", "0 5")]
    [InlineData("(set: $p to 5)(move: $p into $q)(set: $p to 7)$p $q", "7 5")]
    [InlineData("(set: $x to 1, $y to 2)(move: $x into $y)$x $y", "0 1")]
    [InlineData("(set: $p to 5)(print: $p)(move: $p into $q)(print: $p + 1)", "51")]
    [InlineData("(set: $a to 2, $c to 3)(move: $a into $b, $c into $a, $b into $d)$d $a $b $c", "2 3 0 0")]
    [InlineData("(set: $d to (dm:))(set: $b to 3)(move: $b into $d's A)$b (print: $d's A)", "0 3")]
    [InlineData("(set: $a to (a: 3, 1))(set: $b to 2)(move: $b into $a's last)$b (print: $a)", "0 3,2")]
    public void WholeVariableSource_IsDeleted(string source, string expected)
    {
      var result = Render("3.3.9", source);
      Assert.Equal(expected, result.Text);
      Assert.Equal(0, Errors(result));
    }

    [Theory]
    [InlineData("(set: _p to 5)(move: _p into $q)_p $q", " 5", 1)]
    [InlineData("(set: _p to 5)(move: _p into _p)_p", "", 1)]
    [InlineData("[(set: _p to 5)(move: _p into $q)_p]$q", "5", 1)]
    [InlineData("(set: _p to 5)[(move: _p into $q)_p]_p $q", "55 5", 0)]
    [InlineData("(set: _p to 5)[(move: _p into _r)_p _r]_p", "5 55", 0)]
    [InlineData("(set: _p to 5)[[(move: _p into $q)]_p]_p", "55", 0)]
    [InlineData("(set: _p to (a: 1, 2))[(move: _p's 1st into $q)(print: _p)](print: _p) $q", "21,2 1", 0)]
    // The shadow a slot delete leaves takes later writes, not the outer variable.
    [InlineData("(set: _p to (a: 1, 2))[(move: _p's 1st into $q)(set: _p to (a: 9))(print: _p)](print: _p)", "91,2", 0)]
    [InlineData("(set: _a to (a: 1, 2))[(move: _a's 1st into $b)[(set: _a to 9)]_a](print: _a)", "91,2", 0)]
    [InlineData("(set: _d to (dm: \"a\", (dm: \"x\", 1)))[(move: _d's a into _d)(set: _d to 99)](print: _d's x)", "1", 0)]
    public void TempSource_IsDeletedFromTheCurrentScopeOnly(string source, string expected, int errors)
    {
      var result = Render("3.3.9", source);
      Assert.Equal(expected, result.Text);
      Assert.Equal(errors, Errors(result));
    }

    [Theory]
    [InlineData("(set: $arr to (a: 1, 2, 3))(move: $arr's 1st into $var)(print: $arr) $var", "2,3 1")]
    [InlineData("(set: $a to (a: 1, 2, 3))(move: $a's 1st into $b, $a's 2nd into $c)(print: $a) $b $c", "2 1 3")]
    [InlineData("(set: $array to (a: 1, 2, 3))(move: $array into (a: $x, $y))(print: $array) $x $y", "3 1 2")]
    [InlineData("(set: $d to (dm: \"a\", 1, \"b\", 2))(move: $d's a into $x)(print: $d's b) $x", "2 1")]
    [InlineData("(set: $s to \"abc\")(move: $s's 1st into $x)$s $x", "bc a")]
    [InlineData("(set: $a to (a: 1, 2))(move: $a's last into $a's 1st)(print: $a)", "2")]
    [InlineData("(set: $a to (a: 1, 2, 3))(move: $a's 1st into $a's 2nd)(print: $a)", "1,3")]
    // The delete re-walks the variable after the write, and arrays and strings
    // share position space, so it can land in what the destination just stored.
    [InlineData("(set: $a to (a: \"pq\", \"rs\"))(move: $a's 1st into $a)(print: $a)", "q")]
    [InlineData("(set: $a to (a: \"pq\", (a: 1, 2)))(move: $a into (a: $a, $x))(print: $a) (print: $x)", "q 1,2")]
    [InlineData("(set: $a to (a: (a: 1, 2), 3))(move: $a's 1st into $a)(print: $a)", "2")]
    [InlineData("(set: $a to (a: (a: \"pq\", 2)))(move: $a's 1st's 1st into $a's 1st)(print: $a)", "q")]
    // A position counted from the end is recounted at that point.
    [InlineData("(set: $a to (a: \"x\", \"pqr\"))(move: $a's last into $a)(print: $a)", "pq")]
    [InlineData("(set: $a to (a: \"pqr\", \"x\"))(move: $a's 2ndlast into $a)(print: $a)", "pr")]
    [InlineData("(set: $a to (a: 5, (a: 1, 2, 3)))(move: $a's last into $a)(print: $a)", "1,2")]
    [InlineData("(set: $a to (a: 5, (a: 1)))(move: $a's last into $a)(print: $a)", "")]
    [InlineData("(set: $s to \"abc\")(move: $s's last into $s)$s", "")]
    [InlineData("(set: $a to (a: 1, 2, 3))(move: $a's 2ndlast into $a's 4th)(print: $a)", "1,2,2")]
    public void DataStructureSource_LosesTheSlot(string source, string expected)
    {
      var result = Render("3.3.9", source);
      Assert.Equal(expected, result.Text);
      Assert.Equal(0, Errors(result));
    }

    // Reference ignores what src.delete() returns, so these are plain copies.
    [Theory]
    [InlineData("(set: $arr to (a: 1, 2))(move: $arr's length into $x)(print: $arr) $x", "1,2 2")]
    [InlineData("(set: $s to \"abc\")(move: $s's length into $x)$s $x", "abc 3")]
    [InlineData("(set: $c to (rgb: 10, 20, 30))(move: $c's r into $x)$x (print: $c's r)", "10 10")]
    [InlineData("(set: $a to (a: 1, 2))(move: $a's 1st into $a)(print: $a)", "1")]
    [InlineData("(set: $d to (dm: \"a\", 1))(move: $d's a into $d)(print: $d)", "1")]
    [InlineData("(set: $a to (a: (a: 1, 2)))(move: $a's 1st's 1st into $a)(print: $a)", "1")]
    [InlineData("(set: $a to (a: 1, 2))(move: $a into (a: $x, $a))(print: $a) $x", "2 1")]
    [InlineData("(set: $a to (a: 1, 2))(move: $a into (a: $a, $x))(print: $a) $x", "1 2")]
    public void SourceThatCannotBeDeleted_IsJustCopied(string source, string expected)
    {
      var result = Render("3.3.9", source);
      Assert.Equal(expected, result.Text);
      Assert.Equal(0, Errors(result));
    }

    // Removing a character of a character would write into a string, which
    // real Harlowe reports as an error after the destination has been written.
    [Theory]
    [InlineData("(set: $s to \"abc\")(move: $s's 1st's 1st into $b)$s $b", "abc a")]
    [InlineData("(set: $s to \"abc\")(move: $s's last's 1st into $b)$s $b", "abc c")]
    [InlineData("(set: $a to (a: (a: \"pq\")))(move: $a's 1st's 1st's 1st into $a's 1st)(print: $a)", "p")]
    public void DeletingThroughAString_IsAnError(string source, string expected)
    {
      var result = Render("3.3.9", source);
      Assert.Equal(expected, result.Text.Trim());
      Assert.Equal(1, Errors(result));
    }

    [Fact]
    public void ItAfterAWholeVariableMove_IsTheMovedValue()
    {
      var result = Render("3.3.9", "(set: $p to 5)(move: $p into $q)(print: it)");
      Assert.Equal("5", result.Text);
      Assert.Equal(0, Errors(result));
    }

    [Fact]
    public void Blob_WithTurnDataAndADeletion_IsStampedForTheDeletion()
    {
      var moment = new Moment
      {
        PassageName = "A",
        TurnData = "host",
        StoreDelta = new Dictionary<string, HarloweValue> { ["p"] = null },
      };
      string blob = SaveSerializer.SerialiseTimeline(new List<Moment> { moment }, new Moment { PassageName = "A" });
      Assert.Contains("\"version\":3", blob.Replace(" ", ""));
    }

    [Fact]
    public void Harlowe4_ReadingAMovedVariable_IsAnError()
    {
      var result = Render("4.0.0", "(set: $p to 5)(move: $p into $q)$q $p");
      Assert.Equal("5 ", result.Text);
      Assert.Equal(1, Errors(result));
    }

    [Fact]
    public void MovedVariable_IsGoneForTheHost()
    {
      var session = new StorySession(Story("3.3.9", ("Start", "(set: $p to 5)(move: $p into $q)")));
      session.Render();
      Assert.False(session.TryGetVariable("p", out _));
      Assert.Equal(new List<string> { "q" }, session.GetVariableNames());
    }

    private static HarloweStory TurnsStory() => Story("3.3.9",
      ("A", "(set: $p to 5)$p"),
      ("B", "(move: $p into $q)$p $q"),
      ("C", "$p $q"));

    [Fact]
    public void Deletion_LastsAcrossTurns_AndUndoRedoFollowIt()
    {
      var session = new StorySession(TurnsStory());
      Assert.Equal("5", session.Render().Text);
      Assert.Equal("0 5", session.Goto("B").Text);
      Assert.Equal("0 5", session.Goto("C").Text);

      Assert.True(session.Undo());                 // back on B, whose render moves again
      Assert.Equal("0 5", session.Render().Text);
      Assert.True(session.Undo());                 // back on A, before the move
      Assert.Equal("5", session.Render().Text);
      Assert.True(session.TryGetVariable("p", out var p));
      Assert.Equal(5, p.AsNumber);
      Assert.False(session.TryGetVariable("q", out _));

      Assert.True(session.Redo());
      Assert.Equal("0 5", session.Render().Text);
      Assert.True(session.Redo());
      Assert.Equal("0 5", session.Render().Text);
      Assert.False(session.TryGetVariable("p", out _));
    }

    [Fact]
    public void SettingAgainAfterAMove_SurvivesUndo()
    {
      var story = Story("3.3.9",
        ("A", "(set: $p to 1)"),
        ("B", "(move: $p into $q)"),
        ("C", "(set: $p to 2)"),
        ("D", "$p"));
      var session = new StorySession(story);
      session.Render();
      session.Goto("B");
      session.Goto("C");
      Assert.Equal("2", session.Goto("D").Text);
      Assert.True(session.Undo());                 // C, restored with $p gone
      Assert.False(session.TryGetVariable("p", out _));
      Assert.Equal("", session.Render().Text);
      Assert.True(session.TryGetVariable("p", out var p));
      Assert.Equal(2, p.AsNumber);
    }

    [Fact]
    public void Deletion_SurvivesSaveAndLoad()
    {
      var session = new StorySession(TurnsStory());
      session.Render();
      session.Goto("B");
      session.Goto("C");
      Assert.True(session.SaveGame("slot"));

      session.Undo();
      session.Undo();
      session.Render();
      Assert.True(session.TryGetVariable("p", out _));

      Assert.True(session.LoadGame("slot"));
      Assert.Equal("0 5", session.Render().Text);
      Assert.False(session.TryGetVariable("p", out _));
    }

    [Fact]
    public void Blob_IsStampedForDeletions_OnlyWhenItHasOne()
    {
      var withDeletion = new Moment
      {
        PassageName = "A",
        StoreDelta = new Dictionary<string, HarloweValue> { ["p"] = null, ["q"] = HarloweValue.OfNumber(5) },
      };
      var plain = new Moment
      {
        PassageName = "A",
        StoreDelta = new Dictionary<string, HarloweValue> { ["q"] = HarloweValue.OfNumber(5) },
      };
      var present = new Moment { PassageName = "A" };

      string blob = SaveSerializer.SerialiseTimeline(new List<Moment> { withDeletion }, present);
      Assert.Contains("\"version\":3", blob.Replace(" ", ""));
      Assert.Contains("\"p\":null", blob.Replace(" ", ""));
      Assert.Contains("\"version\":1", SaveSerializer.SerialiseTimeline(new List<Moment> { plain }, present).Replace(" ", ""));

      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var result = SaveSerializer.DeserialiseTimeline(blob, Story("3.3.9", ("A", "x")), registry,
        new MacroContext { Store = new HarloweVariableStore() });
      Assert.True(result.Ok, result.Error);
      Assert.True(result.Past[0].StoreDelta.ContainsKey("p"));
      Assert.Null(result.Past[0].StoreDelta["p"]);
      Assert.Equal(5, result.Past[0].StoreDelta["q"].AsNumber);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OlderBlobFormat_CannotCarryADeletion(int version)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      string blob = "{\"version\":" + version + ",\"moments\":[{\"passage\":\"A\",\"vars\":{\"p\":null}}]}";
      var result = SaveSerializer.DeserialiseTimeline(blob, Story("3.3.9", ("A", "x")), registry,
        new MacroContext { Store = new HarloweVariableStore() });
      Assert.False(result.Ok);
    }

    [Fact]
    public void StoreDelta_RecordsADeletion_ButNotForANameThatWasNeverSet()
    {
      var store = new HarloweVariableStore();
      store.Set("p", false, HarloweValue.OfNumber(1));
      store.TakeStoryDelta();

      store.Delete("p", false);
      store.Delete("never", false);
      var delta = store.TakeStoryDelta();

      Assert.Single(delta);
      Assert.True(delta.ContainsKey("p"));
      Assert.Null(delta["p"]);
    }
  }
}
