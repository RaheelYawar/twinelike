using System;
using System.Text;
using System.Threading;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Short or flat source that builds a deep tree. A stack overflow ends the
  /// host process with no chance to report it, so each of these runs on a
  /// 512 KB stack, the smallest default among common host threads.
  /// </summary>
  public class DeepTreeTests
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

    /// <summary>Runs <paramref name="work"/> on a 512 KB stack, failing rather than hanging if it doesn't finish.</summary>
    private static void OnASmallStack(Action work)
    {
      Exception failure = null;
      var worker = new Thread(() =>
      {
        try { work(); }
        catch (Exception e) { failure = e; }
      }, 512 * 1024) { IsBackground = true };
      worker.Start();
      Assert.True(worker.Join(20000), "did not finish");
      if (failure != null) throw new Exception("failed on the small stack", failure);
    }

    private static string Chain(string first, string op, string operand, int count)
    {
      var sb = new StringBuilder(first);
      for (int i = 0; i < count; i++) sb.Append(' ').Append(op).Append(' ').Append(operand);
      return sb.ToString();
    }

    // The parser builds `a + b + c` in a loop, as a tree whose left side is as
    // deep as the chain is long. Walking it recursively overflowed on about
    // 1,500 operands when rendering, and on a few thousand while loading.
    [Theory]
    [InlineData("1", "+", "1", "10001")]
    [InlineData("1", "-", "1", "-9999")]
    [InlineData("2", "*", "1", "2")]
    [InlineData("true", "and", "true", "true")]
    [InlineData("false", "or", "false", "false")]
    [InlineData("1", "is", "1", "false")]
    [InlineData("1", "+ 1 *", "1", "10001")]
    public void ALongOperatorChain_RendersOnASmallStack(string first, string op, string operand, string expected)
    {
      string body = "(print: " + Chain(first, op, operand, 10000) + ")";
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(Story(("Start", body))).Render());
      Assert.Equal("", Errors(result));
      Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void ALongStringChain_RendersOnASmallStack()
    {
      var story = Story(("Start", "(print: (" + Chain("'a'", "+", "'a'", 10000) + ")'s length)"));
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(story).Render());
      Assert.Equal("10001", result.Text);
    }

    [Fact]
    public void ALongPossessiveChain_ErrorsOnASmallStack()
    {
      var story = Story(("Start", "(set: $a to (a: 1))(print: " + Chain("$a", "'s", "1st", 10000).Replace(" 's ", "'s ") + ")"));
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(story).Render());
      Assert.Contains("has no properties", Errors(result));
    }

    [Fact]
    public void ALongChainInASetAndALambda_RendersOnASmallStack()
    {
      var story = Story(("Start",
        "(set: $x to " + Chain("1", "+", "1", 10000) + ")$x "
        + "(print: (altered: via " + Chain("it", "+", "it", 10000) + ", 1))"));
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(story).Render());
      Assert.Equal("", Errors(result));
      Assert.Equal("10001 10001", result.Text);
    }

    [Fact]
    public void ALongChain_LoadsReportsAndWritesOnASmallStack()
    {
      string body = "(print: " + Chain("1", "+", "1", 10000) + ")(go-to: " + Chain("'a'", "+", "'a'", 10000) + ")";
      OnASmallStack(() =>
      {
        var story = Story(("Start", body));
        Assert.Empty(story.GetPassage("Start").Branches);
        Assert.Empty(story.GetParseErrors());
        Assert.Empty(story.GetBrokenLinks());

        string twee = new TweeWriter().Write(story);
        var reread = new TweeReader().Read(twee);
        Assert.Equal(body, reread.GetPassage("Start").RawBody);
        Assert.Equal(twee, new TweeWriter().Write(reread));
      });
    }

    [Fact]
    public void ALongTypeChain_ErrorsOnASmallStack()
    {
      // `-type` is left-associative too: `num-type num-type _a` is a chain.
      var story = Story(("Start", "(print: " + Chain("num", "-type", "num", 10000).Replace(" -type ", "-type ") + "-type _a)"));
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(story).Render());
      Assert.Contains("must have a variable to its right", Errors(result));
    }

    [Fact]
    public void AChainRunsItsOperandsLeftToRight()
    {
      var calls = new StringBuilder("($f: 1)");
      for (int i = 2; i <= 300; i++) calls.Append(" + ($f: ").Append(i).Append(')');
      var result = new StorySession(Story(("Start",
        "(set: $log to (a:))(set: $f to (macro: num-type _n, [(set: $log to it + (a: _n))(output-data: _n)]))"
        + "(print: " + calls + ")(print: $log is (range: 1, 300))"))).Render();
      Assert.Equal("", Errors(result));
      Assert.Equal("45150true", result.Text);
    }

    [Fact]
    public void ALongChainReadsIt()
    {
      var result = new StorySession(Story(("Start",
        "(set: $x to 2)(set: $x to it + it * it - it)$x (set: $y to 1)(set: $y to " + Chain("it", "+", "it", 9999) + ")$y "
        + "(print: (altered: via " + Chain("it", "+", "pos", 9999) + ", 1, 2))"))).Render();
      Assert.Equal("4 10000 10000,20000", result.Text);
    }

    [Fact]
    public void ReferencesInALongChain_AreCollectedInSourceOrder()
    {
      var calls = new StringBuilder("(display: 'p0')");
      for (int i = 1; i < 10000; i++) calls.Append(" + (display: 'p").Append(i).Append("')");
      OnASmallStack(() =>
      {
        var links = Story(("Start", "(set: $x to " + calls + ")")).GetBrokenLinks();
        Assert.Equal(10000, links.Count);
        for (int i = 0; i < links.Count; i++) Assert.Equal("p" + i, links[i].Target);
      });
    }

    [Fact]
    public void ACustomMacroDeepInAChain_IsntTooDeep()
    {
      // Each operator used to count as a level of evaluation, so a custom macro
      // first in a chain of 500 was refused as too deep. The chain no longer
      // uses stack, so it runs.
      var result = new StorySession(Story(("Start",
        "(set: $m to (macro: [(output-data: 1)]))(print: ($m:)" + new StringBuilder().Insert(0, " + 1", 500) + ")"))).Render();
      Assert.Equal("", Errors(result));
      Assert.Equal("501", result.Text);
    }

    // The loops above must group and stop on errors exactly as recursion did.
    [Theory]
    [InlineData("(print: 10 - 3 - 2)", "5")]
    [InlineData("(print: 2 * 3 + 4 * 5 - 6)", "20")]
    [InlineData("(print: 1 + 2 * 3 - 4 / 2)", "5")]
    [InlineData("(print: 100 / 10 / 5)", "2")]
    [InlineData("(print: (a: 1, 2)'s 1st + (a: 3)'s 1st)", "4")]
    [InlineData("(print: 'ab' + 'c' is 'abc' and true)", "true")]
    [InlineData("(print: (a: 1) contains 1 is true)", "true")]
    [InlineData("(set: $x to 2)(print: $x + $x * 2 - 1 is 5 or false)", "true")]
    [InlineData("(set: $d to (dm: 'a', (dm: 'b', 7)))(print: $d's a's b + 1)", "8")]
    [InlineData("(print: 1st of 2nd of (a: (a: 1), (a: 2, 3)))", "2")]
    public void ChainsGroupAsBefore(string body, string expected)
    {
      var result = new StorySession(Story(("Start", body))).Render();
      Assert.Equal("", Errors(result));
      Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("(print: 1 + 'a' + (nonexistent:))", "Number")]
    [InlineData("(print: 1 + 2 + $u's x + (nonexistent:))", "no properties")]
    [InlineData("(print: true and 1 and (nonexistent:))", "and")]
    public void AChainStopsAtItsFirstError(string body, string firstError)
    {
      var result = new StorySession(Story(("Start", body))).Render();
      string errors = Errors(result);
      Assert.Contains(firstError, errors);
      Assert.DoesNotContain("nonexistent", errors);
      Assert.Single(errors.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Doubled(int times)
    {
      var sb = new StringBuilder("(set: $c to (text-style: 'bold'))");
      for (int i = 0; i < times; i++) sb.Append("(set: $c to $c + $c)");
      return sb.ToString();
    }

    private static int Pushes(RenderResult result)
    {
      int n = 0;
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.PushStyle) n++;
      return n;
    }

    // Each of a changer's layers was its own node, nested, and the passes after
    // a render recurse through every level: 2,048 layers overflowed, and so did
    // a 32-layer changer on each of 120 nested hooks.
    [Theory]
    [InlineData(10, "$c[x]", 1024)]
    [InlineData(5, "NEST", 120 * 32)]
    [InlineData(9, "|a>[x](enchant: ?a, $c)", 512)]
    [InlineData(9, "x(change: ?page, $c)", 512)]
    [InlineData(9, "|a>[x](click: ?a, $c)[y]", 512)]
    [InlineData(9, "$c[[[go->Start]]]", 512)]
    [InlineData(9, "|h)[x]$c(show: ?h)", 512)]
    public void AManyLayeredChanger_RendersOnASmallStack(int doublings, string use, int pushes)
    {
      if (use == "NEST")
      {
        var nest = new StringBuilder();
        for (int i = 0; i < 120; i++) nest.Append("$c[");
        nest.Append('x').Append(']', 120);
        use = nest.ToString();
      }
      var story = Story(("Start", Doubled(doublings) + use));
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(story).Render());
      Assert.Equal("", Errors(result));
      Assert.Equal(pushes, Pushes(result));
    }

    [Fact]
    public void AManyLayeredChangerEnchantingNestedHooks_RendersOnASmallStack()
    {
      var body = new StringBuilder(Doubled(9));
      for (int i = 0; i < 100; i++) body.Append("|a>[");
      body.Append('x').Append(']', 100).Append("(enchant: ?a, $c)");
      var story = Story(("Start", body.ToString()));
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(story).Render());
      Assert.Equal("", Errors(result));
      Assert.Equal(100 * 512, Pushes(result));
    }

    // A changer's layers come off together, as they went on.
    [Fact]
    public void AManyLayeredEnchantment_IsReappliedNotDoubledAfterAClick()
    {
      const string C = "(set: $c to (text-style:'bold')+(text-style:'italic')+(text-colour:red))";
      var session = new StorySession(Story(("Start", C + "|a>[x](enchant: ?a, $c)(link: 'go')[y]")));
      var page = session.Render();
      Assert.Equal(3, Pushes(page));
      string region = null;
      foreach (var entry in page.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) region = entry.Region.Id;
      var after = session.DispatchEvent(region);
      Assert.Equal(3, Pushes(after));
      Assert.Equal("xy", after.Text);
    }

    [Fact]
    public void AManyLayeredArmedRegion_LosesEveryLayerWhenClicked()
    {
      const string C = "(set: $c to (text-style:'bold')+(text-style:'italic')+(text-colour:red))";
      var session = new StorySession(Story(("Start", C + "|a>[x](click: ?a, $c)[y]")));
      var page = session.Render();
      Assert.Equal(3, Pushes(page));
      string region = null;
      foreach (var entry in page.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) region = entry.Region.Id;
      var after = session.DispatchEvent(region);
      Assert.Equal(0, Pushes(after));
      Assert.Equal("xy", after.Text);
    }

    // A layered changer is one level of the page now, so the page-depth limit
    // takes twice as many of these to reach, but it still stops them.
    [Theory]
    [InlineData("(enchant: ?h, (text-style: \"bold\") + (text-style: \"italic\"))")]
    [InlineData("(change: ?h, (text-style: \"bold\") + (text-style: \"italic\"))")]
    [InlineData("(click: ?h, (text-style: \"bold\") + (text-style: \"italic\"))[x]")]
    public void ThousandsOfLayeredChangersOnOneHook_StopWithAnErrorOnASmallStack(string macro)
    {
      var body = new StringBuilder("|h>[a]");
      for (int i = 0; i < 10000; i++) body.Append(macro);
      var story = Story(("Start", body.ToString()));
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(story).Render());
      Assert.Contains("too much nested content", Errors(result));
    }

    [Fact]
    public void PushStyles_CopiesEveryLayer()
    {
      var first = new StyleSpec { Bold = true };
      var second = new StyleSpec { Italic = true };
      var builder = new Harlowe.Runtime.Rendering.RenderTreeBuilder();
      Assert.True(builder.PushStyles(new System.Collections.Generic.List<StyleSpec> { first, second }));
      builder.PopStyle();
      Assert.True(builder.IsBalanced);
      var node = Assert.IsType<Harlowe.Runtime.Rendering.RenderStyleNode>(Assert.Single(builder.Root.Children));

      first.Bold = false;
      second.Italic = false;
      Assert.True(node.Styles[0].Bold);
      Assert.True(node.Styles[1].Italic);
      Assert.False(builder.PushStyles(new System.Collections.Generic.List<StyleSpec>()));
      Assert.Single(builder.Root.Children);
    }

    // A changer added to itself doubles, so without a limit a few dozen
    // doublings exhausted memory.
    [Fact]
    public void AChangerOf1024Patches_ComposesAndOneMoreIsAnError()
    {
      var result = new StorySession(Story(("Start",
        Doubled(10) + "$c[x](set: $d to $c + (text-style: 'italic'))(print: $d is 0)"))).Render();
      Assert.Equal(1024, Pushes(result));
      Assert.Contains("more than 1024 parts", Errors(result));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void TheLimitCountsPatchesNotChangers(int doublings, bool errors)
    {
      // (text-style: "none", "bold") clears styles and then adds one: two patches.
      var body = new StringBuilder("(set: $c to (text-style: 'none', 'bold'))");
      for (int i = 0; i < doublings; i++) body.Append("(set: $c to $c + $c)");
      body.Append("$c[x]");
      var result = new StorySession(Story(("Start", body.ToString()))).Render();
      Assert.Equal(errors, Errors(result).Contains("more than 1024 parts"));
    }

    [Fact]
    public void DoublingAChangerThirtyTimes_StopsWithAnError()
    {
      RenderResult result = null;
      OnASmallStack(() => result = new StorySession(Story(("Start", Doubled(30) + "$c[x]"))).Render());
      Assert.Contains("more than 1024 parts", Errors(result));
      Assert.Equal(1024, Pushes(result));
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, true)]
    public void AnInlineChainOfChangers_IsLimitedTo1024(int changers, bool errors)
    {
      var body = new StringBuilder();
      for (int i = 1; i < changers; i++) body.Append("(text-style: 'bold')+");
      body.Append("(text-style: 'bold')[x]");
      var result = new StorySession(Story(("Start", body.ToString()))).Render();
      Assert.Equal(errors, Errors(result).Contains("more than 1024 parts"));
      // Past the limit the chain is spent, and its last hook shows unstyled,
      // as it does after a right side that isn't a changer.
      Assert.Equal("x", result.Text);
      Assert.Equal(errors ? 0 : 1024, Pushes(result));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void AChangerAttachedToACustomCommand_IsLimitedTo1024(int doublings, bool errors)
    {
      // The command's own changer is (output:), one patch.
      var body = Doubled(doublings) + "(set: $m to (macro: [(output:)[x]]))$c($m:)";
      var result = new StorySession(Story(("Start", body))).Render();
      Assert.Equal(errors, Errors(result).Contains("more than 1024 parts"));
      // Past the limit the command doesn't run at all.
      Assert.Equal(errors ? "" : "x", result.Text);
      Assert.Equal(errors ? 0 : 512, Pushes(result));
    }

    [Fact]
    public void ASaved1024PatchChanger_Loads()
    {
      // A save holds the turn as it began, so the changer is set a turn before.
      var session = new StorySession(Story(("Start", Doubled(10)), ("Next", "$c[x]")));
      session.Render();
      session.Goto("Next");
      Assert.True(session.SaveGame("slot"));
      Assert.True(session.LoadGame("slot"));
      Assert.True(session.TryGetVariable("c", out var c));
      Assert.Equal(HarloweValueKind.Changer, c.Kind);
      Assert.Equal(1024, Pushes(session.Render()));
    }

    /// <summary>Hands back every save with one change made to it.</summary>
    private sealed class EditingStorage : Harlowe.Runtime.Saving.ISaveStorage
    {
      private readonly Harlowe.Runtime.Saving.InMemorySaveStorage _inner = new Harlowe.Runtime.Saving.InMemorySaveStorage();
      public Func<string, string> Edit = blob => blob;
      public bool TryWrite(string key, string blob, string filename) => _inner.TryWrite(key, blob, filename);
      public bool TryRead(string key, out string blob)
      {
        if (!_inner.TryRead(key, out blob)) return false;
        blob = Edit(blob);
        return true;
      }
      public bool TryDelete(string key) => _inner.TryDelete(key);
      public System.Collections.Generic.IEnumerable<Harlowe.Runtime.Saving.SavedGameInfo> Enumerate() => _inner.Enumerate();
    }

    [Fact]
    public void ASaveHoldingAChangerPastTheLimit_DoesntLoad()
    {
      // An older build could save a changer of more patches than this one
      // makes. Loading is all or nothing, so the session stays as it was.
      var storage = new EditingStorage();
      var session = new StorySession(Story(("Start", Doubled(10)), ("Next", "$c[x]"), ("Other", "o")), storage);
      session.Render();
      session.Goto("Next");
      Assert.True(session.SaveGame("slot"));
      session.Goto("Other");

      // An edit that keeps 1,024 still loads, so the one after fails for its size.
      storage.Edit = blob =>
      {
        int at = blob.IndexOf("bold", StringComparison.Ordinal);
        return blob.Substring(0, at) + "italic" + blob.Substring(at + 4);
      };
      Assert.True(session.LoadGame("slot"));
      session.Goto("Other");

      storage.Edit = blob =>
      {
        int at = blob.IndexOf("(text-style:", StringComparison.Ordinal);
        int plus = blob.IndexOf('+', at);
        return blob.Insert(at, blob.Substring(at, plus - at + 1));
      };
      Assert.False(session.LoadGame("slot"));
      Assert.Equal("o", session.Render().Text);
      Assert.True(session.TryGetVariable("c", out var c));
      Assert.Equal(HarloweValueKind.Changer, c.Kind);
    }

    [Theory]
    [InlineData("(print: (1 + 2) * 3 - (4 - 5) - 6)", "(print: (1 + 2) * 3 - (4 - 5) - 6)")]
    [InlineData("(print: (1 - 2) - 3)", "(print: 1 - 2 - 3)")]
    [InlineData("(print: 1 - (2 - 3))", "(print: 1 - (2 - 3))")]
    [InlineData("(print: ($a's b)'s c)", "(print: $a's b's c)")]
    [InlineData("(print: 1st of (2nd of $a))", "(print: 1st of 2nd of $a)")]
    [InlineData("(print: (1 is 2) is (not false) and true)", "(print: 1 is 2 is (not false) and true)")]
    [InlineData("(print: ((1 or 2) + 3) * 4)", "(print: ((1 or 2) + 3) * 4)")]
    [InlineData("(print: $a is an array or $b is not a number)", "(print: $a is an array or $b is not a number)")]
    [InlineData("(print: num-type num-type _a)", "(print: num-type num-type _a)")]
    [InlineData("(print: ...num-type _a)", "(print: ...num-type _a)")]
    public void PrintedChainsKeepTheirGrouping(string source, string expected)
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source), source);
      Assert.Equal(expected, new MarkupPrinter().Print(ast));
    }
  }
}
