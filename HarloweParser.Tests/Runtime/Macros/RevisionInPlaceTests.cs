using System.Collections.Generic;
using System.Text;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Revisions render their content into each target and then run it, as
  /// reference does. Every expected page here was rendered by real 3.3.9 on a
  /// fake clock whose animation frames come every 16 ms: inner revisions
  /// landing on top, a rendering per target, earlier targets only while the
  /// passage renders, changers dropped in content a revision emptied, hooks
  /// revised while they're written, clicks and live hooks.
  /// </summary>
  public class RevisionInPlaceTests
  {
    /// <summary>
    /// Renders <paramref name="source"/>, then plays <paramref name="steps"/>:
    /// each is milliseconds to let pass in 16 ms frames, and <c>ms@n</c> first
    /// clicks the nth armed region. Returns the page after the last step, with
    /// each error as a bracketed "error", as the recordings mark it.
    /// </summary>
    private static string Play(string source, string steps)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = source });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B(print: time)" });
      story.AddPassage(new HarlowePassage { Name = "c", RawBody = "(live: 100ms)[c(stop:)]" });
      story.StartNode = story.GetPassage("test").Pid;
      var session = new StorySession(story, 42);
      var current = session.Render();
      double now = 0, nextFrame = 16;
      foreach (var step in steps.Split(','))
      {
        var parts = step.Split('@');
        if (parts.Length > 1)
        {
          var regions = Regions(current);
          int n = int.Parse(parts[1]);
          if (n < regions.Count) current = session.DispatchEvent(regions[n]);
        }
        double until = now + double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        while (nextFrame <= until)
        {
          var ticked = session.Tick(16);
          if (ticked != null) current = ticked;
          nextFrame += 16;
        }
        now = until;
      }
      var text = new StringBuilder();
      foreach (var entry in current.Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.Text || entry.Kind == BufferedRenderOutput.Kind.Link) text.Append(entry.Content);
        else if (entry.Kind == BufferedRenderOutput.Kind.Error) text.Append("\u27e6error\u27e7");
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

    [Theory]
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y]z]", @"20", @"y")] // f21#0
    [InlineData(@"|a>[x](append: ?a)[(replace: ?a)[y]z]", @"20", @"y")] // f21#1
    [InlineData(@"|a>[x](replace: ?a)[w(append: ?a)[y]z]", @"20", @"wzy")] // f21#2
    [InlineData(@"|a>[x](prepend: ?a)[w(append: ?a)[y]z]", @"20", @"wzxy")] // f21#3
    [InlineData(@"|a>[x](replace: ?a)[w(prepend: ?a)[y]z]", @"20", @"ywz")] // f21#4
    [InlineData(@"(set: $m to (macro: [(replace: ?a)[y](output-data: [])]))|a>[x](replace: ?a)[($m:)]", @"20", @"y")] // f21#5
    [InlineData(@"(set: $m to (macro: [(replace: ?a)[y](output-data: [])]))|a>[x](replace: ?a)[($m:)z]", @"20", @"y")] // f21#6
    [InlineData(@"|a>[x]|b>[q](replace: ?a)[(replace: ?b)[y]z]", @"20", @"zq")] // f21#7
    [InlineData(@"|a>[x](replace: ?a)[(set: $n to 1)z]$n", @"20", @"z1")] // f21#8
    [InlineData(@"|a>[x](replace: ?a)[|c>[w](replace: ?c)[v]]", @"20", @"v")] // f21#9
    [InlineData(@"|a>[x](replace: ?a)[(hide: ?a)z]", @"20", @"")] // f21#10
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y](replace: ?a)[u]z]", @"20", @"y")] // f21#11
    [InlineData(@"|a>[x](replace: ?a) + (text-style: 'bold')[(replace: ?a)[y]z]", @"20", @"y")] // f21#12
    [InlineData(@"|a>[x](replace: ""x"")[(replace: ?a)[y]z]", @"20", @"y")] // f21#13
    [InlineData(@"|a>[x]|a>[x2](replace: ?a)[(replace: ?a)[y]z]", @"20", @"yy")] // f21#14
    [InlineData(@"|a>[x](replace: ?a)[(print: ?a's length)]", @"20", @"⟦error⟧")] // f21#15
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y]]w", @"20", @"yw")] // f21#16
    [InlineData(@"(set: $n to 0)|a>[x]|a>[y](replace: ?a)[(set: $n to it + 1)$n]", @"20", @"12")] // f22#0
    [InlineData(@"(set: $n to 0)|a>[x]|a>[y](replace: ?a)[(set: $n to it + 1)]$n", @"20", @"2")] // f22#1
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[(link: 'go')[z]]", @"20,20@0", @"zgo")] // f22#2
    [InlineData(@"(link: 'go')[|c>[w](replace: ?c)[v]]", @"20,20@0", @"v")] // f22#3
    [InlineData(@"|a>[x](link: 'go')[(replace: ?a)[(replace: ?a)[y]z]]", @"20,20@0", @"y")] // f22#4
    [InlineData(@"|a>[x]|b>[q](link: 'go')[(replace: ?a)[(replace: ?b)[y]z]]", @"20,20@0", @"zy")] // f22#5
    [InlineData(@"|h)[(replace: ?b)[y]]|b>[q](show: ?h)", @"20", @"q")] // f22#6
    [InlineData(@"|b>[q]|h)[(replace: ?b)[y]](show: ?h)", @"20", @"y")] // f22#7
    [InlineData(@"|a>[x](live: 100ms)[(replace: ?a)[(replace: ?a)[y]z](stop:)]", @"150", @"y")] // f22#8
    [InlineData(@"|a>[x(replace: ?a)[y]z]", @"20", @"y")] // f22#9
    [InlineData(@"|a>[x(append: ?a)[y]z]", @"20", @"xzy")] // f22#10
    [InlineData(@"|a>[x(prepend: ?a)[y]z]", @"20", @"yxz")] // f22#11
    [InlineData(@"(replace: ?passage)[x]y", @"20", @"x")] // f22#12
    [InlineData(@"w(append: ?passage)[x]y", @"20", @"wyx")] // f22#13
    [InlineData(@"|a>[x](replace: ?a)[(for: each _i, 1, 2)[(append: ?a)[_i]]z]", @"20", @"z12")] // f22#14
    [InlineData(@"|a>[|c>[q]](prepend: ?a)[(replace: ?c)[y]]", @"20", @"q")] // f22#15
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y](set: $n to 5)]$n", @"20", @"y5")] // f22#16
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y](click: ?a)[u]]", @"20,20@0", @"y")] // f22#17
    [InlineData(@"|a>[x]|a>[x](replace: 'x')[(replace: ?a)[y]z]", @"20", @"yy")] // f22#18
    [InlineData(@"{|a>[x]}(replace: ?a)[ p   (replace: ?a)[ y  q ] ]", @"20", @" y  q ")] // f22#19
    [InlineData(@"(set: $m to (macro: [(replace: ?a)[(replace: ?a)[y]z](output-data: [])]))|a>[x]($m:)", @"20", @"y")] // f22#20
    [InlineData(@"|a>[x](replace: ?a)[(display: 'b')z]", @"20", @"B0z")] // f22#21
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[(click: ?b)[u]]|b>[B]", @"20,20@0", @"uB")] // f22#22
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[(append: ?a)[q]]", @"20", @"qqq")] // f23#0
    [InlineData(@"|a>[x]|a>[y](append: ?a)[(append: ?a)[q]]", @"20", @"xqqyq")] // f23#1
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[(replace: ?a)[q]z]", @"20", @"qq")] // f23#2
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[(print: ?a's 2nd's length)]", @"20", @"⟦error⟧⟦error⟧")] // f23#3
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[p(replace: 'p')[r]]", @"20", @"rr")] // f23#4
    [InlineData(@"|a>[x]|a>[y](link: 'go')[(replace: ?a)[(append: ?a)[q]]]", @"20,20@0", @"qqqq")] // f24#0
    [InlineData(@"|a>[x]|a>[y](link: 'go')[(replace: ?a)[p(replace: 'p')[r]]]", @"20,20@0", @"rr")] // f24#1
    [InlineData(@"|a>[x]|a>[y](link: 'go')[(replace: ?a)[(print: ?a's 2nd's length)]]", @"20,20@0", @"⟦error⟧⟦error⟧")] // f24#2
    [InlineData(@"|a>[x|b>[p(replace: ?a)[y]q]z]", @"20", @"y")] // f25#0
    [InlineData(@"|a>[x|b>[p(append: ?a)[y]q]z]", @"20", @"xpqzy")] // f25#1
    [InlineData(@"(set: $m to (macro: [(replace: ?b)[y](output-data: [])]))|a>[x]|b>[q](replace: ?a)[($m:)]", @"20", @"y")] // f25#2
    [InlineData(@"(set: $m to (macro: [(replace: ""q"")[y](output-data: [])]))|a>[x]|b>[q](replace: ?a)[($m:)]", @"20", @"q")] // f25#3
    [InlineData(@"|b>[q](set: $m to (macro: [(replace: ?b)[y](output-data: [])]))|a>[x](replace: ?a)[($m:)]", @"20", @"y")] // f25#4
    [InlineData(@"|a>[x](for: each _i, 1, 2)+(replace: ?a)[_i]", @"20", @"2")] // f25#5
    [InlineData(@"|a>[x](for: each _i, 1, 2)+(append: ?a)[_i]", @"20", @"x12")] // f25#6
    [InlineData(@"|a>[x](link: 'go')[(for: each _i, 1, 2)+(append: ?a)[_i]]", @"20,20@0", @"x12")] // f25#7
    [InlineData(@"(set: $n to 0)|a>[(live: 20ms)[(set: $n to it + 1)](replace: ?a)[y]](live: 100ms)[$n(stop:)]", @"150", @"y3")] // f25#8
    [InlineData(@"(click-replace: ""x"")[y]x", @"20,20@0", @"y")] // f25#9
    [InlineData(@"|a>[x(append: ?a)[(append: ?a)[y]w]z]", @"20", @"xzwy")] // f25#10
    [InlineData(@"|a>[x(replace: ?a)[(append: ?a)[y]w]z]", @"20", @"wy")] // f25#11
    [InlineData(@"|b>[q](set: $m to (macro: [(replace: ""q"")[y](output-data: [])]))|a>[x](replace: ?a)[($m:)]", @"20", @"q")] // f26#0
    [InlineData(@"(set: $m to (macro: [(replace: ""q"")[y](output-data: [])]))|b>[q]($m:)", @"20", @"q")] // f26#1
    [InlineData(@"(set: $m to (macro: [(replace: ?b)[y](output-data: [])]))|b>[q]($m:)", @"20", @"y")] // f26#2
    [InlineData(@"(set: $m to (macro: [(replace: ""q"")[y](output-data: [])]))|a>[x]|b>[q](link: 'go')[($m:)]", @"20,20@0", @"xy")] // f26#3
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[|c>[w](append: ?c)[q]]", @"20", @"wqqwq")] // f26#6
    [InlineData(@"(set: $n to 0)|a>[x](replace: ?a, ""x"")[(set: $n to it + 1)]$n", @"20", @"2")] // f26#7
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[(replace: ?a's last)[q]]", @"20", @"q")] // f27#0
    [InlineData(@"|a>[x]|a>[y](replace: ?a)[(replace: ?a's 1st)[q]]", @"20", @"q")] // f27#1
    [InlineData(@"|a>[x]|a>[y]|a>[z](replace: ?a's 2nd)[(append: ?a's last)[q]]", @"20", @"xz")] // f27#2
    [InlineData(@"(set: $n to 0)(link-rerun: 'go')[(set: $n to it + 1)$n]", @"20,20@0,20@0", @"go2")] // f27#3
    [InlineData(@"|a>[x(hide: ?a)y](show: ?a)", @"20", @"xy")] // f31#3
    [InlineData(@"|a>[x(hide: ?a)y]", @"20", @"")] // f31#4
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y]''(click: ?a)[u]'']", @"20,20@0", @"y")] // f32#0
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y](if: true)[(click: ?a)[u]]]", @"20,20@0", @"y")] // f32#1
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y]|c>[(click: ?a)[u]]]", @"20,20@0", @"y")] // f32#2
    [InlineData(@"|a>[x](replace: ?a)[(replace: ?a)[y]{(click: ?a)[u]}]", @"20,20@0", @"y")] // f32#3
    public void MatchesReference(string source, string steps, string expected)
      => Assert.Equal(expected, Play(source, steps));

    /// <summary>
    /// The differences that stay, with what real 3.3.9 shows: a (for:) with a
    /// click, a later copy's content during a click, a revision whose targets
    /// overlap, and a hook that hides itself or is copied while hidden.
    /// </summary>
    [Theory]
    [InlineData(@"|a>[x](for: each _i, 1, 2)+(click: ?a)[_i]", @"20,20@0", @"x⟦error⟧", @"x12")] // f26#4
    [InlineData(@"|a>[x]|a>[y](link: 'go')[(replace: ?a)[|c>[w](append: ?c)[q]]]", @"20,20@0", @"wqqwq", @"wqqqwq")] // f26#5
    [InlineData(@"(set: $n to 0)|a>[x](replace: ""x"", ?a)[(set: $n to it + 1)]$n", @"20", @"2", @"1")] // f26#8
    [InlineData(@"|a>[x(hide: ?a)(replace: ""x"")[y]](show: ?a)", @"20", @"y", @"xy")] // f31#0
    [InlineData(@"|o>[]|q>[](append: ?o, ?q)[|a>[x](hide: ?a)](show: ?a's 1st)", @"20", @"x", @"xx")] // f31#1
    [InlineData(@"|o>[]|q>[](append: ?o, ?q)[|a>[x](hide: ?a)](show: ?a)", @"20", @"xx", @"x")] // f31#2
    public void KnownDifference(string source, string steps, string expected, string reference)
    {
      Assert.NotEqual(reference, expected);
      Assert.Equal(expected, Play(source, steps));
    }
  }
}