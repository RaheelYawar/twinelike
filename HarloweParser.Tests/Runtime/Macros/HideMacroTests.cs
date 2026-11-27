using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary><c>(hide:)</c>, and <c>(show:)</c> putting its content back. Expected page text was rendered by the real 3.3.9 build.</summary>
  public class HideMacroTests
  {
    private static StorySession Session(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "n", RawBody = "(hide: ?a)y" });
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
    [InlineData("|a>[x](hide: ?a)", "")]
    [InlineData("|a>[x](hide: ?a)y", "y")]
    [InlineData("(hide: ?a)|a>[x]", "x")]
    [InlineData("|a>[x](hide: ?a)(show: ?a)", "x")]
    [InlineData("|a>[(set: $n to it + 1)$n](hide: ?a)(show: ?a)", "1")]
    [InlineData("|a>[x](hide: ?a)(replace: ?a)[y]", "y")]
    [InlineData("|a>[x](hide: ?a)(append: ?a)[y]", "y")]
    [InlineData("|a>[x](hide: ?a)(append: ?a)[y](show: ?a)", "x")]
    [InlineData("|a>[x](hide: ?a)(replace: ?a)[y](show: ?a)", "x")]
    [InlineData("|a>[x]|b>[y](hide: ?a, ?b)", "")]
    [InlineData("|a>[1]|a>[2](hide: ?a's 2nd)", "1")]
    [InlineData("|a>[1]|a>[2](hide: ?a)", "")]
    [InlineData("|a>[x](hide: ?A)", "")]
    [InlineData("|a>[x](hide: ?b)", "x")]
    [InlineData("|a>[x](hide: ?a)[y]", "y")]
    [InlineData("|a)[x](hide: ?a)(show: ?a)", "x")]
    [InlineData("|a>[x](hide: ?a)(hide: ?a)(show: ?a)", "x")]
    [InlineData("|a>[|b>[x]](hide: ?a)(replace: ?b)[y](show: ?a)", "x")]
    [InlineData("|a>[cat](hide: ?a)(replace: \"cat\")[dog](show: ?a)", "cat")]
    [InlineData("|a>[x](hide: ?a)(show: ?a)(hide: ?a)", "")]
    [InlineData("(set: $h to (hide: ?a))|a>[x]$h", "")]
    [InlineData("{|a>[x   y](hide: ?a)(show: ?a)}", "x y")]
    [InlineData("(for: each _i, 1, 2)[|a>[_i]](hide: ?a's 1st)", "2")]
    [InlineData("(if: false)|a>[x](hide: ?a)(show: ?a)", "x")]
    [InlineData("|a>[x](hide: ?a)(print: \"z\")(show: ?a)", "xz")]
    [InlineData("|a>[(print: 1)](hide: ?a)(show: ?a)(show: ?a)", "1")]
    [InlineData("|a>[x](hide: ?a)|b>[y](show: ?a)", "xy")]
    [InlineData("|a>[x](hide: ?a)|b>[(show: ?a)]", "x")]
    [InlineData("|a>[x](hide: ?a)(if: true)[z](else:)[w]", "z")]
    [InlineData("|a>[x](replace: ?a)[(hide: ?a)]", "")]
    [InlineData("|a>[x](hide: ?a)(if: false)(show: ?a)", "x")]
    public void HidesAndRestores(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a>[x(hide: ?a)]y", "y")]
    [InlineData("|a>[x(hide: ?a)z]", "")]
    [InlineData("|a>[x(hide: ?a)y](show: ?a)", "xy")]
    [InlineData("|a>[x(hide: ?a)(show: ?a)y]", "xy")]
    [InlineData("|a)[x(hide: ?a)y](show: ?a)", "")]
    [InlineData("|a)[x(hide: ?a)y](show: ?a)(show: ?a)", "xy")]
    [InlineData("x(hide: ?passage)y", "")]
    [InlineData("x(hide: ?passage)y(show: ?passage)z", "xyz")]
    [InlineData("|b>[z]|a>[x(append: ?b)[(hide: ?a)]y]", "z")]
    [InlineData("|b>[z](append: ?b)[(hide: ?passage)]y", "")]
    [InlineData("|a>[x|b)[(hide: ?a)q](show: ?b)z]", "")]
    [InlineData("|a>[x{(hide: ?a)}y]", "")]
    [InlineData("(set: $m to (macro: [(hide: ?a)(output-data: 1)]))|a>[x($m:)y]", "")]
    [InlineData("|a>[x](hide: ?a)|b>[y(hide: ?b)z]w", "w")]
    public void AHookHidingItselfHidesAllOfIt(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Fact]
    public void HidingALinkLeavesItEmpty()
    {
      // Reference's ?link selects the link element, and hiding it leaves an empty link.
      var result = Session("[[n]](hide: ?link)x").Render();
      Assert.Equal("x", Shown(result));
      var link = result.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Link);
      Assert.NotNull(link);
      Assert.Equal("", link.Content);
      Assert.Equal("n", Shown(Session("[[n]](hide: ?link)(show: ?link)").Render()));
    }

    [Theory]
    [InlineData("(hide: ?page)")]
    [InlineData("(hide: \"a\")")]
    [InlineData("(hide:)")]
    [InlineData("|a>[x](hide: 1)")]
    [InlineData("|a>[x](hide: ?a, 1)")]
    public void Refusals(string source) => Assert.Contains("!", Shown(Session(source).Render()));

    [Theory]
    [InlineData("|a>[x](text-style: \"bold\")(hide: ?a)", "x!")]
    [InlineData("|a>[x](if: false)(hide: ?a)", "x!")]
    [InlineData("|a>[x](if: true)(hide: ?a)", "x!")]
    public void RefusesAnAttachedChanger(string source, string expected)
      => Assert.Equal(expected, Shown(Session(source).Render()));

    [Fact]
    public void AChangerBeforeShowDoesntStyleRestoredContent()
    {
      var result = Session("|a>[x](hide: ?a)(text-style: \"bold\")(show: ?a)").Render();
      Assert.Equal("x", Shown(result));
      Assert.False(IsBold(result, "x"));
    }

    [Fact]
    public void HidingAndShowingByClicks()
    {
      Assert.Equal(new[] { "xgo", "" }, Play("|a>[x](link: 'go')[(hide: ?a)]", 0));
      Assert.Equal(new[] { "xhs", "hs", "xhs" }, Play("|a>[x](link-repeat: 'h')[(hide: ?a)](link-repeat: 's')[(show: ?a)]", 0, 1));
      Assert.Equal(new[] { "xgo", "x" }, Play("|a>[x](link: 'go')[(hide: ?a)(show: ?a)]", 0));
      Assert.Equal(new[] { "xgo", "x!" }, Play("|a>[x](link: 'go')[(text-style: 'bold')(hide: ?a)]", 0));
    }

    [Fact]
    public void RestoredContentKeepsItsState()
    {
      // The link in the hidden hook was clicked before it was hidden, and comes back clicked.
      Assert.Equal(new[] { "inhs", "yhs", "hs", "yhs" },
        Play("|a>[(link: 'in')[y]](link-repeat: 'h')[(hide: ?a)](link-repeat: 's')[(show: ?a)]", 0, 0, 1));
      Assert.Equal(new[] { "1hs", "hs", "1hs", "hs", "1hs" },
        Play("|a>[(set: $n to it + 1)$n](link-repeat: 'h')[(hide: ?a)](link-repeat: 's')[(show: ?a)]", 0, 1, 0, 1));
    }

    [Fact]
    public void ALinkInRestoredContentStillWorks()
      => Assert.Equal(new[] { "inhs", "inyhs", "hs", "inyhs", "inyyhs" },
        Play("|a>[(link-repeat: 'in')[y]](link-repeat: 'h')[(hide: ?a)](link-repeat: 's')[(show: ?a)]", 0, 1, 1, 0));

    [Fact]
    public void HiddenContentCantBeClicked()
    {
      Assert.Equal(new[] { "gox", "goyx", "" }, Play("(link: 'go')[(hide: ?a)]|a>[(click: 'x')[y]x]", 1, 0));
      Assert.Equal(new[] { "xgo", "" }, Play("(click: ?a)[y]|a>[x](link: 'go')[(hide: ?a)]", 1));
    }

    [Fact]
    public void TheToggleFromTheManual()
      => Assert.Equal(new[] { "Peek", "It says: XHide", "Peek", "It says: XHide" },
        Play("|2>[(link-rerun: 'Peek')[(show: ?1)(hide: ?2)]]|1)[It says: X(link-rerun: 'Hide')[(hide: ?1)(show: ?2)]]", 0, 0, 0));

    [Fact]
    public void RevisionsWhileHiddenAreThrownAway()
    {
      Assert.Equal(new[] { "xgotwothree", "twothree", "zthree", "x" },
        Play("|a>[x](link: 'go')[(hide: ?a)](link: 'two')[(replace: ?a)[z]](link: 'three')[(show: ?a)]", 0, 0, 0));
      Assert.Equal(new[] { "xhtwos", "htwos", "zhs", "xh" },
        Play("|a>[x](link-repeat: 'h')[(hide: ?a)](link: 'two')[(append: ?a)[z]](link: 's')[(show: ?a)]", 0, 1, 1));
    }

    [Fact]
    public void HidingDoesntCarryToTheNextPassage()
    {
      var session = Session("|a>[x](link: \"go\")[(hide: ?a)][[n]]");
      Assert.Equal("xgon", Shown(session.Render()));
      Assert.Equal("y", Shown(session.Goto("n")));
    }

    [Fact]
    public void EachCopyOfAHiddenHookIsShownOnItsOwn()
    {
      // A revision copies the hidden hook into two places, and each copy keeps
      // its own content, so showing one doesn't take the other's. 3.3.9 gives
      // "xx" and "x" here, the other way round.
      Assert.Equal("x", Shown(Session("|o>[]|q>[](append: ?o, ?q)[|a>[x](hide: ?a)](show: ?a's 1st)").Render()));
      Assert.Equal("xx", Shown(Session("|o>[]|q>[](append: ?o, ?q)[|a>[x](hide: ?a)](show: ?a)").Render()));
    }
  }
}
