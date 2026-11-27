using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// <c>(ds:)</c> and the dataset value. Expected text was rendered by the real
  /// 3.3.9 build, except that two datasets holding the same values in a
  /// different order are equal, as its manual and 4.0 say. 3.3.9 calls them
  /// unequal, a bug 4.0 fixed.
  /// </summary>
  public class DatasetMacroTests
  {
    private static HarloweStory Story(params (string name, string body)[] passages)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      foreach (var (name, body) in passages)
        story.AddPassage(new HarlowePassage { Name = name, RawBody = body });
      story.StartNode = story.GetPassage(passages[0].name).Pid;
      return story;
    }

    private static RenderResult Render(string body) => new StorySession(Story(("Start", body))).Render();

    private static bool HasError(RenderResult result)
      => result.Entries.Exists(e => e.Kind == BufferedRenderOutput.Kind.Error);

    [Theory]
    [InlineData("(print: (ds:))", "")]
    [InlineData("(print: (ds: 3, 1, 2))", "3,1,2")]
    [InlineData("(print: (dataset: 1, \"a\"))", "1,a")]
    [InlineData("(print: (ds: 1, 1, 2))", "1,2")]
    [InlineData("(print: (ds: (a: 1), (a: 1), (a: 2)))", "1,2")]
    [InlineData("(print: (ds: (ds: 1), (ds: 1)))", "1")]
    [InlineData("(print: (ds: 0, -0))", "0")]
    [InlineData("(print: (ds: \"a\", \"A\"))", "a,A")]
    [InlineData("(print: (ds: ...(a: 3, 1, 2)))", "3,1,2")]
    [InlineData("(ds: 1, 2)", "1,2")]
    [InlineData("(ds: 1, 2)[x]", "1,2x")]
    [InlineData("(ds: 1) [x]", "1 x")]
    [InlineData("(set: $d to (ds: 2))$d[x]", "2x")]
    public void PrintsItsValuesOnceEachInTheOrderAdded(string source, string expected)
    {
      var result = Render(source);
      Assert.False(HasError(result));
      Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("(print: (a: ...(ds: 3, 1, 2)))", "1,2,3")]
    [InlineData("(print: (a: ...(ds: \"b\", \"A\", \"c\", \"10\", \"9\")))", "9,10,A,b,c")]
    [InlineData("(print: (a: ...(ds: 10, 9, 1)))", "1,9,10")]
    [InlineData("(print: (a: ...(ds: 10, \"9\", 1)))", "1,9,10")]
    [InlineData("(print: (a: ...(ds: \"item 10\", \"item 9\", \"Item 1\")))", "item 9,item 10,Item 1")]
    [InlineData("(print: (a: ...(ds: \"apple\", \"Banana\", \"cherry\", \"Apple\")))", "apple,Apple,Banana,cherry")]
    [InlineData("(print: (a: ...(ds: 1.5, -2, 0, 10)))", "-2,0,1.5,10")]
    [InlineData("(print: (a: ...(ds: true, 1, \"a\")))", "1,a,true")]
    [InlineData("(print: (a: ...(ds: (a: 2), (a: 1, 5))))", "1,5,2")]
    [InlineData("(for: each _x, ...(ds: 3, 1, 2))[_x ]", "1 2 3 ")]
    [InlineData("(print: (find: _x where _x > 1, ...(ds: 3, 1, 2)))", "2,3")]
    public void SpreadingSortsIt(string source, string expected)
    {
      var result = Render(source);
      Assert.False(HasError(result));
      Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("(ds: 1, 2) is (ds: 1, 2)", "true")]
    [InlineData("(ds: 1, 2) is (ds: 2, 1)", "true")]
    [InlineData("(ds: 1, 2) is not (ds: 2, 1)", "false")]
    [InlineData("(ds: 1, 2) is (ds: 1, 3)", "false")]
    [InlineData("(ds: 1) is (ds: 1, 1)", "true")]
    [InlineData("(ds: 1) is (a: 1)", "false")]
    [InlineData("(a: (ds: 1, 2)) is (a: (ds: 2, 1))", "true")]
    [InlineData("(dm: \"k\", (ds: 1, 2)) is (dm: \"k\", (ds: 2, 1))", "true")]
    [InlineData("(a: (ds: 1, 2)) contains (ds: 2, 1)", "true")]
    [InlineData("(ds: (ds: 1, 2), (ds: 2, 1))'s length", "1")]
    [InlineData("(ds: (a: 1, 2), (a: 2, 1))'s length", "2")]
    [InlineData("(ds: 1, 2) contains 1", "true")]
    [InlineData("(ds: 1, 2) contains 3", "false")]
    [InlineData("(ds: (a: 1)) contains (a: 1)", "true")]
    [InlineData("(ds: (ds: 99)) contains (ds: 99)", "true")]
    [InlineData("1 is in (ds: 1, 2)", "true")]
    [InlineData("3 is not in (ds: 1, 2)", "true")]
    [InlineData("(ds: 1) does not contain 2", "true")]
    [InlineData("(ds: 1, 2) + (ds: 2, 3)", "1,2,3")]
    [InlineData("(ds: 3, 1) + (ds: 2, 1)", "3,1,2")]
    [InlineData("(ds: 1, 2, 3) - (ds: 2)", "1,3")]
    [InlineData("(ds: 1, 2) - (ds: 1, 2)", "")]
    [InlineData("(ds: 1, 2)'s length", "2")]
    [InlineData("(ds:)'s length", "0")]
    [InlineData("(ds: 1, 2)'s (\"length\")", "2")]
    [InlineData("length of (ds: 1, 2)", "2")]
    [InlineData("(ds: 1, 2) is a dataset", "true")]
    [InlineData("(ds: 1) is an array", "false")]
    [InlineData("(ds:) is an empty", "true")]
    [InlineData("(ds: 1) is an empty", "false")]
    [InlineData("(datatype: (ds: 1))", "[the ds datatype]")]
    [InlineData("(datapattern: (ds: 1, \"a\"))", "[the ds datatype]")]
    public void Operators(string expression, string expected)
    {
      var result = Render($"(print: {expression})");
      Assert.False(HasError(result));
      Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("(ds: 2, 3) matches (ds: 3, num)", "true")]
    [InlineData("(ds: 3, num) matches (ds: 2, 3)", "true")]
    [InlineData("(ds: 2, 3) matches (ds: num, num)", "false")]
    [InlineData("(ds: 2, 3) does not match (ds: str, num)", "true")]
    [InlineData("(ds: array) matches (ds: (a:))", "true")]
    [InlineData("(ds: 2, 3) matches (a: 3, num)", "false")]
    [InlineData("(ds: 2) matches (ds: 2, 3)", "false")]
    [InlineData("(ds: 1, 2, 3) matches (ds: ...num)", "true")]
    [InlineData("(ds: 1, \"a\") matches (ds: ...num)", "false")]
    [InlineData("(ds: 1, 2) matches (ds: 1, ...num)", "true")]
    [InlineData("(ds: ...num) matches (ds: 1, 2)", "true")]
    [InlineData("(ds: 1, \"a\", 2) matches (ds: ...num, str)", "false")]
    [InlineData("(ds:) matches (ds:)", "true")]
    [InlineData("(ds:) matches (ds: ...num)", "false")]
    [InlineData("(ds: (a: 1), (a: \"x\")) matches (ds: (a: str), (a: num))", "true")]
    [InlineData("(ds: 1, 2, 3, 4, 5, 6, 7, 8, 9) matches (ds: 9, 8, 7, 6, 5, 4, 3, 2, 1)", "true")]
    public void Matches(string expression, string expected)
    {
      var result = Render($"(print: {expression})");
      Assert.False(HasError(result));
      Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void MatchingAPatternWithTooManyOrderingsIsAnErrorNotAHang()
    {
      var result = Render("(print: (ds: ...(range: 1, 30)) matches (ds: ...num, ...(range: 1, 29), \"x\"))");
      Assert.True(HasError(result));
    }

    [Fact]
    public void RepeatedHugeMatchesStopAtTheRenderBudget()
    {
      var result = Render(
        "(for: each _i, ...(range: 1, 2000))[(print: (ds: ...(range: 1, 30)) matches (ds: ...num, ...(range: 1, 29), \"x\"))]");
      Assert.True(HasError(result));
    }

    [Fact]
    public void LargeDatasetsOfCollectionsMatchWithoutTryingEveryPair()
    {
      var result = Render(
        "(set: $d to (ds: ...(altered: _i via (a: _i), ...(range: 1, 20000))))(print: $d matches $d)");
      Assert.False(HasError(result));
      Assert.Equal("true", result.Text);
    }

    [Theory]
    [InlineData("(print: (ds: 1, 2)'s 1st)")]
    [InlineData("(print: (ds: 1, 2)'s last)")]
    [InlineData("(print: (ds: 1, 2)'s (1))")]
    [InlineData("(print: (ds: 1, 2)'s \"a\")")]
    [InlineData("(print: (ds: 1, 2)'s random)")]
    [InlineData("(print: (ds: 1, 2) + (a: 3))")]
    [InlineData("(print: (ds: 1, 2) - 1)")]
    [InlineData("(print: (a: 3) + (ds: 1))")]
    [InlineData("(print: (ds: ?a))")]
    [InlineData("(print: (ds: num-type _a))")]
    [InlineData("(print: (str: (ds: 1)))")]
    [InlineData("(if: (ds: 1))[x]")]
    [InlineData("(set: $d to (ds: 1))(set: $d's 1st to 2)")]
    [InlineData("(set: $d to (ds: 1))(set: $d's length to 2)")]
    [InlineData("(set: $d to (ds: 1,2))(move: $d's 1st into $x)")]
    [InlineData("(set: str-type $s to (ds: 1))")]
    [InlineData("(set: ds-type $s to 1)")]
    public void Refusals(string source) => Assert.True(HasError(Render(source)));

    [Theory]
    [InlineData("(set: $d to (ds: 1,2))(move: $d's length into $x)$x,$d", "2,1,2")]
    [InlineData("(set: $a to (ds: 1, 2))(set: $b to $a)(set: $a to $a + (ds: 3))$b $a", "1,2 1,2,3")]
    [InlineData("(set: $d to (dm: \"k\", (ds: 1)))(print: $d's k contains 1)", "true")]
    [InlineData("(set: ds-type $s to (ds: 1))$s", "1")]
    [InlineData("(set: $m to (macro: ds-type _d, [(output-data: _d's length)]))($m: (ds: 1, 2))", "2")]
    public void WorksWithVariablesAndMacros(string source, string expected)
    {
      var result = Render(source);
      Assert.False(HasError(result));
      Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void PrintedStringsRenderAsMarkup()
    {
      var result = Render("(print: (ds: \"(print: 1)\", \"c\"))");
      Assert.False(HasError(result));
      Assert.Equal("1,c", result.Text);
    }

    [Fact]
    public void AVariableKeepsItsOrderAcrossTurnsAndUndo()
    {
      var session = new StorySession(Story(
        ("Start", "(set: $d to (ds: 3, 1, 2))"),
        ("Next", "$d (print: (a: ...$d))")));
      session.Render();
      Assert.Equal("3,1,2 1,2,3", session.Goto("Next").Text);
      Assert.True(session.Undo());
      session.Render();
      Assert.Equal("3,1,2 1,2,3", session.Goto("Next").Text);
    }

    [Fact]
    public void ASavedDatasetComesBackSorted()
    {
      // Reference's toSource sorts a dataset, so it prints sorted after a load.
      var storage = new InMemorySaveStorage();
      var story = Story(("Start", "(set: $d to (ds: 3, 1, 2))$d"), ("Next", "$d"));
      var session = new StorySession(story, storage);
      session.Render();
      session.Goto("Next");
      Assert.True(session.SaveGame("slot"));

      var loaded = new StorySession(story, storage);
      Assert.True(loaded.LoadGame("slot"));
      Assert.Equal("1,2,3", loaded.Render().Text);
    }

    [Fact]
    public void LargeDatasetsOfCollectionsStayFast()
    {
      var result = Render(
        "(set: $d to (ds: ...(altered: _i via (a: _i), ...(range: 1, 20000))))(print: $d's length)" +
        "(print: ($d - $d)'s length)(print: ($d + $d)'s length)");
      Assert.False(HasError(result));
      Assert.Equal("20000020000", result.Text);
    }

    [Fact]
    public void HostFactoryRemovesDuplicates()
    {
      var value = HarloweValue.OfDataset(new List<HarloweValue>
      {
        HarloweValue.OfNumber(1), HarloweValue.OfNumber(1), HarloweValue.OfString("1"),
      });
      Assert.Equal(2, value.AsDataset.Count);
    }
  }
}
