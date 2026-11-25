using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// <c>(after:)</c>. Each case is played as real 3.3.9 played it, on a fake
  /// clock whose animation frames come every 16 ms. A step is milliseconds to
  /// let pass: <c>ms@n</c> first clicks the nth link or armed region, and
  /// <c>ms+</c> holds a key down for the step. The expected pages are 3.3.9's,
  /// with each error shown as <c>!</c>.
  /// </summary>
  public class AfterMacroTests
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

    private static List<string> Play(string source, string steps)
    {
      var session = Session(source);
      var current = session.Render();
      var log = new List<string> { Shown(current) };
      double now = 0, nextFrame = 16;
      foreach (var step in steps.Split(','))
      {
        string ms = step;
        bool held = ms.EndsWith("+");
        if (held) ms = ms.Substring(0, ms.Length - 1);
        int at = ms.IndexOf('@');
        if (at >= 0)
        {
          var regions = Regions(current);
          int n = int.Parse(ms.Substring(at + 1));
          if (n < regions.Count) current = session.DispatchEvent(regions[n]);
          else log.Add("(no such link)");
          ms = ms.Substring(0, at);
        }
        double until = now + double.Parse(ms, System.Globalization.CultureInfo.InvariantCulture);
        while (nextFrame <= until)
        {
          var ticked = session.Tick(16, held);
          if (ticked != null) current = ticked;
          nextFrame += 16;
        }
        now = until;
        log.Add(Shown(current));
      }
      return log;
    }

    [Theory]
    [InlineData("a(after: 1s)[b]c", "500,400,90,20,100", new[] { "ac", "ac", "ac", "ac", "abc", "abc" })]
    [InlineData("(after: 32ms)[x]", "16,16,16,16", new[] { "", "", "", "x", "x" })]
    [InlineData("(after: 16ms)[x]", "16,16,16", new[] { "", "", "x", "x" })]
    [InlineData("(after: 1ms)[x]", "16,16", new[] { "", "x", "x" })]
    [InlineData("(after: 0.5)[x]", "16", new[] { "", "x" })]
    [InlineData("(after: 2s)[x]", "1990,20,20", new[] { "", "", "", "x" })]
    [InlineData("(after: 0)[x]y", "16", new[] { "!xy", "!xy" })]
    [InlineData("(after: -1)[x]y", "16", new[] { "!xy", "!xy" })]
    [InlineData("(after: 1s, -1)[x]y", "16", new[] { "!xy", "!xy" })]
    [InlineData("(after:)[x]y", "16", new[] { "!xy", "!xy" })]
    [InlineData("(after: '1s')[x]y", "16", new[] { "!xy", "!xy" })]
    [InlineData("(after: 50ms, 2, 3)[x]y", "100", new[] { "!xy", "!xy" })]
    [InlineData("(after: 50ms, 'a')[x]y", "100", new[] { "!xy", "!xy" })]
    [InlineData("(after: 50ms, 0)[x]y", "100", new[] { "y", "xy" })]
    [InlineData("(after: 100ms)[(print: time)]", "200", new[] { "", "112" })]
    [InlineData("(after: 100ms)[A(after: time + 100ms)[B]]", "100,16,50,100,100", new[] { "", "", "A", "A", "AB", "AB" })]
    [InlineData("There she was. (after: 200ms)[=Covered in fur, (after: time + 200ms)[=sitting on all fours, (after: time + 200ms)[=and howling at the moon.", "100,150,200,200,200", new[] { "There she was. ", "There she was. ", "There she was. Covered in fur, ", "There she was. Covered in fur, sitting on all fours, ", "There she was. Covered in fur, sitting on all fours, and howling at the moon.", "There she was. Covered in fur, sitting on all fours, and howling at the moon." })]
    [InlineData("(if: false)+(after: 50ms)[x]", "100", new[] { "", "x" })]
    [InlineData("(after: 50ms)+(if: false)[x]", "100", new[] { "", "x" })]
    [InlineData("(if: true)[a](after: 50ms)[b](else:)[c]", "100", new[] { "a", "ab" })]
    [InlineData("(if: false)[a](after: 50ms)[b](else:)[c]", "100", new[] { "c", "bc" })]
    [InlineData("(after: 50ms)+(text-style: 'bold')[x]", "100", new[] { "", "x" })]
    [InlineData("(set: $n to 0)(live: 50ms)+(after: 200ms)[(set: $n to it + 1)$n]", "60,60,60,60,60", new[] { "", "", "", "", "1", "1" })]
    [InlineData("(set: $n to 0)(after: 200ms)+(live: 50ms)[(set: $n to it + 1)$n]", "60,60,60,60,60", new[] { "", "", "1", "2", "3", "4" })]
    [InlineData("(after: 50ms)(print: 'x')y", "100", new[] { "!y", "!y" })]
    [InlineData("(set: $n to 0)(after: 500ms)|a>[(set: $n to it + 1)$n](show: ?a)", "100,500", new[] { "1", "1", "2" })]
    [InlineData("(after: 100ms)|a>[x](replace: ?a)[y]", "50,100", new[] { "y", "y", "x" })]
    [InlineData("(after: 100ms)|a>[x](append: ?a)[y]", "50,100", new[] { "y", "y", "x" })]
    [InlineData("(link: 'go')[(after: 100ms)[x]]", "500,20@0,20", new[] { "go", "go", "x", "x" })]
    [InlineData("(link: 'go')[(after: 1s)[x]]", "500,20@0,500", new[] { "go", "go", "", "x" })]
    [InlineData("(live: 100ms)[(after: 300ms)[x](stop:)]", "120,120,120,120", new[] { "", "", "", "x", "x" })]
    [InlineData("(set: _t to 't')(after: 50ms)[_t]", "100", new[] { "", "t" })]
    [InlineData("(for: each _i, 1, 2)[(after: _i * 100ms)[_i]]", "120,100", new[] { "", "1", "12" })]
    [InlineData("(after: 1s, 100ms)[x]", "160+", new[] { "", "x" })]
    [InlineData("(after: 1s, 100ms)[x]", "160", new[] { "", "" })]
    [InlineData("(after: 1s, 0)[x]", "160+", new[] { "", "" })]
    [InlineData("(after: 1s, 100ms)[x]", "48+,500,500", new[] { "", "", "", "x" })]
    [InlineData("(after: 1s, 1s)[x]", "16+,16", new[] { "", "x", "x" })]
    [InlineData("(after: 100ms)[(go-to: 'b')]", "200", new[] { "", "B0" })]
    [InlineData("(after: 50ms)|a)[x]", "100", new[] { "", "x" })]
    [InlineData("(after: 50ms)|a)[x](show: ?a)", "100", new[] { "x", "x" })]
    [InlineData("|a>[old](replace: ?a)+(after: 100ms)[new]", "50,100", new[] { "old", "old", "new" })]
    [InlineData("(after: 50ms)[(print: 1 + 'a')]", "100", new[] { "", "!" })]
    [InlineData("(after: 50ms)[a](after: 50ms)[b]", "100", new[] { "", "ab" })]
    [InlineData("(after: 50ms)[(set: $x to 1)]$x", "100", new[] { "0", "0" })]
    [InlineData("(after: 200ms)[x](link: 'go')[(go-to: 'b')]", "50,300@0", new[] { "go", "go", "B0" })]
    [InlineData("(set: $c to (after: 50ms))$c[x]", "100", new[] { "", "x" })]
    [InlineData("(print: (after: 50ms))", "16", new[] { "[A (after:) changer]", "[A (after:) changer]" })]
    [InlineData("(after: 50ms)[(after: 50ms)[x]]", "64,16,16", new[] { "", "", "x", "x" })]
    [InlineData("{(after: 50ms)[ x ]}y", "100", new[] { "y", "xy" })]
    [InlineData("(after: 50ms)[[[b]]]", "100", new[] { "", "b" })]
    [InlineData("(after: 100ms)[x](hide: ?passage)", "200", new[] { "", "" })]
    [InlineData("|a>[1]|a>[2](replace: ?a)[(after: 50ms)[x]]", "30,70", new[] { "", "", "xx" })]
    [InlineData("(after: 100ms)[x](replace: ?passage)[y]", "50,100", new[] { "y", "y", "y" })]
    [InlineData("(after: 50ms)[(replace: ?a)[new]]|a>[old]", "100", new[] { "old", "new" })]
    [InlineData("(after: 50ms)[(set: $x to 5)](live: 100ms)[$x(stop:)]", "150", new[] { "", "5" })]
    [InlineData("(after: 3s)[x]", "2992,16", new[] { "", "", "x" })]
    [InlineData("(after: 50ms)[|b>[click me]](click: ?b)[clicked]", "100,16@0", new[] { "", "click me", "click meclicked" })]
    [InlineData("(after: 50ms)+(after: 200ms)[x]", "100,120", new[] { "", "", "x" })]
    [InlineData("(after: 200ms)+(after: 50ms)[x]", "100,120", new[] { "", "x", "x" })]
    [InlineData("(after: 1s, 50ms)[x]", "16+,940,10", new[] { "", "", "", "x" })]
    [InlineData("(after: 100ms, 50ms)[(print: time)]", "64+", new[] { "", "32" })]
    [InlineData("(after: 100ms, 50ms)[a](after: 100ms)[b]", "48+,64", new[] { "", "a", "ab" })]
    [InlineData("(link-rerun: 'go')[(after: 50ms)[x]]", "20,100@0,20,100@0", new[] { "go", "go", "gox", "gox", "gox" })]
    [InlineData("(after: 50ms)[(link: 'go')[y]]", "100,16@0", new[] { "", "go", "y" })]
    [InlineData("(after: 50ms)[x](after: 100ms)[(hide: ?passage)]", "80,50", new[] { "", "x", "" })]
    [InlineData("(set: $a to (after: 50ms))(set: $b to $a + (text-style: 'italic'))$b[x]", "100", new[] { "", "x" })]
    [InlineData("(after: 50ms)[(after: 10ms)[a](after: 200ms)[b]]", "64,16,150", new[] { "", "", "a", "ab" })]
    [InlineData("(after: 50ms)[(stop:)x]", "100", new[] { "", "x" })]
    [InlineData("(after: 16.5)[x]", "16,16", new[] { "", "", "x" })]
    [InlineData("(after: 1e999)[x]y", "100", new[] { "y", "y" })]
    [InlineData("(after: 50ms, 1e999)[x]", "16+", new[] { "", "x" })]
    [InlineData("(after: 2.220446049250313e-16)[x]y", "16", new[] { "y", "xy" })]
    [InlineData("(after: 1e-15)[x]y", "16", new[] { "y", "xy" })]
    public void MatchesReference(string source, string steps, string[] expected)
      => Assert.Equal(expected, Play(source, steps));

    [Theory]
    [InlineData("(after: 0)[x]", "The (after:) macro's 1st value is the number 0, but should be a positive number.")]
    [InlineData("(after: -1)[x]", "The (after:) macro's 1st value is the number -1, but should be a positive number.")]
    [InlineData("(after: 1e-20)[x]", "The (after:) macro's 1st value is the number 1e-20, but should be a positive number.")]
    [InlineData("(after: 2.2e-16)[x]", "The (after:) macro's 1st value is the number 2.2e-16, but should be a positive number.")]
    [InlineData("(after: '1s')[x]", "The (after:) macro's 1st value is the string \"1s\", but should be a positive number.")]
    [InlineData("(after: 1s, -1)[x]", "The (after:) macro's 2nd value is the number -1, but should be (optional) a number between 0 and Infinity.")]
    [InlineData("(after: 50ms, 'a')[x]", "The (after:) macro's 2nd value is the string \"a\", but should be (optional) a number between 0 and Infinity.")]
    [InlineData("(after: 50ms)(print: 'x')", "I currently can't attach (live:) or (event:) macros to commands - only hooks.")]
    public void ErrorsAsReferenceWordsThem(string source, string message)
    {
      var error = Session(source).Render().Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(error);
      Assert.Equal(message, error.Content);
    }

    [Fact]
    public void StopsWaitingOnceItRenders()
    {
      var session = Session("(after: 20ms)[x]");
      session.Render();
      Assert.True(session.HasLiveHooks);
      Assert.Null(session.Tick(16));
      Assert.Equal("x", Shown(session.Tick(16)));
      Assert.False(session.HasLiveHooks);
      Assert.Null(session.Tick(16));
    }

    [Fact]
    public void CountsFromTheTurnItWasRenderedIn()
    {
      // Undo renders the passage again, which starts its clock again.
      var session = Session("(after: 40ms)[x][[b]]");
      session.Render();
      for (int i = 0; i < 3; i++) session.Tick(16);
      session.Goto("b");
      Assert.False(session.HasLiveHooks);
      Assert.True(session.Undo());
      Assert.Equal("", Shown(session.Render()).Replace("b", ""));
      Assert.True(session.HasLiveHooks);
      Assert.Null(session.Tick(16));
      Assert.Null(session.Tick(16));
      Assert.Equal("x", Shown(session.Tick(16)).Replace("b", ""));
    }

    [Fact]
    public void ASingleArgumentTickHoldsNothing()
    {
      var session = Session("(after: 1s, 1s)[x]");
      session.Render();
      Assert.Null(session.Tick(16));
      Assert.Equal("x", Shown(session.Tick(16, true)));
    }
  }
}
