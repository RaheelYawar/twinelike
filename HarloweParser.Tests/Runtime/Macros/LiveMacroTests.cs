using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// <c>(live:)</c>, <c>(stop:)</c>, <see cref="StorySession.Tick"/> and the
  /// <c>time</c> keyword. Expected text was rendered by the real 3.3.9 build on
  /// a fake clock whose animation frames come every 16 ms, which these tests
  /// copy by ticking in 16 ms frames.
  /// </summary>
  public class LiveMacroTests
  {
    private static StorySession Session(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B(print: time)" });
      story.AddPassage(new HarlowePassage { Name = "c", RawBody = "(live: 100ms)[c(stop:)]" });
      story.StartNode = story.GetPassage("test").Pid;
      return new StorySession(story, 42);
    }

    private static string Shown(RenderResult result)
    {
      var text = new System.Text.StringBuilder();
      foreach (var entry in result.Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.Text || entry.Kind == BufferedRenderOutput.Kind.Link) text.Append(entry.Content);
        else if (entry.Kind == BufferedRenderOutput.Kind.Error) text.Append("!");
      }
      return text.ToString();
    }

    private static List<string> Regions(RenderResult result)
    {
      var ids = new List<string>();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive && !ids.Contains(entry.Region.Id)) ids.Add(entry.Region.Id);
      return ids;
    }

    /// <summary>Renders, then lets each step's milliseconds pass in 16 ms frames, returning the page after each step.</summary>
    private static List<string> Play(string source, params double[] steps) => Play(source, null, steps);

    private static List<string> Play(string source, Dictionary<int, int> clicks, params double[] steps)
    {
      var session = Session(source);
      var current = session.Render();
      var log = new List<string> { Shown(current) };
      double now = 0, nextFrame = 16;
      for (int i = 0; i < steps.Length; i++)
      {
        if (clicks != null && clicks.TryGetValue(i, out int n)) current = session.DispatchEvent(Regions(current)[n]);
        double until = now + steps[i];
        while (nextFrame <= until)
        {
          var ticked = session.Tick(16);
          if (ticked != null) current = ticked;
          nextFrame += 16;
        }
        now = until;
        log.Add(Shown(current));
      }
      return log;
    }

    [Fact]
    public void ShowsAfterItsDelay()
      => Assert.Equal(new[] { "", "", "x", "x" }, Play("(live: 100ms)[x]", 50, 100, 100));

    [Fact]
    public void RendersAgainEachDelay()
      => Assert.Equal(new[] { "", "", "1", "2", "2", "3" }, Play("(set: $n to 0)(live: 100ms)[(set: $n to it + 1)$n]", 50, 100, 100, 100, 100));

    [Fact]
    public void StopEndsIt()
      => Assert.Equal(new[] { "", "1", "2", "2", "3", "3", "3" },
        Play("(set: $n to 0)(live: 100ms)[(set: $n to it + 1)$n(if: $n is 3)[(stop:)]]", 150, 100, 100, 100, 100, 100));

    [Theory]
    [InlineData("(live:)[x]")]
    [InlineData("(live: 0)[x]")]
    [InlineData("(live: -5)[x]")]
    public void NoDelayRendersEveryFrame(string source) => Assert.Equal(new[] { "", "", "x" }, Play(source, 10, 30));

    [Fact]
    public void TheFirstFrameOnlyStartsTheCount()
    {
      // Reference's first animation frame has no earlier timestamp to subtract.
      Assert.Equal(new[] { "", "128", "240" }, Play("(live: 100ms)[(print: time)]", 150, 100));
      Assert.Equal(new[] { "", "", "", "x" }, Play("(live: 2s)[x(stop:)]", 1000, 900, 200));
    }

    [Fact]
    public void TimeIsZeroWhileThePassageRenders()
      => Assert.Equal(new[] { "0", "0true" }, Play("(print: time)(live: 100ms)[(stop:)(print: time > 50)]", 150));

    [Fact]
    public void TimeOnlyMovesWithTick()
    {
      var session = Session("x");
      session.Render();
      Assert.Equal(0, session.Time.AsNumber);
      Assert.Null(session.Tick(250));
      Assert.Equal(250, session.Time.AsNumber);
      session.Goto("b");
      Assert.Equal(0, session.Time.AsNumber);
    }

    [Theory]
    [InlineData("(live: \"x\")[y]", "!y")]
    [InlineData("(live: 1s, 2)[y]", "!y")]
    [InlineData("(live: 100ms)", "!")]
    [InlineData("(live: 100ms)(print: 1)", "!")]
    [InlineData("(live: 100ms)[[b]]", "!")]
    [InlineData("(stop:)x", "x")]
    [InlineData("(text-style: \"bold\")(stop:)x", "!x")]
    public void Refusals(string source, string expected) => Assert.Equal(expected, Shown(Session(source).Render()));

    [Fact]
    public void AttachingToACommandIsReferencesError()
    {
      var error = Session("(live: 100ms)(print: 1)").Render().Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Equal("I currently can't attach (live:) or (event:) macros to commands - only hooks.", error.Content);
    }

    [Theory]
    [InlineData("(live: 100ms) + (text-style: \"bold\")[x(stop:)]", "x")]
    [InlineData("(if: false) + (live: 100ms)[x(stop:)]", "x")]
    [InlineData("(live: 100ms) + (if: false)[x(stop:)]", "x")]
    [InlineData("(set: _t to 5)(live: 100ms)[_t(stop:)]", "5")]
    [InlineData("(live: 100ms)[(set: _t to (either: 1))_t(stop:)]_t", "1!")]
    [InlineData("(live: 100ms)|a)[x(stop:)]", "x")]
    [InlineData("(live: 100ms)[x(stop:)](live: 100ms)[y(stop:)]", "xy")]
    [InlineData("(for: each _i, 1, 2)[(live: 100ms)[_i(stop:)]]", "12")]
    [InlineData("(live: 100ms)[(hide: ?passage)]x", "")]
    [InlineData("|a>[x](live: 100ms)[(replace: ?a)[y](stop:)]", "y")]
    [InlineData("{(live: 100ms)[ x    y ]}", "x y")]
    [InlineData("(live: 50ms)[(set: $a to (a: 1))]|b>[z](live: 100ms)[(append: ?b)[q](stop:)]", "zq")]
    public void AfterOneRender(string source, string expected) => Assert.Equal(expected, Play(source, 150)[1]);

    [Theory]
    [InlineData("(live: 100ms)[x(stop:)](else:)[y]", "!y", "x!y")]
    [InlineData("(if: true)[a](live: 100ms)[x(stop:)](else:)[y]", "a", "ax")]
    [InlineData("(if: false)[a](live: 100ms)[x(stop:)](else:)[y]", "y", "xy")]
    public void DoesntTakePartInElsePairing(string source, string before, string after)
      => Assert.Equal(new[] { before, after }, Play(source, 150));

    [Theory]
    [InlineData("(if: false)[a](live: 100ms)[(else:)[b](stop:)]", "!b")]
    [InlineData("(if: true)[a](live: 100ms)[(else:)[b](stop:)]", "a!b")]
    [InlineData("(live: 100ms)[(else:)[b](stop:)]", "!b")]
    [InlineData("(if: false)[a](live: 100ms)[(if: true)[c](else:)[b](stop:)]", "c")]
    [InlineData("(live: 100ms)[(if: false)[a](stop:)](else:)[b]", "!b")]
    public void ALiveRenderPairsElseOnlyWithinItself(string source, string expected)
      => Assert.Equal(expected, Play(source, 150)[1]);

    [Fact]
    public void ShowRevealsItEarly()
      => Assert.Equal(new[] { "x", "x", "x" }, Play("(live: 100ms)|a>[x(stop:)](show: ?a)", 50, 100));

    [Fact]
    public void GoingToAPassageNavigates()
    {
      Assert.Equal(new[] { "", "B0", "B0" }, Play("(live: 100ms)[(go-to: \"b\")]", 150, 100));
      Assert.Equal(new[] { "", "B0", "B0" }, Play("(live: 100ms)[(stop:)(go-to: \"b\")]", 150, 100));
    }

    [Fact]
    public void AReplacedLiveHookKeepsRunningOffThePage()
      => Assert.Equal(new[] { "", "1", "gone2", "gone3", "gone5" },
        Play("(set: $n to 0)|a>[(live: 100ms)[(set: $n to it + 1)]](live: 250ms)[(replace: ?a)[gone](stop:)](live: 50ms)[$n]", 150, 150, 150, 150));

    [Fact]
    public void NestedLiveHooksAreReplacedBeforeTheyRender()
    {
      Assert.Equal(new[] { "", "", "", "i2" }, Play("(set: $n to 0)(live: 100ms)[(set: $n to it + 1)(live: 50ms)[i$n(stop:)]]", 150, 100, 100));
      Assert.Equal(new[] { "", "", "", "", "" }, Play("(live: 100ms)[(display: \"c\")]", 150, 100, 100, 100));
    }

    [Fact]
    public void ALiveHookMadeByAClickRunsToo()
      => Assert.Equal(new[] { "go", "", "x" }, Play("(link: \"go\")[(live: 100ms)[x(stop:)]]", new Dictionary<int, int> { [0] = 0 }, 50, 150));

    [Fact]
    public void ALinkInALiveHookCanBeClicked()
      => Assert.Equal(new[] { "", "go", "go", "y" }, Play("(live: 100ms)[(link: \"go\")[y]]", new Dictionary<int, int> { [2] = 0 }, 150, 100, 100));

    [Fact]
    public void TickReportsWhetherAnythingRendered()
    {
      var session = Session("(live: 100ms)[x(stop:)]");
      session.Render();
      Assert.True(session.HasLiveHooks);
      Assert.Null(session.Tick(16));
      Assert.Null(session.Tick(50));
      Assert.Equal("x", Shown(session.Tick(60)));
      Assert.False(session.HasLiveHooks);
      Assert.Null(session.Tick(1000));
    }

    [Fact]
    public void ALiveHookInAMacroBodyNeverRenders()
    {
      var session = Session("(set: $m to (macro: [(live: 10ms)[(set: $hit to true)](output-data: 1)]))($m:)");
      Assert.Equal("1", Shown(session.Render()));
      Assert.False(session.HasLiveHooks);
    }

    [Fact]
    public void LiveHooksAreLimited()
    {
      var session = Session("(for: each _i, ...(range: 1, 600))[(live: 1s)[x]]");
      var result = session.Render();
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error && e.Content.Contains("too many (live:) hooks"));
    }

    [Fact]
    public void UndoStartsTheHooksAgain()
    {
      var session = Session("(live: 100ms)[x(stop:)][[b]]");
      session.Render();
      session.Tick(16);
      session.Tick(120);
      Assert.False(session.HasLiveHooks);
      session.Goto("b");
      session.Undo();
      Assert.Equal("b", Shown(session.Render()));
      Assert.True(session.HasLiveHooks);
    }
  }
}
