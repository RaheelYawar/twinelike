using System.Collections.Generic;
using System.Text;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Saving
{
  /// <summary>
  /// Host turn data: opaque per-turn strings attached through
  /// <see cref="StorySession.TurnData"/> that ride the timeline (undo/redo, discarded
  /// with an abandoned redo branch, save/load) without being visible to story code —
  /// plus the save-format guarantee that a session which never sets any keeps writing
  /// the original version-1 blob, byte for byte.
  /// </summary>
  public class TurnDataTests
  {
    private const string Ifid = "TEST-IFID";

    private static HarloweStory Story(params string[] bodies)
    {
      var sb = new StringBuilder();
      sb.Append($"<html><body><tw-storydata name=\"T\" startnode=\"1\" ifid=\"{Ifid}\" creator=\"\" creator-version=\"\">");
      for (int i = 0; i < bodies.Length; i++)
        sb.Append($"<tw-passagedata pid=\"{i + 1}\" name=\"P{i + 1}\" tags=\"\">{bodies[i]}</tw-passagedata>");
      sb.Append("</tw-storydata></body></html>");
      return new HarloweStory(sb.ToString());
    }

    private static string ReadBlob(InMemorySaveStorage storage, string slot)
    {
      Assert.True(storage.TryRead(SaveKeys.ToStorageKey(Ifid, slot), out var blob));
      return blob;
    }

    private static (MacroRegistry reg, MacroContext ctx) Env()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      return (reg, new MacroContext { Store = new HarloweVariableStore() });
    }

    // ----- Session behaviour -----

    [Fact]
    public void TurnData_IsNullUntilSet_AndSettingNullClearsIt()
    {
      var session = new StorySession(Story("one"));
      session.Render();
      Assert.Null(session.TurnData);

      session.TurnData = "shown";
      Assert.Equal("shown", session.TurnData);

      session.TurnData = null;
      Assert.Null(session.TurnData);
    }

    [Fact]
    public void Goto_StartsNewTurnWithoutData_AndKeepsThePreviousTurns()
    {
      var session = new StorySession(Story("one", "two"));
      session.Render();
      session.TurnData = "first";

      session.Goto("P2");
      Assert.Null(session.TurnData);
      session.TurnData = "second";

      var timeline = session.GetTimeline();
      Assert.Equal(2, timeline.Count);
      Assert.Equal("P1", timeline[0].PassageName);
      Assert.Equal("first", timeline[0].TurnData);
      Assert.Equal("P2", timeline[1].PassageName);
      Assert.Equal("second", timeline[1].TurnData);
    }

    [Fact]
    public void UndoAndRedo_ReportTheRestoredTurnsData()
    {
      var session = new StorySession(Story("one", "two"));
      session.Render();
      session.TurnData = "first";
      session.Goto("P2");
      session.TurnData = "second";

      Assert.True(session.Undo());
      Assert.Equal("first", session.TurnData);   // available before the re-render
      session.Render();
      Assert.Equal("first", session.TurnData);

      Assert.True(session.Redo());
      Assert.Equal("second", session.TurnData);
    }

    [Fact]
    public void GotoAfterUndo_DiscardsTheAbandonedBranchsData()
    {
      var session = new StorySession(Story("one", "two", "three", "four"));
      session.Render();
      session.TurnData = "a";
      session.Goto("P2");
      session.TurnData = "b";
      session.Goto("P3");
      session.TurnData = "c";

      Assert.True(session.Undo());
      Assert.True(session.Undo());              // back on P1; P2 and P3 are the redo future
      session.Goto("P4");                       // a new branch discards that future

      Assert.False(session.Redo());
      var timeline = session.GetTimeline();
      Assert.Equal(2, timeline.Count);
      Assert.Equal("a", timeline[0].TurnData);
      Assert.Equal("P4", timeline[1].PassageName);
      Assert.Null(timeline[1].TurnData);
    }

    [Fact]
    public void GetTimeline_ExcludesTheRedoFuture()
    {
      var session = new StorySession(Story("one", "two"));
      session.Render();
      session.Goto("P2");
      session.TurnData = "ahead";
      Assert.True(session.Undo());

      var timeline = session.GetTimeline();
      Assert.Single(timeline);
      Assert.Equal("P1", timeline[0].PassageName);
    }

    [Fact]
    public void GetTimeline_PresentTurnReportsTheRestingPassageAfterARedirect()
    {
      var session = new StorySession(Story("one", "(goto: \"P3\")", "three"));
      session.Render();
      session.Goto("P2");                       // redirects to P3 within the turn

      var timeline = session.GetTimeline();
      Assert.Equal("P3", timeline[timeline.Count - 1].PassageName);
    }

    [Fact]
    public void GetTimeline_IsADetachedSnapshot()
    {
      var session = new StorySession(Story("one"));
      session.Render();
      session.TurnData = "kept";

      var timeline = session.GetTimeline();
      timeline[0].TurnData = "changed";
      timeline.Clear();

      Assert.Equal("kept", session.TurnData);
      Assert.Single(session.GetTimeline());
    }

    [Fact]
    public void TurnData_IsInvisibleToTheStory()
    {
      // Setting turn data changes nothing the story can observe: same render, same
      // variables, same history/turn counts.
      var story = Story("(set: $x to 1)", "(print: $x) (print: turns) (print: (history:))");
      var plain = new StorySession(story);
      plain.Render();
      string expected = plain.Goto("P2").Text;

      var withData = new StorySession(story);
      withData.Render();
      withData.TurnData = "first";
      string actual = withData.Goto("P2").Text;
      withData.TurnData = "second";

      Assert.Equal(expected, actual);
      Assert.Equal(expected, withData.Render().Text);
      Assert.Equal(plain.GetVariableNames(), withData.GetVariableNames());
    }

    // ----- Save / load -----

    [Fact]
    public void SaveThenLoad_RestoresEveryTurnsData_IncludingThePresent()
    {
      var storage = new InMemorySaveStorage();
      var session = new StorySession(Story("one", "two", "three"), storage);
      session.Render();
      session.TurnData = "first";
      session.Goto("P2");
      session.TurnData = "second";
      Assert.True(session.SaveGame("slot"));

      session.Goto("P3");
      session.TurnData = "wandered off";

      Assert.True(session.LoadGame("slot"));
      Assert.Equal("second", session.TurnData);    // the present turn's, before any render
      var timeline = session.GetTimeline();
      Assert.Equal(2, timeline.Count);
      Assert.Equal("first", timeline[0].TurnData);
      Assert.Equal("second", timeline[1].TurnData);

      Assert.True(session.Undo());
      Assert.Equal("first", session.TurnData);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain prose")]
    [InlineData("quotes \" and \\ backslashes")]
    [InlineData("line one\nline two\r\n\ttabbed")]
    [InlineData("{\"narration\": \"You climb.\", \"choices\": [\"Run\", \"Hide\"]}")]
    [InlineData("caf\u00e9 \u2014 \u65e5\u672c\u8a9e \U0001F409")]
    [InlineData("\u2028separators\u2029")]
    public void SaveThenLoad_RoundTripsArbitraryTextExactly(string data)
    {
      var storage = new InMemorySaveStorage();
      var session = new StorySession(Story("one"), storage);
      session.Render();
      session.TurnData = data;
      Assert.True(session.SaveGame("slot"));
      session.TurnData = null;

      Assert.True(session.LoadGame("slot"));
      Assert.Equal(data, session.TurnData);       // "" stays "", not null
    }

    [Fact]
    public void LoadGameMacro_RestoresTurnData()
    {
      var storage = new InMemorySaveStorage();
      var session = new StorySession(Story("one", "(load-game: \"slot\")"), storage);
      session.Render();
      session.TurnData = "saved turn";
      Assert.True(session.SaveGame("slot"));

      session.Goto("P2");                         // the macro installs the save mid-render
      Assert.Equal("P1", session.CurrentPassage);
      Assert.Equal("saved turn", session.TurnData);
    }

    [Fact]
    public void SessionWithoutTurnData_SavesByteIdenticalToTheOriginalFormat()
    {
      // Golden blob captured from the build before turn data existed. Covers every
      // moment field (variables, seed, RNG position, redirect trail) and the
      // compressed bare-name form — a session that never sets turn data must keep
      // writing exactly this, so older builds can still load its saves.
      var storage = new InMemorySaveStorage();
      var session = new StorySession(Story(
        "(set: $x to 1)",
        "(set: $x to (random: 1, 100))(set: $list to (a: 1, \"two\"))",
        "(goto: \"P4\")",
        "done",
        "plain"), 42, storage);
      session.Render();
      session.Goto("P2");
      session.Goto("P3");
      session.Goto("P5");
      Assert.True(session.SaveGame("slot"));

      string expected = string.Join("\n", new[]
      {
        "{",
        "  \"version\": 1,",
        "  \"moments\": [",
        "    {",
        "      \"passage\": \"P1\",",
        "      \"vars\": {",
        "        \"x\": \"1\"",
        "      },",
        "      \"seed\": \"42\"",
        "    },",
        "    {",
        "      \"passage\": \"P2\",",
        "      \"vars\": {",
        "        \"x\": \"59\",",
        "        \"list\": \"(a:1,\\\"two\\\")\"",
        "      },",
        "      \"seedIter\": 1",
        "    },",
        "    {",
        "      \"passage\": \"P4\",",
        "      \"visits\": [",
        "        \"P3\",",
        "        \"P4\"",
        "      ]",
        "    },",
        "    \"P5\"",
        "  ]",
        "}",
      });
      Assert.Equal(expected, ReadBlob(storage, "slot"));
    }

    [Fact]
    public void SaveWithTurnData_IsStampedVersion2_AndClearingItReturnsToVersion1()
    {
      var storage = new InMemorySaveStorage();
      var session = new StorySession(Story("one"), storage);
      session.Render();

      session.TurnData = "x";
      Assert.True(session.SaveGame("with"));
      Assert.Contains("\"version\": 2", ReadBlob(storage, "with"));

      session.TurnData = null;
      Assert.True(session.SaveGame("without"));
      Assert.Contains("\"version\": 1", ReadBlob(storage, "without"));
      Assert.DoesNotContain("turnData", ReadBlob(storage, "without"));
    }

    // ----- Serialiser -----

    [Fact]
    public void TurnDataOnlyMoment_IsNotCompressed_AndRoundTrips()
    {
      var past = new List<Moment> { new Moment { PassageName = "P1", TurnData = "note" } };
      var present = new Moment { PassageName = "P2" };

      string blob = SaveSerializer.SerialiseTimeline(past, present);
      Assert.Contains("\"turnData\": \"note\"", blob);
      Assert.Contains("\"P2\"", blob);             // the data-free turn still compresses

      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("one", "two"), reg, ctx);
      Assert.True(r.Ok, r.Error);
      Assert.Equal("note", r.Past[0].TurnData);
      Assert.Null(r.Present.TurnData);
    }

    [Fact]
    public void NonStringTurnData_FailsLoad()
    {
      var (reg, ctx) = Env();
      var blob = "{ \"version\": 2, \"moments\": [ { \"passage\": \"P1\", \"turnData\": 5 } ] }";
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("one"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("turn data is malformed", r.Error);
    }

    [Fact]
    public void VersionNewerThanCurrent_IsRefused()
    {
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline("{ \"version\": " + (SaveBlobVersion.Current + 1) + ", \"moments\": [\"P1\"] }", Story("one"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("newer version", r.Error);
    }
  }
}
