using System;
using Harlowe.Ast.Body;
using Harlowe.Runtime;
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
      var ex = Assert.Throws<HarloweParseException>(() => new Harlowe("<html><body>no story here</body></html>"));
      Assert.Contains("tw-storydata", ex.Message);
    }

    [Fact]
    public void Constructor_TopLevelError_HasNoLocationFields()
    {
      // Errors that fire before any passage is entered leave Line/Column at -1
      // and PassageName null.
      var ex = Assert.Throws<HarloweParseException>(() => new Harlowe("<html><body>nope</body></html>"));
      Assert.Equal(-1, ex.Line);
      Assert.Equal(-1, ex.Column);
      Assert.Null(ex.PassageName);
    }

    [Fact]
    public void Constructor_PassageParseError_RecoversWithSyntheticAst()
    {
      // (set: $x to ) leaves the right-hand side of `to` empty; the expression
      // parser hits a MacroClose where it expected an operand. The HTML
      // loader recovers per-passage: the rest of the story is loadable, and
      // rendering the broken passage emits an in-prose error rather than
      // throwing out of the constructor.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"Bad\">(set: $x to )</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"Good\">hello</tw-passagedata>"
        + "</tw-storydata></body></html>";

      var story = new Harlowe(html);
      Assert.Equal(2, story.PassageCount);
      // Good passage is unaffected.
      var good = story.GetPassage("Good");
      Assert.NotNull(good);
      // Rendering the bad passage produces an Error entry, not an exception.
      var session = new StorySession(story);
      var result = session.Goto("Bad");
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                        && e.Content.Contains("parse error")
                                        && e.Content.Contains("Bad"));
    }

    [Fact]
    public void Constructor_EmptyStorydata_LoadsAsZeroPassageStory()
    {
      // A <tw-storydata> with no <tw-passagedata> children is structurally
      // valid — same shape as `new Harlowe()`. SelectNodes returns null in
      // that case; the loader must treat it as zero passages, not NRE.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"0\"></tw-storydata></body></html>";
      var story = new Harlowe(html);
      Assert.Equal(0, story.PassageCount);
    }

    [Fact]
    public void Constructor_PassageMissingNameAttribute_ThrowsHarloweParseException()
    {
      // Missing 'name' was dereferenced directly before; verify it now
      // surfaces through the project's error contract instead of NRE'ing.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\">body</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var ex = Assert.Throws<HarloweParseException>(() => new Harlowe(html));
      Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Constructor_PassageMissingPidAttribute_ThrowsHarloweParseException()
    {
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata name=\"P1\">body</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var ex = Assert.Throws<HarloweParseException>(() => new Harlowe(html));
      Assert.Equal("P1", ex.PassageName);
      Assert.Contains("pid", ex.Message);
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

    [Fact]
    public void Passage_AstIsPopulatedByNewPipeline()
    {
      var story = TestFixture.LoadTestFile();
      var passage = story.GetPassage("Disclaimer");

      Assert.NotNull(passage.Ast);
      Assert.NotNull(passage.Ast.Children);
      Assert.NotEmpty(passage.Ast.Children);
    }

    [Fact]
    public void Passage_AstContainsLinkNode_DerivedAsBranch()
    {
      // The Disclaimer passage has a single [[Continue->FirstPassage]] link;
      // the AST should expose it as a LinkNode and the derived Branches list
      // should mirror it.
      var story = TestFixture.LoadTestFile();
      var passage = story.GetPassage("Disclaimer");

      LinkNode link = null;
      foreach (var n in passage.Ast.Children)
      {
        if (n is LinkNode found) { link = found; break; }
      }
      Assert.NotNull(link);
      Assert.Equal("Continue", link.Text);
      Assert.Equal("FirstPassage", link.Target);

      Assert.Single(passage.Branches);
      Assert.Equal("Continue", passage.Branches[0].Text);
      Assert.Equal("FirstPassage", passage.Branches[0].Name);
    }

    [Fact]
    public void Passage_BodyRenderedFromAst_DecodesNonApostropheEntities()
    {
      // FirstPassage has &quot; (HTML quote) entities in its source. The new
      // pipeline runs InnerHtml through HtmlEntity.DeEntitize before tokenizing,
      // so these should be decoded to actual " characters rather than left
      // verbatim like the old &#39;-only ParseBody did.
      var story = TestFixture.LoadTestFile();
      var body = story.GetPassageBody("FirstPassage");

      Assert.DoesNotContain("&quot;", body);
      Assert.DoesNotContain("&#39;", body);
      Assert.DoesNotContain("&gt;", body);
    }

    [Fact]
    public void Passage_Tags_EmptyForFixturePassages()
    {
      // The bundled testFile.html declares tags="" on every passage. Each
      // passage should report an empty (non-null) tag list.
      var story = TestFixture.LoadTestFile();
      var passage = story.GetPassage("Disclaimer");

      Assert.NotNull(passage.Tags);
      Assert.Empty(passage.Tags);
    }

    [Fact]
    public void Passage_Tags_ParsedFromWhitespaceSeparatedAttribute()
    {
      // The fixture has no real tags, so use a synthetic story to check the
      // splitter: multi-space and tab separators should both produce clean
      // tokens with no empty entries.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"P\" tags=\"alpha  beta\tgamma\">body</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var story = new Harlowe(html);
      var passage = story.GetPassage("P");

      Assert.NotNull(passage.Tags);
      Assert.Equal(new[] { "alpha", "beta", "gamma" }, passage.Tags);
    }

    [Fact]
    public void Passage_Tags_EmptyWhenAttributeMissing()
    {
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"P\">body</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var story = new Harlowe(html);
      var passage = story.GetPassage("P");

      Assert.NotNull(passage.Tags);
      Assert.Empty(passage.Tags);
    }
  }

}
