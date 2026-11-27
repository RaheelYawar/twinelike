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

    [Fact]
    public void ShowingThousandsOfHooks_IsChargedPerHook()
    {
      // Found by fuzzing. The page prints itself, so it holds thousands of
      // hidden ?a hooks and 'go' links, and each hook a click reveals cost a
      // search of the whole page: the first click took half a minute, the next
      // longer. Charged per hook, the click stops at the page-search budget.
      var session = new StorySession(Story(("Start", "(for: each _i, 1, 2)[|a)[_i](print: (passage:)'s source)(link: 'go')[(show: ?a)]]")));
      RenderResult result = null;
      var thread = new Thread(() =>
      {
        result = session.Render();
        foreach (var entry in result.Entries)
          if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) { result = session.DispatchEvent(entry.Region.Id); break; }
      }, 16 * 1024 * 1024) { IsBackground = true };
      thread.Start();
      Assert.True(thread.Join(20000), "the click did not finish");

      Assert.Contains("too much nested content", Errors(result));
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

      // Each copy renders in its target, so an inner (replace:) can empty its
      // own copy, and the recursion may reach the depth limit before the budget.
      Assert.Matches("too much nested content|recursion limit reached", Errors(result));
      Assert.InRange(result.Text.Length, 0, 4_000_000);
    }

    // Each of these macros searches the whole page, so a self-displaying
    // passage used to run thousands of searches over a growing page.
    [Theory]
    [InlineData("a(display: \"Start\")(replace: \"a\")[b](display: \"Start\")")]
    [InlineData("|h>[a](display: \"Start\")(replace: ?h)[b](display: \"Start\")")]
    [InlineData("a(display: \"Start\")(enchant: \"a\", (text-style: \"bold\"))(display: \"Start\")")]
    [InlineData("|h>[a](display: \"Start\")(enchant: ?h, (text-style: \"bold\"))(display: \"Start\")")]
    [InlineData("a(display: \"Start\")(click: \"a\")[b](display: \"Start\")")]
    [InlineData("(link: \"a\")[b](display: \"Start\")(display: \"Start\")")]
    [InlineData("''bold'' //italic// _(display: \"Start\")_unde(replace: \"a\")[ b  c ]rline__ ld'' //italic// __u~~strik(display: \"Start\")_unde~~ ^^super^^")]
    public void SelfDisplayWithATargetingMacro_StopsQuicklyWithAnError(string body)
    {
      var result = RenderWithin20Seconds(Story(("Start", body)));

      Assert.Contains("too much nested content", Errors(result));
    }

    // Wrapping each match used to insert into the same child list, which was
    // quadratic in the number of matches.
    [Theory]
    [InlineData("(replace: \"a\")[b]")]
    [InlineData("(enchant: \"a\", (text-style: \"bold\"))")]
    [InlineData("(click: \"a\")[b]")]
    public void OneMacroOverVeryManyMatches_FinishesQuickly(string macro)
    {
      var result = RenderWithin20Seconds(Story(("Start", new string('a', 200000) + macro)));

      Assert.Equal("", Errors(result));
    }

    // Each of these adds a layer around its target. Enough layers used to
    // overflow the stack, which kills the host with no chance to report it.
    [Theory]
    [InlineData("(enchant: ?h, (text-style: \"bold\"))")]
    [InlineData("(change: ?h, (text-style: \"bold\"))")]
    [InlineData("(click: ?h)[x]")]
    public void ThousandsOfLayersOnOneHook_StopWithAnErrorOnASmallStack(string macro)
    {
      var body = new StringBuilder("|h>[a]");
      for (int i = 0; i < 10000; i++) body.Append(macro);
      var story = Story(("Start", body.ToString()));

      RenderResult result = null;
      var worker = new Thread(() => result = new StorySession(story).Render(), 512 * 1024) { IsBackground = true };
      worker.Start();

      Assert.True(worker.Join(20000), "render did not finish");
      Assert.Contains("too much nested content", Errors(result));
    }

    [Theory]
    [InlineData("a(display: \"Start\")(change: ?page, (text-style: \"bold\"))(display: \"Start\")")]
    [InlineData("a(display: \"Start\")(enchant: ?page, (text-style: \"bold\"))(display: \"Start\")")]
    public void SelfDisplayWrappingThePage_FinishesOnASmallStack(string body)
    {
      var story = Story(("Start", body));

      RenderResult result = null;
      var worker = new Thread(() => result = new StorySession(story).Render(), 512 * 1024) { IsBackground = true };
      worker.Start();

      Assert.True(worker.Join(20000), "render did not finish");
      Assert.Contains("too much nested content", Errors(result));
    }

    [Fact]
    public void AFlatPageThatSearchesTooMuch_StopsWithAnError()
    {
      var body = new StringBuilder(new string('x', 25000));
      for (int i = 0; i < 5000; i++) body.Append("(replace: \"zz\")[y]");

      var result = RenderWithin20Seconds(Story(("Start", body.ToString())));

      Assert.Contains("too much nested content", Errors(result));
    }

    [Fact]
    public void ManyEnchantmentsOnManyLinks_StopWithAnError()
    {
      var body = new StringBuilder();
      for (int i = 0; i < 1500; i++) body.Append("[[l->Other]]");
      for (int i = 0; i < 300; i++) body.Append("(enchant: ?link, (text-style: \"bold\"))");

      var result = RenderWithin20Seconds(Story(("Start", body.ToString()), ("Other", "o")));

      Assert.Contains("too much nested content", Errors(result));
    }

    // Wrapping a link used to search the whole page for it.
    [Theory]
    [InlineData("(enchant: ?link, (text-style: \"bold\"))", 5)]
    [InlineData("(click: ?link)[x]", 5)]
    public void AFewMacrosOverThousandsOfLinks_AllApply(string macro, int count)
    {
      const int links = 5000;
      var body = new StringBuilder();
      for (int i = 0; i < links; i++) body.Append("[[l->Other]]");
      for (int i = 0; i < count; i++) body.Append(macro);

      var result = RenderWithin20Seconds(Story(("Start", body.ToString()), ("Other", "o")));

      Assert.Equal("", Errors(result));
      int linkEvents = 0, wraps = 0;
      foreach (var entry in result.Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.Link) linkEvents++;
        if (entry.Kind == BufferedRenderOutput.Kind.PushStyle || entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) wraps++;
      }
      Assert.Equal(links, linkEvents);
      Assert.Equal(links * count, wraps);
    }

    [Fact]
    public void AnOverBudgetPass_ReportsOnceAndClearsWhenADispatchFits()
    {
      var body = new StringBuilder("|big>[").Append('x', 3500000).Append("]go(click: \"go\")[(replace: ?big)[small]]");
      for (int i = 0; i < 40; i++) body.Append("(enchant: \"zzz\", (text-style: \"bold\"))");
      var session = new StorySession(Story(("Start", body.ToString())));

      var first = session.Render();
      Assert.Equal("this render has displayed or printed too much nested content\n", Errors(first));

      string region = null;
      foreach (var entry in first.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) region = entry.Region.Id;
      var after = session.DispatchEvent(region);

      Assert.Equal("", Errors(after));
      Assert.Contains("small", after.Text);
    }

    [Fact]
    public void ManyTargetingMacrosOnAnOrdinaryPage_AllStillRun()
    {
      var body = new StringBuilder(new string('x', 20000)).Append(" word");
      for (int i = 0; i < 200; i++) body.Append("(replace: \"word\")[word]");
      var result = new StorySession(Story(("Start", body.ToString()))).Render();

      Assert.Equal("", Errors(result));
      Assert.EndsWith(" word", result.Text);
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
      Assert.Equal("b", result.Text);
    }

    [Fact]
    public void CallersElse_DoesNotPairWithADisplayedIf()
    {
      var result = new StorySession(Story(("Start", "(display: \"P\")(else:)[c]"), ("P", "(if: true)[a]"))).Render();
      Assert.NotEqual("", Errors(result));
      Assert.Equal("ac", result.Text);
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

      // 512 KB is the floor promised to hosts, which run the Release build:
      // there the printed case needs about 390 KB. A Debug build needs about
      // 450 KB, and on a 512 KB thread about one run in 200 overflowed on
      // .NET Framework and took the test process down, so Debug gets room.
#if DEBUG
      const int stackKb = 1024;
#else
      const int stackKb = 512;
#endif
      string errors = null;
      var thread = new Thread(() => errors = Errors(new StorySession(Story(("Start", body))).Render()), stackKb * 1024);
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
