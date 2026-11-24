using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary><c>(cond:)</c> and <c>(passages:)</c>. Expected text was rendered by the real 3.3.9 build.</summary>
  public class CondAndPassagesMacroTests
  {
    private static RenderResult Render(string body, params HarlowePassage[] others)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
      foreach (var p in others) story.AddPassage(p);
      story.StartNode = story.GetPassage("test").Pid;
      return new StorySession(story).Render();
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

    private static HarlowePassage P(string name, string body = "x", params string[] tags)
      => new HarlowePassage { Name = name, RawBody = body, Tags = new List<string>(tags) };

    [Theory]
    [InlineData("(print: (cond: false, 1, 2))", "2")]
    [InlineData("(print: (cond: false, 1, true, 2, 3))", "2")]
    [InlineData("(print: (cond: true, 1, 2))", "1")]
    [InlineData("(print: (cond: true, 1, 2, 3, 4))", "1")]
    [InlineData("(print: (cond: false, 1, true))", "true")]
    [InlineData("(set: $c to (cond: false, 1, \"z\"))$c", "z")]
    [InlineData("(cond: true, \"a\", \"b\")", "a")]
    [InlineData("(print: (cond: (a: 1) contains 1, \"y\", \"n\"))", "y")]
    [InlineData("(cond: true, (go-to: \"b\"), 1)", "B")]
    public void PicksTheFirstTrue(string source, string expected) => Assert.Equal(expected, Shown(Render(source)));

    [Theory]
    [InlineData("(print: (cond: true, 1))", null)]
    [InlineData("(print: (cond: false, 1))", null)]
    [InlineData("(print: (cond:))", null)]
    [InlineData("(print: (cond: false, 1, false, 2))", "An odd number of values must be given to (cond:), not 4")]
    [InlineData("(print: (cond: 1, 2, 3))", "The (cond:) macro's 1st value is the number 1, but should be a boolean.")]
    [InlineData("(print: (cond: false, 1, 2, 3))", "(cond:)'s 3rd value is the number 2, but should be a boolean.")]
    [InlineData("(print: (cond: true, ?a, 1))", "The (cond:) macro's 2nd value, the hook name ?a, is not valid data for this macro.")]
    [InlineData("(print: (cond: true, 1, \"a\" - 1))", null)]
    public void CondRefusals(string source, string message)
    {
      // Three values at least, as reference's [Boolean, Any, rest(Any)] signature
      // asks, and every value is evaluated, so an error anywhere is the result.
      var error = Render(source).Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(error);
      if (message != null) Assert.Equal(message, error.Content);
    }

    [Fact]
    public void CondChangerAttaches()
    {
      var result = Render("(cond: false, 1, (text-style: \"bold\"))[x]");
      Assert.Equal("x", Shown(result));
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.PushStyle && e.Style.Bold);
    }

    [Fact]
    public void PassagesAreInNaturalOrder()
    {
      Assert.Equal("a2,a10,b,test", Shown(Render("(print: (altered: via its name, ...(passages:)))", P("a10"), P("a2"))));
      Assert.Equal("A1,a 9,a 10,a-1,b,test", Shown(Render("(print: (altered: via its name, ...(passages:)))", P("a 10"), P("a 9"), P("a-1"), P("A1"))));
    }

    [Theory]
    [InlineData("(print: (passages: where its tags contains \"x\")'s length)", "2")]
    [InlineData("(print: (passages: where its name is \"c\")'s 1st's source)", "C")]
    [InlineData("(print: (altered: via its name, ...(passages: _p where _p's name contains \"c\")))", "c")]
    [InlineData("(print: (passages: each _p)'s length)", "4")]
    [InlineData("(print: (passages: where its name contains \"nope\")'s length)", "0")]
    [InlineData("(print: (passages:) is (passages:))", "true")]
    [InlineData("(print: (passages: where its name is \"b\")'s 1st is (passage: \"b\"))", "true")]
    [InlineData("(set: $p to (passages:))(set: $p's 1st's name to \"z\")(print: (passages:)'s 1st's name)(print: $p's 1st's name)", "bz")]
    [InlineData("(print: (passages: where its name is \"d\")'s 1st's tags)", "x,y")]
    public void PassagesDescribeEachPassage(string source, string expected)
      => Assert.Equal(expected, Shown(Render(source, P("c", "C", "x"), P("d", "D", "x", "y"))));

    [Fact]
    public void PassagesIncludeStartupAndHeaderPassages()
      => Assert.Equal("4", Shown(Render("(print: (passages:)'s length)", P("s", "(set: $q to 1)", "startup"), P("h", "H", "header"))).Replace("H", ""));

    [Theory]
    [InlineData("(print: (passages: via its name))", "The (passages:) macro's 1st value is a lambda, but should be (optional) a \"where ...\" lambda.")]
    [InlineData("(print: (passages: 1))", "The (passages:) macro's 1st value is the number 1, but should be (optional) a \"where ...\" lambda.")]
    [InlineData("(print: (passages: where 1))", "")]
    [InlineData("(print: (passages: where its name contains \"a\", 1))", "")]
    public void PassagesRefusals(string source, string message)
    {
      var error = Render(source).Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(error);
      if (message.Length > 0) Assert.Equal(message, error.Content);
    }

    [Fact]
    public void PassagesFilterInStoryOrderBeforeSorting()
    {
      // `pos` counts every passage in story order, as reference filters before it sorts.
      Assert.Equal("z", Shown(Render("(print: (altered: via its name, ...(passages: where pos is 3)))", P("z"), P("a"))));
    }
  }
}
