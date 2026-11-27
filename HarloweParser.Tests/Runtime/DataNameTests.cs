using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>Datamap names that are numbers. Expected text was rendered by the real 3.3.9 build.</summary>
  public class DataNameTests
  {
    private static string Shown(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.StartNode = story.GetPassage("test").Pid;
      var result = new StorySession(story).Render();
      var text = new System.Text.StringBuilder();
      foreach (var entry in result.Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.Text) text.Append(entry.Content);
        else if (entry.Kind == BufferedRenderOutput.Kind.Error) text.Append("!");
      }
      return text.ToString();
    }

    [Fact]
    public void NumbersAndStringsAreDifferentNames()
    {
      Assert.NotEqual(DataName.Of(1), DataName.Of("1"));
      Assert.Equal(DataName.Of(0), DataName.Of(-0.0));
      Assert.Equal(DataName.Of(0).GetHashCode(), DataName.Of(-0.0).GetHashCode());
      Assert.Equal(DataName.Of("a"), (DataName)"a");
      Assert.True(DataName.Of(2).IsNumber);
      Assert.Equal("1.5", DataName.Of(1.5).Text);
    }

    [Fact]
    public void ADefaultNameIsTheEmptyString()
    {
      var name = default(DataName);
      Assert.False(name.IsNumber);
      Assert.Equal(DataName.Of(""), name);
    }

    [Theory]
    [InlineData("(print: (dm: 1, \"a\")'s (1))", "a")]
    [InlineData("(print: (dm: 1, \"a\")'s 1)", "a")]
    [InlineData("(set: $m to (dm: 1, \"a\", 2, \"b\"))(print: $m's (1 + 1))", "b")]
    [InlineData("(print: (dm: 1.5, \"a\")'s (1.5))", "a")]
    [InlineData("(print: (dm: -1, \"a\")'s (-1))", "a")]
    [InlineData("(print: (dm: 0, \"a\")'s (-0))", "a")]
    [InlineData("(print: (dm: 1e3, \"a\")'s (1000))", "a")]
    [InlineData("(print: (dm: 1, \"a\") contains 1)", "true")]
    [InlineData("(print: (dm: 1, \"a\") contains \"1\")", "false")]
    [InlineData("(print: (dm: \"1\", \"a\") contains 1)", "false")]
    [InlineData("(print: 1 is in (dm: 1, \"a\"))", "true")]
    [InlineData("(print: (dm: 1, \"a\") is (dm: 1, \"a\"))", "true")]
    [InlineData("(print: (dm: 1, \"a\") is (dm: \"1\", \"a\"))", "false")]
    [InlineData("(print: (dm: 1, \"a\", 2, \"b\") is (dm: 2, \"b\", 1, \"a\"))", "true")]
    [InlineData("(print: ((dm: 1, \"a\") + (dm: 2, \"b\"))'s (2))", "b")]
    [InlineData("(set: $m to (dm: 1, (dm: \"name\", \"B\")))(print: $m's (1)'s name)", "B")]
    [InlineData("(set: $t to (a: (dm: \"id\", 25)))(set: $mons to (dm: 25, (dm: \"name\", \"Pika\")))(print: $mons's ($t's 1st's id)'s name)", "Pika")]
    [InlineData("(print: (dm: 1, \"a\") matches (dm: 1, str))", "true")]
    [InlineData("(print: (dm: 1, \"a\") matches (dm: \"1\", str))", "false")]
    [InlineData("(print: (ds: (dm: 1, \"a\")) contains (dm: 1, \"a\"))", "true")]
    [InlineData("(unpack: (dm: 1, \"a\") into (dm: 1, _x))_x", "a")]
    [InlineData("(set: datamap-type $m to (dm: 1, 2))(print: $m's (1))", "2")]
    [InlineData("(print: (1) of (dm: 1, \"a\"))", "a")]
    [InlineData("(print: (dm: (2), \"a\")'s (2))", "a")]
    [InlineData("(print: (dm: 1, 2)'s (1) + 1)", "3")]
    public void Reads(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Theory]
    [InlineData("(set: $m to (dm: 1, \"a\"))(set: $m's (2) to \"b\")(print: $m contains 2)", "!false")]
    [InlineData("(set: $m to (dm:))(set: $m's (1) to \"x\")(print: $m contains 1)", "!false")]
    [InlineData("(set: $m to (dm: 1, \"a\"))(set: $m's (1) to it + \"b\")(print: $m's (1))", "!a")]
    [InlineData("(set: $m to (dm: 1, \"a\"))(set: $m's 1 to \"b\")(print: $m's 1)", "!a")]
    [InlineData("(set: $m to (dm: 1, \"a\"))(set: $n to $m)(set: $n's (1) to \"b\")(print: $m's (1))", "!a")]
    [InlineData("(set: $m to (dm: 1, \"a\"))(set: $m's \"1\" to \"b\")(print: $m contains \"1\")", "!false")]
    public void CantBeWrittenThrough(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Fact]
    public void MovingOneOutCopiesWithoutDeleting()
      => Assert.Equal("atrue", Shown("(set: $m to (dm: 1, \"a\"))(move: $m's (1) into $x)$x(print: $m contains 1)"));

    [Fact]
    public void MovingThroughOneDeletesNothing()
      => Assert.Equal("btrue", Shown("(set: $m to (dm: 1, (dm: \"n\", \"b\")))(move: $m's (1)'s n into $x)$x(print: $m's (1) contains \"n\")"));

    [Theory]
    [InlineData("(print: (dm: 1, \"a\", \"1\", \"b\"))")]
    [InlineData("(print: (dm: \"1\", \"a\", 1, \"b\"))")]
    [InlineData("(print: (dm: 1, \"a\", \"01\", \"b\"))")]
    [InlineData("(print: (dm: true, \"a\"))")]
    [InlineData("(print: (dm: 1, \"a\", 1, \"b\"))")]
    [InlineData("(print: (dm: 1, \"a\") matches (dm: num, str))")]
    [InlineData("(set: $m to (dm: 1, \"a\"))(print: $m's \"1\")")]
    [InlineData("(set: $m to (dm: \"1\", \"a\"))(print: $m's (1))")]
    [InlineData("(set: $m to (dm: 1, \"a\"))(print: $m's (2))")]
    [InlineData("(print: (dm: 1, \"a\")'s 1st)")]
    [InlineData("(print: (dm: 1, \"a\")'s (true))")]
    public void Refusals(string source) => Assert.Equal("!", Shown(source));

    [Fact]
    public void ANumberOnlyClashesWithItsPrintedForm()
    {
      // Reference checks a new number against its printed form, "1", so "01"
      // can come first. Reading "01" back then finds the number it reads as.
      Assert.Equal("b", Shown("(set: $m to (dm: \"01\", \"a\", 1, \"b\"))(print: $m's (1))"));
      Assert.Equal("!", Shown("(set: $m to (dm: \"01\", \"a\", 1, \"b\"))(print: $m's \"01\")"));
    }

    [Theory]
    [InlineData("(print: (num: \"0x11\"))", "17")]
    [InlineData("(print: (num: \"0o17\"))", "15")]
    [InlineData("(print: (num: \"0B101\"))", "5")]
    [InlineData("(print: (num: \"-0x11\"))", "!")]
    [InlineData("(print: (num: \"0x\"))", "!")]
    [InlineData("(print: (num: \"0x1g\"))", "!")]
    [InlineData("(print: (dm: 16, 'a', '0x10', 'b'))", "!")]
    public void HexStringsAreNumbersAsInJavaScript(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Fact]
    public void AddingDatamapsDoesntCheckNames()
    {
      var map = new ExpressionEvaluatorHarness().Eval("(dm: 1, \"a\") + (dm: \"1\", \"b\")").AsDatamap;
      Assert.Equal(2, map.Count);
      Assert.Equal("a", map[DataName.Of(1)].AsString);
      Assert.Equal("b", map["1"].AsString);
    }

    [Fact]
    public void SavesAsNumbers()
    {
      var value = HarloweValue.OfDatamap(new Dictionary<DataName, HarloweValue>
      {
        ["b"] = HarloweValue.OfNumber(2),
        [DataName.Of(10)] = HarloweValue.OfString("j"),
        [DataName.Of(2)] = HarloweValue.OfString("t"),
      });
      Assert.Equal("(dm:2,\"t\",10,\"j\",\"b\",2)", value.ToSource());
    }

    /// <summary>Evaluates an expression with the standard macros.</summary>
    private sealed class ExpressionEvaluatorHarness
    {
      public HarloweValue Eval(string expression)
      {
        var story = new HarloweStory { FormatVersion = "3.3.9" };
        story.AddPassage(new HarlowePassage { Name = "test", RawBody = "(set: $v to " + expression + ")" });
        story.StartNode = story.GetPassage("test").Pid;
        var session = new StorySession(story);
        session.Render();
        session.TryGetVariable("v", out var value);
        return value;
      }
    }
  }
}
