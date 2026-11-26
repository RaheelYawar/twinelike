using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Collapsing whitespace in what renders after the passage: a click's
  /// revealed hook, a live hook's tick, a <c>(show:)</c>. Reference collapses
  /// each such render on its own as it finishes, in <c>Section.execute</c>, and
  /// leaves the rest of the page as it was. Each case is played as real 3.3.9
  /// played it, on a fake clock with a frame every 16 ms: a step is
  /// milliseconds to let pass, and <c>ms@n</c> first clicks the nth link or
  /// armed region. The expected pages are 3.3.9's text, each error shown as
  /// <c>!</c>.
  /// </summary>
  public class LaterRenderCollapseTests
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

    private static List<string> Play(StorySession session, string steps)
    {
      var current = session.Render();
      var log = new List<string> { Shown(current) };
      double now = 0, nextFrame = 16;
      foreach (var step in steps.Split(','))
      {
        string ms = step;
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
          var ticked = session.Tick(16);
          if (ticked != null) current = ticked;
          nextFrame += 16;
        }
        now = until;
        log.Add(Shown(current));
      }
      return log;
    }

    [Theory]
    [InlineData("{(live: 100ms)[ (set: $n to it + 1) $n ]}", "150", new[] { "", "1 " })]
    [InlineData("{(live: 100ms)[(stop:) x ]}", "150", new[] { "", "x " })]
    [InlineData("{(live: 100ms)[(stop:)x]}", "150", new[] { "", "x" })]
    [InlineData("{(live: 100ms)[(stop:) x ] y}", "150", new[] { "y", "x y" })]
    [InlineData("{(live: 100ms)[(stop:) x ]}y", "150", new[] { "y", "x y" })]
    [InlineData("{a (live: 100ms)[(stop:) x ]}", "150", new[] { "a", "a x " })]
    [InlineData("{a (live: 100ms)[(stop:) x ] b}", "150", new[] { "a b", "a x b" })]
    [InlineData("{(live: 100ms)[(stop:)(print: \"\")  ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)(print: (a:))  ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)(set: $x to 1)  ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)(print: \"x\")  ]}", "150", new[] { "", "x " })]
    [InlineData("{(live: 100ms)[(stop:)(print: \"x\")]}", "150", new[] { "", "x" })]
    [InlineData("(set: $m to (macro: [(output-data: [])])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [(output-data: \"\")])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [(output: )[]])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("|bt>[](set: $m to (macro: [(replace: ?bt)[appeared! ](output-data: [])])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "appeared! " })]
    [InlineData("{(live: 100ms)[(stop:)  x  (print: 1)  ]}", "150", new[] { "", "x 1 " })]
    [InlineData("{(live: 100ms)[x (stop:)  ]}", "150", new[] { "", "x " })]
    [InlineData("{(live: 100ms)[ x (stop:)]}", "150", new[] { "", "x" })]
    [InlineData("{(live: 100ms)[(stop:)[ x ] ]}", "150", new[] { "", "x " })]
    [InlineData("{(live: 100ms)[(stop:)\n x \n]}", "150", new[] { "", "x " })]
    [InlineData("{(live: 100ms)[(stop:)\n x \n]\n}", "150", new[] { "", "x " })]
    [InlineData("x{(live: 100ms)[(stop:) y ]}z", "150", new[] { "xz", "xy z" })]
    [InlineData("{(live: 100ms)[(stop:)b ]}", "150", new[] { "", "b " })]
    [InlineData("{(link: \"a\")[ x ]}", "50@0", new[] { "a", "x" })]
    [InlineData("{(link: \"a\")[ x ] y}", "50@0", new[] { "a y", "x  y" })]
    [InlineData("{(link: \"a\")[(print: 1) ]}", "50@0", new[] { "a", "1 " })]
    [InlineData("{(link: \"a\")[(set: $x to 1) x ]}", "50@0", new[] { "a", "x " })]
    [InlineData("{(link-reveal: \"a\")[ x ]}", "50@0", new[] { "a", "a x" })]
    [InlineData("{(link-reveal: \"a\")[ x ] y}", "50@0", new[] { "a y", "a x  y" })]
    [InlineData("{a (click: \"a\")[ x ]}", "50@0", new[] { "a", "a x" })]
    [InlineData("{|h>[a] (click: ?h)[ x ] }", "50@0", new[] { "a", "a x" })]
    [InlineData("{(link: \"a\")[(replace: ?h)[ y ]] |h>[z]}", "50@0", new[] { "a z", " y" })]
    [InlineData("{|h>[z] (link: \"a\")[(replace: ?h)[(set: $x to 1) y ]]}", "50@0", new[] { "z a", "y  " })]
    [InlineData("{(link: \"a\")[ x ]\n\n}", "50@0", new[] { "a", "x" })]
    [InlineData("{(link-repeat: \"a\")[ x ]}", "50@0", new[] { "a", "a x" })]
    [InlineData("(set: $m to (macro: [{ (output-data: []) }])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [ (output-data: []) ])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [x(output-data: [])])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [{\n (set: $q to 1)\n (output-data: [])\n}])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [{(output-data: [])}])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [{ }(output-data: [])])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("(set: $m to (macro: [ (set: $q to 1)(output-data: [])])){(live: 100ms)[\n    (stop:)\n($m:)\n\n ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)(print: \"\")\n ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)[ ]\n ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)(if: true)[ ]\n ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)(if: false)[x]\n ]}", "150", new[] { "", "" })]
    [InlineData("{(live: 100ms)[(stop:)(print: \" \")\n ]}", "150", new[] { "", "  " })]
    [InlineData("{x(live: 100ms)[(stop:) ]}", "150", new[] { "x", "x " })]
    [InlineData("{x (live: 100ms)[(stop:) ]}", "150", new[] { "x", "x " })]
    [InlineData("{x (live: 100ms)[(stop:)\n ]}", "150", new[] { "x", "x " })]
    [InlineData("{x (live: 100ms)[\n(stop:)\n]}", "150", new[] { "x", "x" })]
    [InlineData("{x (live: 100ms)[(stop:)(print: 1)]}", "150", new[] { "x", "x1" })]
    [InlineData("{(live: 100ms)[(stop:){ x } ]}", "150", new[] { "", "x " })]
    [InlineData("{(live: 100ms)[(stop:)\n* a\n* b\n]}", "150", new[] { "", "ab" })]
    [InlineData("{x (live: 100ms)[(stop:)\n* a\n* b\n]}", "150", new[] { "x", "x ab" })]
    [InlineData("(live: 100ms)[(stop:){ x } y ]", "150", new[] { "", "x y " })]
    [InlineData("{(link: \"a\")[{ x }] y}", "50@0", new[] { "a y", "x  y" })]
    [InlineData("(link: \"a\")[{ x } y]", "50@0", new[] { "a", "x y" })]
    [InlineData("{|h>[]}(link: \"a\")[(replace: ?h)[ x ]]", "50@0", new[] { "a", " x " })]
    [InlineData("{|h>[]}{(link: \"a\")[(replace: ?h)[ x ]]}", "50@0", new[] { "a", "x" })]
    [InlineData("{(link: \"a\")[(show: ?h)]|h)[ x ] y}", "50@0", new[] { "a y", "x  y" })]
    [InlineData("{(link: \"a\")[ x ]}{ y}", "50@0", new[] { "a y", "x  y" })]
    [InlineData("{ (link: \"a\")[  x  y  ]  z }", "50@0", new[] { "a z", "x y  z" })]
    [InlineData("{(link-repeat: \"a\")[ x ]}", "50@0,50@0", new[] { "a", "a x", "a x x" })]
    [InlineData("{(link-repeat: \"a\")[ x ] y}", "50@0,50@0", new[] { "a y", "a x  y", "a x x  y" })]
    [InlineData("{(link: \"a\")[(print: \"\")x ]}", "50@0", new[] { "a", "x " })]
    [InlineData("{b (link: \"a\")[ x ]}", "50@0", new[] { "b a", "b x" })]
    [InlineData("{b(link: \"a\")[ x ]}", "50@0", new[] { "ba", "b x" })]
    [InlineData("{(link: \"a\")[\n* x\n* y\n] z}", "50@0", new[] { "a z", "xy z" })]
    [InlineData("{(link: \"a\")[ x ] [ y ]}", "50@0", new[] { "a y", "x  y" })]
    [InlineData("x{(link: \"a\")[ y ]}z", "50@0", new[] { "xaz", "xyz" })]
    [InlineData("{(link: \"a\")[(replace: ?h)[ y ] w ] |h>[z]}", "50@0", new[] { "a z", "w  y" })]
    [InlineData("{(link-repeat: \"a\")[ ] (link-repeat: \"b\")[ x ]}", "50@0,50@1", new[] { "a b", "a  b", "a  b x" })]
    [InlineData("{(link: \"a\")[ { x } { y } ] z}", "50@0", new[] { "a z", "x y  z" })]
    [InlineData("(link: \"a\")[{ a {  b  } c }] z", "50@0", new[] { "a z", "a b c z" })]
    [InlineData("{(link-repeat: \"go\")[ word ](click: \"word\")[!]}", "50@0,50@1", new[] { "go", "go word", "go word!" })]
    [InlineData("{(link-repeat: \"go\")[ word ]}(click: \"word\")[!]", "50@0,50@1", new[] { "go", "go word", "go word!" })]
    [InlineData("{(live: 100ms)[(stop:)(if: true)[ x ]]}", "150", new[] { "", "x" })]
    [InlineData("{(live: 100ms)[(stop:)(link: \"q\")[r] ]}", "150", new[] { "", "q " })]
    [InlineData("{(link: \"a\")[(link: \"q\")[r] ] z}", "50@0", new[] { "a z", "q  z" })]
    [InlineData("{(link: \"a\")[|h>[(hide: ?h) x ]](link: \"b\")[(show: ?h)]}", "50@0,50@0", new[] { "ab", "b", "x" })]
    [InlineData("{(link: \"a\")[|h>[(hide: ?h) x ] y](link: \"b\")[(show: ?h) z]}", "50@0,50@0", new[] { "ab", "yb", "xy z" })]
    [InlineData("(link: \"a\")[|h>[(hide: ?h){ x } { y }]](link: \"b\")[(show: ?h)]", "50@0,50@0", new[] { "ab", "b", "x y" })]
    [InlineData("(link: \"a\")[|h>[(hide: ?h){ x }{ y }]](link: \"b\")[(show: ?h)]", "50@0,50@0", new[] { "ab", "b", "xy" })]
    [InlineData("(link: \"a\")[|h>[(hide: ?h)[{ x } { y }]]](link: \"b\")[(show: ?h)]", "50@0,50@0", new[] { "ab", "b", "x y" })]
    [InlineData("{(link: \"a\")[|h>[(hide: ?h){ x } { y }]](link: \"b\")[(show: ?h)]}", "50@0,50@0", new[] { "ab", "b", "xy" })]
    [InlineData("(link: \"a\")[|h>[(hide: ?h)a{ x }b]](link: \"b\")[(show: ?h)]", "50@0,50@0", new[] { "ab", "b", "axb" })]
    public void MatchesReference(string source, string steps, string[] expected)
      => Assert.Equal(expected, Play(Session(source), steps));

    [Fact]
    public void NoEmptyTextEventsReachTheHostAfterAClick()
    {
      var session = Session("{a (link: \"go\")[  (set: $x to 1)  b   ] c}");
      var after = session.DispatchEvent(Regions(session.Render())[0]);
      foreach (var entry in after.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Text) Assert.NotEqual("", entry.Content);
      Assert.Equal("a b  c", Shown(after));
    }

    [Fact]
    public void AStringTargetStillMatchesTextAClickCollapsed()
    {
      var session = Session("{(link-repeat: \"go\")[ word  word ](click-rerun: \"word\")[!]}");
      var page = session.Render();
      page = session.DispatchEvent(Regions(page)[0]);
      page = session.DispatchEvent(Regions(page)[0]);
      Assert.Equal("go word word word word", Shown(page));
      Assert.Equal(2, Regions(page).Count);
      Assert.Equal("go word word word word!", Shown(session.DispatchEvent(Regions(page)[1])));
    }

    [Fact]
    public void ASessionWithNoSpansCollapsesNothing()
    {
      var session = Session("(link: \"a\")[  x  ]  y");
      Assert.Equal(new List<string> { "a  y", "  x    y" }, Play(session, "50@0"));
    }
  }
}
