using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary><c>(range:)</c>. Expected text was rendered by the real 3.3.9 build.</summary>
  public class RangeMacroTests
  {
    private static RenderResult Render(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "Start", RawBody = body });
      story.StartNode = story.GetPassage("Start").Pid;
      return new StorySession(story).Render();
    }

    private static bool HasError(RenderResult result)
      => result.Entries.Exists(e => e.Kind == BufferedRenderOutput.Kind.Error);

    [Theory]
    [InlineData("(print: (range: 1, 5))", "1,2,3,4,5")]
    [InlineData("(print: (range: 5, 1))", "1,2,3,4,5")]
    [InlineData("(print: (range: 0, 0))", "0")]
    [InlineData("(print: (range: 2, -2))", "-2,-1,0,1,2")]
    [InlineData("(print: (range: -1, -3))", "-3,-2,-1")]
    [InlineData("(print: (range: 1e2, 102))", "100,101,102")]
    [InlineData("(print: (range: 1.0, 3))", "1,2,3")]
    [InlineData("(print: (range: 1e-7, 3))", "1e-7,1.0000001,2.0000001000000003,3.0000001000000003")]
    [InlineData("(print: (range: ...(a: 1, 3)))", "1,2,3")]
    [InlineData("(print: (a: ...(range: 1, 3), 9))", "1,2,3,9")]
    [InlineData("(for: each _i, ...(range: 1, 3))[_i ]", "1 2 3 ")]
    [InlineData("(print: (range: 1, 3)'s length)", "3")]
    [InlineData("(print: (range: 1, 3) is (a: 1, 2, 3))", "true")]
    [InlineData("(set: $a to (a: 1, 2, 3))(print: (range: 1, $a's length))", "1,2,3")]
    public void MakesAnInclusiveRun(string source, string expected)
    {
      var result = Render(source);
      Assert.False(HasError(result));
      Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("(range:)")]
    [InlineData("(range: 1)")]
    [InlineData("(range: 1, 2, 3)")]
    [InlineData("(range: 1.5, 3)")]
    [InlineData("(range: 1, 2.5)")]
    [InlineData("(range: 1.5e-7, 3)")]
    [InlineData("(range: \"1\", 3)")]
    [InlineData("(range: true, 3)")]
    [InlineData("(range: (a: 1), 3)")]
    public void RefusesAnythingButTwoWholeNumbers(string source)
      => Assert.True(HasError(Render(source)));

    [Theory]
    [InlineData("(range: 1, 1e9)")]
    [InlineData("(range: 1, 1e21)")]
    [InlineData("(range: -1e300, 1e300)")]
    public void AnEnormousRangeIsAnErrorInsteadOfExhaustingMemory(string source)
      => Assert.True(HasError(Render(source)));

    [Fact]
    public void TheLongestRangeAllowedStillWorks()
    {
      var result = Render($"(print: (range: 1, {RangeMacroLimit})'s length)");
      Assert.False(HasError(result));
      Assert.Equal(RangeMacroLimit.ToString(), result.Text);
    }

    private const int RangeMacroLimit = 200000;
  }
}
