using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary><c>(passage:)</c>. Expected text was rendered by the real 3.3.9 build.</summary>
  public class PassageMacroTests
  {
    private static RenderResult Render(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
      story.AddPassage(new HarlowePassage { Name = "t", RawBody = "T", Tags = new List<string> { "alpha", "beta" } });
      story.AddPassage(new HarlowePassage { Name = "shows", RawBody = "(print: (passage:)'s name)" });
      story.StartNode = story.GetPassage("test").Pid;
      return new StorySession(story).Render();
    }

    private static bool HasError(RenderResult result)
      => result.Entries.Exists(e => e.Kind == BufferedRenderOutput.Kind.Error);

    [Theory]
    [InlineData("(print: (passage:)'s name)", "test")]
    [InlineData("(print: (passage: \"\")'s name)", "test")]
    [InlineData("(print: (passage: \"b\")'s name)", "b")]
    [InlineData("(print: (passage: \"b\")'s source)", "B")]
    [InlineData("(print: (passage: \"t\")'s tags)", "alpha,beta")]
    [InlineData("(print: (passage: \"t\")'s tags contains \"alpha\")", "true")]
    [InlineData("(print: (passage: \"b\")'s tags's length)", "1")]
    [InlineData("(print: (passage: \"b\")'s tags's 1st is \"\")", "true")]
    [InlineData("(print: (passage: \"b\") is a dm)", "true")]
    [InlineData("(print: (passage: \"b\") contains \"source\")", "true")]
    [InlineData("(print: (passage: \"b\") contains \"storylet\")", "false")]
    [InlineData("(print: (passage: \"b\") is (passage: \"b\"))", "true")]
    [InlineData("(print: (passage: \"b\") is (passage:))", "false")]
    [InlineData("(print: (passage: \"b\") matches (dm: \"name\", str, \"source\", str, \"tags\", array))", "true")]
    [InlineData("(if: (passage:)'s tags contains \"x\")[y](else:)[n]", "n")]
    [InlineData("(set: $p to (passage: \"b\"))(set: $p's name to \"c\")(print: $p's name + (passage: \"b\")'s name)", "cb")]
    [InlineData("(display: \"shows\")", "test")]
    public void DescribesAPassage(string source, string expected)
    {
      var result = Render(source);
      Assert.False(HasError(result));
      Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("(print: (passage: \"nope\"))")]
    [InlineData("(print: (passage: \"B\"))")]
    [InlineData("(print: (passage: \" b\"))")]
    [InlineData("(print: (passage: 1))")]
    [InlineData("(print: (passage: \"b\", \"b\"))")]
    [InlineData("(print: (passage: \"b\")'s nope)")]
    [InlineData("(print: passage)")]
    [InlineData("(print: passage's name)")]
    [InlineData("(print: (passage:)'s name is passage)")]
    [InlineData("(set: $x to passage)")]
    public void Refusals(string source) => Assert.True(HasError(Render(source)));

    [Fact]
    public void WithoutAStoryItReportsThePassageIsMissing()
    {
      var registry = new MacroRegistry();
      Harlowe.Runtime.Macros.StandardMacros.RegisterAll(registry);
      var context = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      var result = registry.Invoke("passage", new List<HarloweValue> { HarloweValue.OfString("x") }, context);
      Assert.True(result.IsError);
    }

    [Fact]
    public void PrintingItsOwnSourceIsStoppedByTheNestingLimit()
    {
      var result = Render("ab(print: (passage:)'s source)cd");
      Assert.True(HasError(result));
      Assert.StartsWith("abab", result.Text);
    }
  }
}
