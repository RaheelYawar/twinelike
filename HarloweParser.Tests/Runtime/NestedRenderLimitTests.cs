using System.Text;
using System.Threading;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// The total budget on nested renders. The depth limit alone lets a passage
  /// that displays itself twice do 2^20 renders, so these cases used to take
  /// seconds or effectively hang.
  /// </summary>
  public class NestedRenderLimitTests
  {
    private static HarloweStory Story(params (string name, string body)[] passages)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      foreach (var (name, body) in passages)
        story.AddPassage(new HarlowePassage { Name = name, RawBody = body });
      story.StartNode = story.GetPassage(passages[0].name).Pid;
      return story;
    }

    private static string Errors(RenderResult result)
    {
      var sb = new StringBuilder();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) sb.Append(entry.Content).Append('\n');
      return sb.ToString();
    }

    /// <summary>
    /// Renders on a background thread with a deadline, so a regression fails
    /// the test instead of hanging the run. The limit is far above normal so a
    /// slow CI machine can't flake it: the failure it guards against takes
    /// hours, not seconds.
    /// </summary>
    private static RenderResult RenderWithin20Seconds(HarloweStory story)
    {
      RenderResult result = null;
      var thread = new Thread(() => result = new StorySession(story).Render(), 16 * 1024 * 1024) { IsBackground = true };
      thread.Start();
      Assert.True(thread.Join(20000), "render did not finish");
      return result;
    }

    [Theory]
    [InlineData("a(display: \"Start\")(display: \"Start\")")]
    [InlineData("(display: \"Start\")(display: \"Start\")(display: \"Start\")")]
    [InlineData("(for: each _i, 1, 2)[(display: \"Start\")]")]
    [InlineData("(for: each _i, 1, 2, 3, 4)[(display: \"Start\")]")]
    public void BranchingSelfDisplay_StopsQuicklyWithAnError(string body)
    {
      var result = RenderWithin20Seconds(Story(("Start", body)));

      Assert.Contains("too much nested content", Errors(result));
      Assert.InRange(result.Text.Length, 0, 2_000_000);
    }

    [Theory]
    [InlineData("|ap>[start](append: ?ap)[(display: \"Start\") end]")]
    [InlineData("|ap>[s](append: ?ap)[(display: \"Start\")](prepend: ?ap)[(display: \"Start\")]")]
    [InlineData("|ap>[s]|ap>[t](replace: ?ap)[(display: \"Start\")(display: \"Start\")]")]
    public void SelfDisplayInsideARevision_StopsQuicklyWithAnError(string body)
    {
      var result = RenderWithin20Seconds(Story(("Start", body)));

      Assert.Contains("too much nested content", Errors(result));
      Assert.InRange(result.Text.Length, 0, 4_000_000);
    }

    [Fact]
    public void DispatchThatExceedsTheBudget_ShowsTheErrorInPlace()
    {
      // The display spends 3 million and the reveal's splice can't fit another 3.
      var session = new StorySession(Story(
        ("Start", "go(click: \"go\")[(display: \"Big\")]"),
        ("Big", new string('x', 3_000_000))));
      var first = session.Render();
      string region = null;
      foreach (var entry in first.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) region = entry.Region.Id;

      var after = session.DispatchEvent(region);

      Assert.Contains("too much nested content", Errors(after));
      Assert.Equal("go", after.Text);
    }

    // ----- A displayed passage is its own frame -----

    [Fact]
    public void DisplayedElse_DoesNotPairWithTheCallersIf()
    {
      var result = new StorySession(Story(("Start", "(if: false)[a](display: \"P\")"), ("P", "(else:)[b]"))).Render();
      Assert.NotEqual("", Errors(result));
      Assert.Equal("", result.Text);
    }

    [Fact]
    public void CallersElse_DoesNotPairWithADisplayedIf()
    {
      var result = new StorySession(Story(("Start", "(display: \"P\")(else:)[c]"), ("P", "(if: true)[a]"))).Render();
      Assert.NotEqual("", Errors(result));
      Assert.Equal("a", result.Text);
    }

    [Fact]
    public void CallersPairing_SurvivesADisplay()
    {
      var result = new StorySession(Story(("Start", "(if: false)[a](display: \"P\")(else:)[c]"), ("P", "(if: true)[b]"))).Render();
      Assert.Equal("", Errors(result));
      Assert.Equal("bc", result.Text);
    }

    [Fact]
    public void TempDeclaredInADisplay_DoesNotLeakOut()
    {
      var result = new StorySession(Story(("Start", "(display: \"P\")_t"), ("P", "(set: _t to 7)"))).Render();
      Assert.Contains("_t is not set", Errors(result));
    }

    [Fact]
    public void DisplayedPassage_SeesAndUpdatesOuterTemps()
    {
      var result = new StorySession(Story(("Start", "(set: _t to 1)(display: \"P\")_t"), ("P", "_t(set: _t to 2)"))).Render();
      Assert.Equal("", Errors(result));
      Assert.Equal("12", result.Text);
    }

    [Fact]
    public void LargeSelfDisplayingPassage_IsBoundedByItsSize()
    {
      string filler = new string('x', 20000);
      var result = new StorySession(Story(("Start", filler + "(display: \"Start\")(display: \"Start\")"))).Render();

      Assert.Contains("too much nested content", Errors(result));
      Assert.InRange(result.Text.Length, 0, 4_000_000 + filler.Length);
    }

    [Fact]
    public void PlainSelfDisplay_StillReportsTheDepthLimit()
    {
      var result = new StorySession(Story(("Start", "(display: \"Start\")"))).Render();
      Assert.Contains("recursion limit reached", Errors(result));
      Assert.DoesNotContain("too much nested content", Errors(result));
    }

    [Fact]
    public void ManySmallDisplays_StayWithinTheBudget()
    {
      var body = new StringBuilder("(set: $n to (a:");
      for (int i = 0; i < 1000; i++) body.Append(i == 0 ? "1" : ",1");
      body.Append("))(for: each _i, ...$n)[(display: \"Row\")]");

      var result = new StorySession(Story(("Start", body.ToString()), ("Row", "r"))).Render();

      Assert.Equal("", Errors(result));
      Assert.Equal(new string('r', 1000), result.Text);
    }

    [Fact]
    public void EachDispatch_GetsItsOwnBudget()
    {
      // One click spends most of the budget, so a shared one would run out by
      // the third click.
      string big = new string('x', 1_500_000);
      var session = new StorySession(Story(
        ("Start", "go(click-rerun: \"go\")[(display: \"Big\")]"),
        ("Big", big)));

      var result = session.Render();
      string region = null;
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) region = entry.Region.Id;
      Assert.NotNull(region);

      for (int i = 0; i < 4; i++)
      {
        result = session.DispatchEvent(region);
        Assert.Equal("", Errors(result));
      }
    }

    [Theory]
    [InlineData("print")]
    [InlineData("display")]
    public void DeepHooksInsideRecursion_FitASmallThreadStack(string kind)
    {
      // The heaviest call chain per level: a changer applying its hook, with
      // the parser running on top of the stack for a printed string. If this
      // overflows, the whole process dies, so the thread is deliberately small.
      var open = new StringBuilder();
      var close = new StringBuilder();
      for (int i = 0; i < 120; i++) { open.Append("(for: each _i, 1)[ "); close.Append(" ]"); }
      string body = kind == "print"
        ? "(set: $x to \"" + open + "$x" + close + "\")$x"
        : open + "(display: \"Start\")" + close;

      string errors = null;
      var thread = new Thread(() => errors = Errors(new StorySession(Story(("Start", body))).Render()), 512 * 1024);
      thread.Start();
      thread.Join();

      Assert.Contains("infinite loop", errors);
    }

    [Fact]
    public void HooksAtTheParserLimit_StillRender()
    {
      var open = new StringBuilder();
      var close = new StringBuilder();
      for (int i = 0; i < 128; i++) { open.Append("[ "); close.Append(" ]"); }

      var result = new StorySession(Story(("Start", open + "deep" + close))).Render();

      Assert.Equal("", Errors(result));
      Assert.Contains("deep", result.Text);
    }

    [Fact]
    public void SelfPrintingVariable_ReportsAnInfiniteLoop()
    {
      var result = new StorySession(Story(("Start", "(set: $x to \"$x\")$x"))).Render();
      Assert.Contains("infinite loop", Errors(result));
    }

    [Fact]
    public void BranchingSelfPrintingVariable_StopsQuickly()
    {
      var result = RenderWithin20Seconds(Story(("Start", "(set: $x to \"a$x$x$x\")$x")));

      Assert.Contains("too much nested content", Errors(result));
    }
  }
}
