using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Hidden hooks, <c>(hidden:)</c> and <c>(show:)</c>. Expected page text was
  /// rendered by the real 3.3.9 build.
  /// </summary>
  public class ShowMacroTests
  {
    private static StorySession Session(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
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

    private static bool IsBold(RenderResult result, string text)
    {
      int bold = 0;
      foreach (var entry in result.Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.PushStyle && entry.Style.Bold) bold++;
        if (entry.Kind == BufferedRenderOutput.Kind.PopStyle && bold > 0) bold--;
        if (entry.Kind == BufferedRenderOutput.Kind.Text && entry.Content == text) return bold > 0;
      }
      return false;
    }

    /// <summary>Renders, then fires the region at each index in turn, returning the page after each step.</summary>
    private static List<string> Play(string source, params int[] clicks)
    {
      var session = Session(source);
      var result = session.Render();
      var log = new List<string> { Shown(result) };
      foreach (int n in clicks)
      {
        result = session.DispatchEvent(Regions(result)[n]);
        log.Add(Shown(result));
      }
      return log;
    }

    [Theory]
    [InlineData("|a)[x]", "")]
    [InlineData("[x](a|", "")]
    [InlineData("|a)[x]y", "y")]
    [InlineData("x|a)[y]z", "xz")]
    [InlineData("|a)[=x", "")]
    [InlineData("|a)[==x", "")]
    [InlineData("[|a)[x]]", "")]
    [InlineData("|a)x", "|a)x")]
    [InlineData("|a) [x]", "|a) x")]
    [InlineData("(a|", "(a|")]
    [InlineData("[x] (a|", "x (a|")]
    [InlineData("[x](a| y", " y")]
    [InlineData("[x](a|(b|", "(b|")]
    [InlineData("|)[x]", "|)x")]
    [InlineData("|a b)[x]", "|a b)x")]
    [InlineData("|a>[x](b|", "|a>[x](b|")]
    [InlineData("|1>[x]", "x")]
    [InlineData("[x]<1|", "x")]
    public void HiddenHookMarkup(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a)[x](show: ?a)", "x")]
    [InlineData("[x](a|(show: ?a)", "x")]
    [InlineData("|a)[=x(show: ?a)", "")]
    [InlineData("(show: ?a)|a)[x]", "")]
    [InlineData("|a)[x](show: ?a)(show: ?a)", "x")]
    [InlineData("|1)[x](show: ?1)", "x")]
    [InlineData("|a-b)[x](show: ?a-b)", "x")]
    [InlineData("|a_b)[x](show: ?a_b)", "x")]
    [InlineData("|a-1)[x](show: ?a1)", "x")]
    [InlineData("|aB)[x](show: ?ab)", "x")]
    [InlineData("|a>[x](show: ?a)", "x")]
    [InlineData("|a)[1]|b)[2](show: ?a, ?b)", "12")]
    [InlineData("|a)[1]|a)[2](show: ?a)", "12")]
    [InlineData("|a)[1]|a)[2](show: ?a's 1st)", "1")]
    [InlineData("|a)[1]|a)[2](show: ?a's last)", "2")]
    [InlineData("|a)[1]|a)[2](show: ?a's 3rd)", "")]
    [InlineData("|a)[''x''](show: ?a)", "x")]
    [InlineData("|a)[[[b->b]]](show: ?a)", "b")]
    [InlineData("(show: ?passage)", "")]
    [InlineData("(show: ?nope)", "")]
    public void RevealsHiddenHooks(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("(hidden:)[x]", "")]
    [InlineData("(hidden:)|a>[x](show: ?a)", "x")]
    [InlineData("(set: $h to (hidden:))$h[x]", "")]
    [InlineData("(set: $h to (hidden:))$h|a>[x](show: ?a)", "x")]
    [InlineData("(if: false)|a>[x](show: ?a)", "x")]
    [InlineData("(unless: true)|a>[x](show: ?a)", "x")]
    [InlineData("(if: false)[x](else:)|a>[y](show: ?a)", "y")]
    [InlineData("(if: true)[x](else:)|a>[y](show: ?a)", "xy")]
    [InlineData("(set: $f to false)$f|a>[x](show: ?a)", "x")]
    [InlineData("(if: false)[x](show: ?hook)", "")]
    [InlineData("(if: false)|o>[|a>[x]](show: ?o)(show: ?a)", "x")]
    public void FalseConditionsAndTheHiddenChanger(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("(show:)")]
    [InlineData("(show: 1)")]
    [InlineData("(show: \"a\")")]
    [InlineData("(show: ?page)")]
    [InlineData("(show: ?a, 1)")]
    [InlineData("(print: (show: 1))")]
    [InlineData("(set: $c to (show: 1))")]
    [InlineData("(hidden: 1)[x]")]
    [InlineData("(hidden:)")]
    public void Refusals(string source)
      => Assert.Contains("!", Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a)[$x](set: $x to 1)(show: ?a)", "1")]
    [InlineData("(set: _t to 5)|a)[_t](show: ?a)", "5")]
    [InlineData("|o>[(set: _t to 5)|a)[_t]](set: _t to 6)(show: ?a)", "5")]
    [InlineData("(set: _t to 1)|a)[(set: _t to 2)_t](show: ?a)_t", "22")]
    [InlineData("|a)[(set: _u to 2)](show: ?a)_u", "!")]
    [InlineData("|a)[(set: $y to 1)]$y(show: ?a)$y", "01")]
    [InlineData("(for: each _i, 1, 2)[|a)[_i]](show: ?a)", "12")]
    public void RunsWhenShownInTheScopeItWasWrittenIn(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a)[x](replace: ?a)[y]", "y")]
    [InlineData("|a)[x](append: ?a)[y]", "y")]
    [InlineData("|a)[x](append: ?a)[y](show: ?a)", "x")]
    [InlineData("|a)[x](replace: ?a)[y](show: ?a)", "x")]
    [InlineData("|a)[x](show: ?a)(replace: ?a)[y]", "y")]
    [InlineData("|a)[x](show: ?a)(append: ?a)[y]", "xy")]
    [InlineData("(if: false)|a>[x](append: ?a)[y](show: ?a)", "x")]
    [InlineData("|a)[x(replace: ?a)[y]z](show: ?a)", "y")]
    [InlineData("|a)[x(append: ?a)[y]z](show: ?a)", "xzy")]
    [InlineData("|a)[x(prepend: ?a)[y]z](show: ?a)", "yxz")]
    public void RevisionsAndShowingTakeTurns(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a)[|b)[y]](show: ?a)", "")]
    [InlineData("|a)[|b)[y]](show: ?b)", "")]
    [InlineData("|a)[|b)[y]](show: ?a)(show: ?b)", "y")]
    [InlineData("|a)[|a)[y]](show: ?a)(show: ?a)", "y")]
    [InlineData("|a)[(show: ?a)](show: ?a)", "")]
    [InlineData("|a)[z(show: ?a)w](show: ?a)", "zw")]
    [InlineData("|a)[(show: ?b)]|b)[y](show: ?a)", "y")]
    public void NestedShows(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("(if: false)[q]|a)[r](else:)[z]", "z")]
    [InlineData("(if: true)[q]|a)[r](else:)[z]", "q")]
    [InlineData("(if: false)[q]|a)[r](show: ?a)(else:)[z]", "rz")]
    [InlineData("(if: false)[q](show: ?nope)(else:)[z]", "z")]
    [InlineData("(if: true)|a)[x](else:)[y]", "")]
    [InlineData("(if: false)|a)[x](else:)[y]", "y")]
    [InlineData("(text-style: 'bold')|a)[x](else:)[y]", "")]
    [InlineData("(set: $b to true)$b|a)[x](else:)[y]", "")]
    public void TheElsePairingIsLeftAlone(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("{|a)[x  y](show: ?a)}", "x y")]
    [InlineData("{ |a)[x   y] }(show: ?a)", "x y")]
    [InlineData("|a)[x   y]{(show: ?a)}", "x   y")]
    [InlineData("{(if: false)|a>[x   y]}(show: ?a)", "x y")]
    [InlineData("|a)[{x   y}](show: ?a)", "x y")]
    [InlineData("{(print: \"|a)[x   y]\")(show: ?a)}", "x   y")]
    public void WhitespaceFollowsWhereTheHookSits(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a)[x](if: false)(show: ?a)", "")]
    [InlineData("|a)[x](if: true)(show: ?a)", "x")]
    [InlineData("|a)[x](if: false)[y](show: ?a)", "x")]
    [InlineData("|a)[x](show: ?a)[y]", "xy")]
    [InlineData("|a)[x](show: ?a) [y]", "x y")]
    [InlineData("|a)[x](set: $c to (show: ?a))$c", "x")]
    [InlineData("|a)[x](set: $c to (show: ?a))q", "q")]
    [InlineData("|a)[x](set: $c to (show: ?a))(print: $c)", "[A (show:) command]")]
    [InlineData("(print: (show: ?a))", "[A (show:) command]")]
    [InlineData("|a)[x](a: (show: ?a))", "[A (show:) command]")]
    [InlineData("(print: (hidden:))", "[A (hidden:) changer]")]
    public void IsACommand(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a)[x](text-style: \"bold\")(show: ?a)", true)]
    [InlineData("|a)[x](text-style: \"bold\")+(if: true)(show: ?a)", true)]
    [InlineData("(text-style: \"bold\")|a)[x](show: ?a)", false)]
    [InlineData("(hidden:)+(text-style: \"bold\")|a>[x](show: ?a)", false)]
    [InlineData("(if: false)+(text-style: \"bold\")|a>[x](show: ?a)", false)]
    [InlineData("(enchant: ?a, (text-style: \"bold\"))|a)[x](show: ?a)", true)]
    [InlineData("|a)[x](enchant: ?a, (text-style: \"bold\"))(show: ?a)", true)]
    [InlineData("|a)[x](change: ?a, (text-style: \"bold\"))(show: ?a)", false)]
    [InlineData("|a)[x](show: ?a)(change: ?a, (text-style: \"bold\"))", true)]
    public void WhichStylesReachTheRevealedText(string source, bool bold)
    {
      var result = Session(source).Render();
      Assert.Equal("x", Shown(result));
      Assert.Equal(bold, IsBold(result, "x"));
    }

    [Theory]
    [InlineData("(link: 'go')[(show: ?a)]|a)[x]", new[] { 0 }, new[] { "go", "x" })]
    [InlineData("|a)[x](link: 'go')[(show: ?a)]", new[] { 0 }, new[] { "go", "x" })]
    [InlineData("(link-repeat: 'go')[(show: ?a)]|a)[x]", new[] { 0, 0 }, new[] { "go", "gox", "gox" })]
    [InlineData("|a)[(set: $n to it + 1)$n](link-repeat: 'go')[(show: ?a)]", new[] { 0, 0 }, new[] { "go", "1go", "1go" })]
    [InlineData("(set: _t to 7)|a)[_t](link: 'go')[(set: _t to 8)(show: ?a)]", new[] { 0 }, new[] { "go", "8" })]
    [InlineData("(link: 'go')[(set: _t to 8)(show: ?a)]|o>[(set: _t to 7)|a)[_t]]", new[] { 0 }, new[] { "go", "7" })]
    [InlineData("|a)[(link: 'in')[y]](link: 'go')[(show: ?a)]", new[] { 0, 0 }, new[] { "go", "in", "y" })]
    [InlineData("(click: ?c)[(show: ?a)]|c>[tap]|a)[x]", new[] { 0 }, new[] { "tap", "tapx" })]
    [InlineData("|a)[x](link: 'go')[(append: ?a)[y]](link: 'two')[(show: ?a)]", new[] { 0, 0 }, new[] { "gotwo", "ytwo", "x" })]
    [InlineData("|a)[x](link: 'go')[(show: ?a)(append: ?a)[y]]", new[] { 0 }, new[] { "go", "xy" })]
    [InlineData("(link: 'go')[|a)[x](show: ?a)]", new[] { 0 }, new[] { "go", "x" })]
    [InlineData("(link: 'go')[|a)[x]](link: 'two')[(show: ?a)]", new[] { 0, 0 }, new[] { "gotwo", "two", "x" })]
    [InlineData("|a)[(click: \"y\")[z]y](link: 'go')[(show: ?a)]", new[] { 0, 0 }, new[] { "go", "y", "zy" })]
    [InlineData("|a)[1]|a)[2](link: 'go')[(show: ?a's 2nd)](link: 'two')[(show: ?a)]", new[] { 0, 0 }, new[] { "gotwo", "2two", "12" })]
    [InlineData("|a)[(replace: ?c)[n]]|c>[o](link: 'go')[(show: ?a)]", new[] { 0 }, new[] { "ogo", "n" })]
    [InlineData("|o>[](link: 'go')[(append: ?o)[|a)[x]]](link: 'two')[(show: ?a)]", new[] { 0, 0 }, new[] { "gotwo", "two", "x" })]
    [InlineData("|o>[p]|q>[r](link: 'go')[(append: ?o, ?q)[|a)[x]]](link: 'two')[(show: ?a's 1st)](link: 'three')[(show: ?a)]", new[] { 0, 0, 0 }, new[] { "prgotwothree", "prtwothree", "pxrthree", "pxrx" })]
    [InlineData("(for: each _i, 1, 2)[|a)[_i](link: 'go')[(show: ?a)]]", new[] { 0, 0 }, new[] { "gogo", "12go", "12" })]
    [InlineData("{|a)[x   y](link: 'go')[(show: ?a)]}", new[] { 0 }, new[] { "go", "x y" })]
    [InlineData("|a)[x   y]{(link: 'go')[(show: ?a)]}", new[] { 0 }, new[] { "go", "x   y" })]
    [InlineData("(link: 'go')[(if: false)(show: ?a)]|a)[x]", new[] { 0 }, new[] { "go", "" })]
    public void WorksFromClicks(string source, int[] clicks, string[] expected)
      => Assert.Equal(expected, Play(source, clicks));

    [Theory]
    [InlineData("(link: 'go')|a)[x](show: ?a)", new[] { 0 }, new[] { "go", "x" })]
    [InlineData("(link: 'go')|a)[x](link: 'two')[(show: ?a)]", new[] { 0, 0 }, new[] { "two", "go", "x" })]
    [InlineData("(link-reveal: 'go')|a)[x](show: ?a)", new[] { 0 }, new[] { "go", "gox" })]
    [InlineData("(click: ?b)|a)[x]|b>[y]", new[] { 0 }, new[] { "y", "xy" })]
    [InlineData("(click: ?b)|a)[x]|b>[y](show: ?a)", new[] { 0 }, new[] { "xy", "xxy" })]
    public void AnInteractionAttachedToAHiddenHook(string source, int[] clicks, string[] expected)
      => Assert.Equal(expected, Play(source, clicks));

    [Theory]
    [InlineData("(set: $x to ?a)$x", "!0")]
    [InlineData("(set: _x to ?a)_x", "!!")]
    [InlineData("(put: ?a into $x)$x", "!0")]
    [InlineData("(set: $x to (a: ?a))$x", "!0")]
    [InlineData("(set: $x to (dm: \"k\", ?a))q", "!q")]
    [InlineData("(print: (a: 1, ?a)'s length)", "!")]
    [InlineData("(print: (dm: \"k\", ?a)'s k)", "!")]
    [InlineData("(print: (ds: ?a))", "!")]
    [InlineData("(print: ?a)", "!")]
    [InlineData("(for: each _h, ?a)[x]", "!x")]
    [InlineData("(print: (either: ?a))", "!")]
    [InlineData("(print: (find: _x where true, ?a))", "!")]
    [InlineData("(set: $x to (a: 1))(set: $x's 1st to ?a)$x", "!1")]
    public void AHookNameIsNotData(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a>[x](replace: ?a)[y]", "y")]
    [InlineData("|a>[x](append: ?a)[y]", "xy")]
    [InlineData("|a>[x](prepend: ?a)[y]", "yx")]
    [InlineData("|a>[x](change: ?a, (text-colour: red))", "x")]
    [InlineData("|a>[x](enchant: ?a, (text-colour: red))", "x")]
    [InlineData("(set: $r to (partial: \"replace\", ?a))|a>[x]($r:)[y]", "y")]
    public void TargetingMacrosStillTakeHookNames(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Fact]
    public void GoingToAPassageFromARevealedHookNavigates()
      => Assert.Equal("B", Shown(Session("|a)[(go-to: \"b\")](show: ?a)x").Render()));

    [Theory]
    [InlineData("|a)[x]")]
    [InlineData("[x](a|")]
    [InlineData("|a)[=x")]
    [InlineData("(hidden:)|b>[x](show: ?b, ?c)")]
    public void HiddenMarkupRoundTrips(string source)
    {
      var tokens = new HarloweTokenizer().Tokenize(source);
      var body = new HarloweBodyParser().Parse(tokens, source);
      Assert.Equal(source, new MarkupPrinter().Print(body));
    }

    [Fact]
    public void AHiddenHookStopsAtTheRevealBudget()
    {
      // Each revealed hook prints another hidden copy and shows it again.
      var result = Session("(set: $x to \"|a)[$x$x(show: ?a)]\")$x(show: ?a)").Render();
      Assert.Contains("!", Shown(result));
    }
  }
}
