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
    public void Constructor_NestedHookParseError_NamesPassageInRenderedDiagnostic()
    {
      // Per-node parser recovery inside `[hook contents]` produces a
      // ParseErrorNode under the HookNode, not at the top level. The loader's
      // DecorateParseErrors helper must walk into hook children so the
      // rendered error still mentions the passage by name — otherwise authors
      // get a bare "parse error at line N" with no clue which passage is
      // broken.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"NestedBad\">prefix [hook (set: $x to )] tail</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var story = new Harlowe(html);
      var session = new StorySession(story);
      var r = session.Goto("NestedBad");
      Assert.Contains(r.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                   && e.Content.Contains("NestedBad"));
    }

    [Fact]
    public void Constructor_BodyWithErrorThenValidContent_ResumesParsingAfterBrokenMacro()
    {
      // Per-node recovery now resumes at the next safe body-mode resync
      // point (Newline or closing macro paren). A passage that mixes a bad
      // macro followed by a valid link should yield both the error node AND
      // the trailing link — previously the parser broke after the first
      // error and dropped every well-formed sibling that came after.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"Hub\">(badmacro: $x to ) trailing [[Town]]</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"Town\">t</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var story = new Harlowe(html);
      var hub = story.GetPassage("Hub");
      Assert.Contains(hub.Branches, b => b.Name == "Town");
    }

    [Fact]
    public void Constructor_PassageWithPartialParseFailure_PreservesPriorBranches()
    {
      // A passage with valid prefix content (text, links) followed by a
      // broken macro: per-node parser recovery preserves everything before
      // the failure, so BranchCollector still picks up the links and
      // navigable graph tooling sees the real outgoing edges. Before
      // per-node recovery, the whole AST was replaced with a synthetic
      // single-ParseErrorNode and branches went to zero.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"Hub\">Go to [[Town]] or [[Forest]] then (badmacro: $x to )</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"Town\">town</tw-passagedata>"
        + "<tw-passagedata pid=\"3\" name=\"Forest\">forest</tw-passagedata>"
        + "</tw-storydata></body></html>";

      var story = new Harlowe(html);
      var hub = story.GetPassage("Hub");
      Assert.NotNull(hub);
      // Both branches survive the partial recovery.
      Assert.Equal(2, hub.Branches.Count);
      Assert.Contains(hub.Branches, b => b.Name == "Town");
      Assert.Contains(hub.Branches, b => b.Name == "Forest");
      // Rendering surfaces the parse error in-prose, named to the passage.
      var session = new StorySession(story);
      var result = session.Goto("Hub");
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                        && e.Content.Contains("Hub"));
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
    public void Constructor_MultiStoryArchive_LoadsOnlyFirstStorysPassages()
    {
      // Twine's "Archive" export concatenates several <tw-storydata> blocks.
      // The loader selects the first story; its passage search must be scoped
      // to that story (relative XPath), not pull in the second story's
      // passages — both of which here share the common name "Start".
      const string html = "<html><body>"
        + "<tw-storydata name=\"A\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"Start\">story A</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"OnlyInA\">a</tw-passagedata>"
        + "</tw-storydata>"
        + "<tw-storydata name=\"B\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"Start\">story B</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"OnlyInB\">b</tw-passagedata>"
        + "</tw-storydata>"
        + "</body></html>";
      var story = new Harlowe(html);
      Assert.Equal("A", story.StoryName);
      Assert.Equal(2, story.PassageCount);
      Assert.NotNull(story.GetPassage("OnlyInA"));
      Assert.Null(story.GetPassage("OnlyInB"));
      Assert.Equal("story A", story.GetPassageBody("Start"));
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
    public void Constructor_DuplicatePid_ThrowsHarloweParseException()
    {
      // A shared pid resolves through GetPassageByPid/GetStartPassage to
      // whichever passage enumerates first — non-deterministic across
      // runtimes — so the loader refuses it like a duplicate name.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"A\">a</tw-passagedata>"
        + "<tw-passagedata pid=\"1\" name=\"B\">b</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var ex = Assert.Throws<HarloweParseException>(() => new Harlowe(html));
      Assert.Contains("duplicate passage pid '1'", ex.Message);
      Assert.Contains("'A' and 'B'", ex.Message);
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
    public void GetPassageBody_ReturnsRawAuthorSource()
    {
      // Body now holds the raw author source verbatim (matches the AddPassage
      // contract and the Twee loader). The previous "links stripped" shape
      // produced an empty string for parse-error-recovered passages,
      // indistinguishable from a missing passage. Branches still resolve via
      // GetPassageBranches for callers that want the navigable links.
      var story = TestFixture.LoadTestFile();
      var body = story.GetPassageBody("Disclaimer");

      Assert.Contains("[[", body);
      Assert.NotEmpty(story.GetPassageBranches("Disclaimer"));
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
    public void Constructor_EntityEncodedAttributes_DecodesNamesTagsAndMetadata()
    {
      // Twine 2 entity-encodes attribute values on export. The loader must
      // decode them the same way it decodes bodies, or the passage index key
      // ("Cake &amp; Tea") diverges from the decoded link target ("Cake & Tea")
      // and the link errors "doesn't exist" on a story that works in Twine.
      const string html = "<html><body><tw-storydata name=\"Tea &amp; Cakes\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"Start\" tags=\"spooky&amp;dark\">Visit [[Cake &amp; Tea]]</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"Cake &amp; Tea\">yum</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var story = new Harlowe(html);

      Assert.Equal("Tea & Cakes", story.StoryName);
      var start = story.GetPassage("Start");
      Assert.Contains("spooky&dark", start.Tags);
      // The decoded link target resolves against the decoded passage name.
      var branch = Assert.Single(start.Branches);
      Assert.Equal("Cake & Tea", branch.Name);
      Assert.NotNull(story.GetPassage(branch.Name));
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

    [Fact]
    public void Constructor_TokenizerFailure_RecoversWithSyntheticAst()
    {
      // `=<` makes the *tokenizer* itself throw (the reversed-operator
      // authoring hint) before the body parser ever runs — unlike the
      // parser-level failure above, this exercises the loader's outer catch
      // and the wholly-stubbed AST it substitutes.
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"2\">"
        + "<tw-passagedata pid=\"1\" name=\"Bad\">(if: $x =&lt; 5)[low]</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"Good\">hello</tw-passagedata>"
        + "</tw-storydata></body></html>";

      var story = new Harlowe(html);
      Assert.Equal(2, story.PassageCount);
      Assert.True(ParseErrorNode.IsWhollyParseError(story.GetPassage("Bad").Ast));
      Assert.Equal("(if: $x =< 5)[low]", story.GetPassage("Bad").RawBody);

      var session = new StorySession(story);
      Assert.Equal("hello", session.Render().Text);
      var result = session.Goto("Bad");
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                        && e.Content.Contains("parse error")
                                        && e.Content.Contains("Bad")
                                        && e.Content.Contains("'<='"));
    }

    [Theory]
    [InlineData("(print: $)")]   // bare story-var sigil in expression position
    [InlineData("(print: _)")]   // bare temp-var sigil
    [InlineData("(print: 's)")]  // 's after whitespace: ' opens an unterminated string
    [InlineData("(a: 1,'s)")]    // 's directly after a comma: rejected as the 's operator
    public void MalformedExpressionCharacter_RecoversWithInProseError(string body)
    {
      const string htmlTemplate = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"P\">{0}</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var story = new Harlowe(string.Format(htmlTemplate, body));
      var result = new StorySession(story).Render();
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
    }

    [Fact]
    public void Constructor_DuplicatePassageNames_Throws()
    {
      const string html = "<html><body><tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"Twin\">a</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"Twin\">b</tw-passagedata>"
        + "</tw-storydata></body></html>";
      var ex = Assert.Throws<HarloweParseException>(() => new Harlowe(html));
      Assert.Contains("duplicate passage name 'Twin'", ex.Message);
    }
  }

}
