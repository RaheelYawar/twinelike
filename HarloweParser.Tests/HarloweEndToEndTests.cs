using System;
using Xunit;

namespace Harlowe.Tests
{
  public class HarloweEndToEndTests
  {
    [Fact]
    public void Constructor_LoadsTestFileWithoutThrowing()
    {
      var story = TestFixture.LoadTestFile();
      Assert.NotNull(story);
    }

    [Fact]
    public void Constructor_ThrowsOnHtmlMissingTwStorydata()
    {
      var ex = Assert.Throws<Exception>(() => new Harlowe("<html><body>no story here</body></html>"));
      Assert.Contains("tw-storydata", ex.Message);
    }

    [Fact]
    public void PassageCount_MatchesFixture()
    {
      var story = TestFixture.LoadTestFile();
      Assert.Equal(8, story.PassageCount);
    }

    [Fact]
    public void StartNode_MatchesFixtureMetadata()
    {
      var story = TestFixture.LoadTestFile();
      Assert.Equal("1", story.StartNode);
    }

    [Theory]
    [InlineData("Disclaimer")]
    [InlineData("FirstPassage")]
    [InlineData("FirstPassage - Yes")]
    [InlineData("FirstPassage - Agree")]
    [InlineData("FirstPassage - Nod")]
    [InlineData("Rucksack")]
    [InlineData("Rucksack - Give Away")]
    [InlineData("Rucksack - Dont Give Away")]
    public void GetPassage_ReturnsKnownPassageByName(string name)
    {
      var story = TestFixture.LoadTestFile();
      var passage = story.GetPassage(name);

      Assert.NotNull(passage);
      Assert.Equal(name, passage.Name);
    }

    [Fact]
    public void GetPassage_ReturnsNullForUnknownName()
    {
      var story = TestFixture.LoadTestFile();
      Assert.Null(story.GetPassage("NoSuchPassage"));
    }

    [Fact]
    public void GetPassageBody_ReturnsEmptyStringForUnknownName()
    {
      var story = TestFixture.LoadTestFile();
      Assert.Equal(string.Empty, story.GetPassageBody("NoSuchPassage"));
    }

    [Fact]
    public void GetPassageBranches_ReturnsNullForUnknownName()
    {
      var story = TestFixture.LoadTestFile();
      Assert.Null(story.GetPassageBranches("NoSuchPassage"));
    }

    [Fact]
    public void Disclaimer_HasContinueBranchToFirstPassage()
    {
      var story = TestFixture.LoadTestFile();
      var branches = story.GetPassageBranches("Disclaimer");

      Assert.NotNull(branches);
      Assert.Contains(branches, b => b.Text == "Continue" && b.Name == "FirstPassage");
    }

    [Fact]
    public void FirstPassage_HasMultipleAffirmativeResponseBranches()
    {
      var story = TestFixture.LoadTestFile();
      var branches = story.GetPassageBranches("FirstPassage");

      Assert.NotNull(branches);
      Assert.Contains(branches, b => b.Name == "FirstPassage - Yes");
      Assert.Contains(branches, b => b.Name == "FirstPassage - Agree");
      Assert.Contains(branches, b => b.Name == "FirstPassage - Nod");
    }

    [Fact]
    public void Rucksack_HasGiveAwayAndDismissBranches()
    {
      var story = TestFixture.LoadTestFile();
      var branches = story.GetPassageBranches("Rucksack");

      Assert.NotNull(branches);
      Assert.Contains(branches, b => b.Name == "Rucksack - Give Away");
      Assert.Contains(branches, b => b.Name == "Rucksack - Dont Give Away");
    }

    [Fact]
    public void GetPassage_ExposesPidAndName()
    {
      var story = TestFixture.LoadTestFile();
      var disclaimer = story.GetPassage("Disclaimer");

      Assert.Equal("1", disclaimer.Pid);
      Assert.Equal("Disclaimer", disclaimer.Name);
    }

    [Fact]
    public void GetPassageBody_ReturnsNonEmptyForKnownPassage()
    {
      var story = TestFixture.LoadTestFile();
      var body = story.GetPassageBody("Disclaimer");

      Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact]
    public void GetPassageBody_DecodesApostropheEntity()
    {
      var story = TestFixture.LoadTestFile();
      var body = story.GetPassageBody("Rucksack - Dont Give Away");

      Assert.DoesNotContain("&#39;", body);
    }

    [Fact]
    public void Branches_AreStrippedFromPassageBody()
    {
      var story = TestFixture.LoadTestFile();
      var body = story.GetPassageBody("Disclaimer");

      Assert.DoesNotContain("[[", body);
      Assert.DoesNotContain("]]", body);
    }
  }
}
