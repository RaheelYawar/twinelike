using System.Collections.Generic;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Twee
{
  public class TweeReaderTests
  {
    private static Harlowe Read(string source) => new TweeReader().Read(source);

    // U+FEFF BOM, written via code point so it stays visible in source.
    private const char Bom = (char)0xFEFF;

    [Fact]
    public void Empty_ReturnsEmptyStory()
    {
      var story = Read("");
      Assert.Equal(0, story.PassageCount);
      Assert.Equal("", story.StoryName);
      Assert.Equal("", story.Format);
    }

    [Fact]
    public void Null_ReturnsEmptyStory()
    {
      var story = Read(null);
      Assert.Equal(0, story.PassageCount);
    }

    [Fact]
    public void LeadingBom_Stripped_SinglePassageStillParses()
    {
      // A consumer decoding bytes with Encoding.UTF8.GetString (rather than
      // BOM-aware File.ReadAllText) leaves a leading BOM on the string. It
      // must not hide the first :: header and drop the only passage.
      var story = Read(Bom + ":: First\nHello world.");
      Assert.Equal(1, story.PassageCount);
      Assert.Equal("Hello world.", story.GetPassage("First").RawBody);
    }

    [Fact]
    public void LeadingBom_Stripped_FirstOfMultipleNotDropped()
    {
      var story = Read(Bom + ":: First\nA\n\n:: Second\nB");
      Assert.Equal(2, story.PassageCount);
      Assert.Equal("A", story.GetPassage("First").RawBody);
      Assert.Equal("B", story.GetPassage("Second").RawBody);
    }

    [Fact]
    public void SinglePassage_ParsesNameAndBody()
    {
      var story = Read(":: First\nHello world.");
      Assert.Equal(1, story.PassageCount);
      var p = story.GetPassage("First");
      Assert.NotNull(p);
      Assert.Equal("First", p.Name);
      Assert.Equal("Hello world.", p.RawBody);
      Assert.Equal("Hello world.", p.Body);
      Assert.Empty(p.Tags);
      Assert.Null(p.Position);
    }

    [Fact]
    public void MultiplePassages_PreserveOrder()
    {
      var story = Read(":: First\nA\n\n:: Second\nB\n\n:: Third\nC");
      Assert.Equal(3, story.PassageCount);
      Assert.Equal("1", story.GetPassage("First").Pid);
      Assert.Equal("2", story.GetPassage("Second").Pid);
      Assert.Equal("3", story.GetPassage("Third").Pid);
    }

    [Fact]
    public void BlankLineBetweenPassages_NotIncludedInBody()
    {
      var story = Read(":: First\nA\n\n:: Second\nB");
      Assert.Equal("A", story.GetPassage("First").RawBody);
      Assert.Equal("B", story.GetPassage("Second").RawBody);
    }

    [Fact]
    public void MultiLineBody_PreservesInternalNewlines()
    {
      var story = Read(":: First\nLine 1\nLine 2\nLine 3");
      Assert.Equal("Line 1\nLine 2\nLine 3", story.GetPassage("First").RawBody);
    }

    [Fact]
    public void Tags_ParsedFromHeader()
    {
      var story = Read(":: First [foo bar]\nbody");
      var tags = story.GetPassage("First").Tags;
      Assert.Equal(2, tags.Count);
      Assert.Equal("foo", tags[0]);
      Assert.Equal("bar", tags[1]);
    }

    [Fact]
    public void EmptyTagBracket_ProducesEmptyList()
    {
      var story = Read(":: First []\nbody");
      Assert.Empty(story.GetPassage("First").Tags);
    }

    [Fact]
    public void Tags_EscapedBracketsInName_AreUnescaped()
    {
      // Twee 3 lets tag names carry [ and ] via \ escapes — the reader must
      // skip past an escaped ] when finding the closing tag block and strip
      // the leading \ when materializing the tag value.
      var story = Read(":: First [a\\]b c\\[d]\nbody");
      var tags = story.GetPassage("First").Tags;
      Assert.Equal(2, tags.Count);
      Assert.Equal("a]b", tags[0]);
      Assert.Equal("c[d", tags[1]);
    }

    [Fact]
    public void Tags_EscapedBackslash_IsUnescaped()
    {
      // \\ in source materializes to a single backslash in the tag value.
      var story = Read(":: First [back\\\\slash]\nbody");
      var tags = story.GetPassage("First").Tags;
      Assert.Single(tags);
      Assert.Equal("back\\slash", tags[0]);
    }

    [Fact]
    public void Tags_TabSeparated_SplitIntoMultipleTags()
    {
      var story = Read(":: First [foo\tbar]\nbody");
      var tags = story.GetPassage("First").Tags;
      Assert.Equal(2, tags.Count);
      Assert.Equal("foo", tags[0]);
      Assert.Equal("bar", tags[1]);
    }

    [Fact]
    public void DuplicatePassageName_ThrowsHarloweParseException()
    {
      // The underlying dictionary throws ArgumentException; the loader rewraps
      // so consumers can catch a single exception type for malformed input.
      var ex = Assert.Throws<HarloweParseException>(() => Read(":: First\nA\n\n:: First\nB"));
      Assert.Equal("First", ex.PassageName);
    }

    [Fact]
    public void Position_PreservedFromHeader()
    {
      var story = Read(":: First {\"position\":\"640,229\"}\nbody");
      Assert.Equal("{\"position\":\"640,229\"}", story.GetPassage("First").Position);
    }

    [Fact]
    public void TagsAndPosition_BothPresent()
    {
      var story = Read(":: First [foo] {\"position\":\"1,2\"}\nbody");
      var p = story.GetPassage("First");
      Assert.Equal("foo", p.Tags[0]);
      Assert.Equal("{\"position\":\"1,2\"}", p.Position);
    }

    [Fact]
    public void Position_NestedObject_CapturedWhole()
    {
      // The blob is stored opaque and re-emitted verbatim, so a first-'}'
      // scan that dropped the outer brace made the writer round-trip
      // invalid JSON. Twine's flat blob never nests; custom metadata can.
      var story = Read(":: First {\"position\":\"1,2\",\"meta\":{\"a\":1}}\nbody");
      Assert.Equal("{\"position\":\"1,2\",\"meta\":{\"a\":1}}", story.GetPassage("First").Position);
    }

    [Fact]
    public void Position_BraceInsideStringValue_CapturedWhole()
    {
      var story = Read(":: First {\"note\":\"a}b\"}\nbody");
      Assert.Equal("{\"note\":\"a}b\"}", story.GetPassage("First").Position);
    }

    [Fact]
    public void Position_EscapedQuoteInsideStringValue_CapturedWhole()
    {
      // \" inside the string must not end it, or the } after it reads as
      // in-string content and the block never closes.
      var story = Read(":: First {\"note\":\"a\\\"}\\\"b\"}\nbody");
      Assert.Equal("{\"note\":\"a\\\"}\\\"b\"}", story.GetPassage("First").Position);
    }

    [Fact]
    public void Position_UnclosedBlock_IgnoredNotCaptured()
    {
      // An unterminated block keeps the old contract: no position, and the
      // header still parses (name + tags intact).
      var story = Read(":: First [foo] {\"position\":\"1,2\"\nbody");
      var p = story.GetPassage("First");
      Assert.Equal("foo", p.Tags[0]);
      Assert.Null(p.Position);
    }

    [Fact]
    public void NameWithSpaces_ParsedCorrectly()
    {
      var story = Read(":: My Long Name\nbody");
      Assert.NotNull(story.GetPassage("My Long Name"));
    }

    [Fact]
    public void NameTrimmed_OfTrailingSpacesBeforeBracket()
    {
      var story = Read(":: First [tag]\nbody");
      Assert.NotNull(story.GetPassage("First"));
    }

    [Fact]
    public void StoryTitle_PopulatesStoryName()
    {
      var story = Read(":: StoryTitle\nDeathTrip\n\n:: First\nbody");
      Assert.Equal("DeathTrip", story.StoryName);
      // StoryTitle itself is not exposed as a regular passage.
      Assert.Equal(1, story.PassageCount);
      Assert.Null(story.GetPassage("StoryTitle"));
    }

    [Fact]
    public void StoryData_PopulatesMetadata()
    {
      string src = ":: StoryData\n{\n  \"ifid\": \"D674C58C-DEFA-4F70-B7A2-27742230C0FC\",\n  \"format\": \"Harlowe\",\n  \"format-version\": \"3.3.9\",\n  \"start\": \"First\"\n}\n\n:: First\nbody";
      var story = Read(src);
      Assert.Equal("D674C58C-DEFA-4F70-B7A2-27742230C0FC", story.Ifid);
      Assert.Equal("Harlowe", story.Format);
      Assert.Equal("3.3.9", story.FormatVersion);
      Assert.Equal(story.GetPassage("First").Pid, story.StartNode);
    }

    [Fact]
    public void StoryData_ResolvesStart_RegardlessOfOrder()
    {
      // StoryData appears before First but its `start: "First"` still resolves.
      string src = ":: StoryData\n{\"start\":\"First\"}\n\n:: First\nA\n\n:: Second\nB";
      var story = Read(src);
      Assert.Equal(story.GetPassage("First").Pid, story.StartNode);
    }

    [Fact]
    public void StoryData_StartAfterStoryDataBlock_AlsoResolves()
    {
      // start name resolves even when the named passage is declared after StoryData.
      string src = ":: First\nA\n\n:: StoryData\n{\"start\":\"Second\"}\n\n:: Second\nB";
      var story = Read(src);
      Assert.Equal(story.GetPassage("Second").Pid, story.StartNode);
    }

    [Fact]
    public void StoryData_NumericStart_CoercesAndResolves()
    {
      // Out-of-spec but recoverable: {"start":123} names the passage "123".
      // The old string-only read dropped it silently, so the story loaded
      // with StartNode still "0" and could never start.
      string src = ":: StoryData\n{\"start\":123}\n\n:: 123\nA\n\n:: Other\nB";
      var story = Read(src);
      Assert.Equal(story.GetPassage("123").Pid, story.StartNode);
    }

    [Fact]
    public void StoryData_NumericStart_NoMatchingPassage_LeavesDefaultStartNode()
    {
      // Coercion feeds the same lookup gate as a string start: an
      // unresolvable name leaves StartNode alone.
      var story = Read(":: StoryData\n{\"start\":123}\n\n:: First\nA");
      Assert.Equal("0", story.StartNode);
    }

    [Fact]
    public void StoryData_StructuredStart_LeavesDefaultStartNode()
    {
      // An object/array can't be a passage name; stays unresolved, no crash.
      var story = Read(":: StoryData\n{\"start\":{\"a\":1}}\n\n:: First\nA");
      Assert.Equal("0", story.StartNode);
    }

    [Fact]
    public void StoryData_NotPassage_DoesNotShowInPassageCount()
    {
      var story = Read(":: StoryData\n{\"format\":\"Harlowe\"}\n\n:: First\nA");
      Assert.Equal(1, story.PassageCount);
      Assert.Null(story.GetPassage("StoryData"));
    }

    [Fact]
    public void StoryData_MissingStart_LeavesDefaultStartNode()
    {
      var story = Read(":: StoryData\n{\"format\":\"Harlowe\"}\n\n:: First\nbody");
      Assert.Equal("0", story.StartNode);
    }

    [Fact]
    public void StoryData_MalformedJson_DoesNotThrow_SiblingsSurvive()
    {
      // Per the Twee 3 spec, a StoryData decode error should discard the
      // metadata and continue processing — not abort the whole load.
      var story = Read(":: StoryData\nnot json at all\n\n:: First\nbody");
      Assert.NotNull(story.GetPassage("First"));
      Assert.Equal("", story.Ifid);
    }

    [Fact]
    public void StoryData_DeeplyNestedJson_DoesNotCrash_SiblingsSurvive()
    {
      // A degenerately nested StoryData body would recurse JsonReader into an
      // uncatchable StackOverflowException. The depth cap turns it into a
      // HarloweParseException, which Read discards like any malformed
      // StoryData — the load survives and siblings still parse.
      var story = Read(":: StoryData\n" + new string('[', 5000) + "\n\n:: First\nbody");
      Assert.NotNull(story.GetPassage("First"));
      Assert.Equal("", story.Ifid);
    }

    [Fact]
    public void StoryData_ValidJsonButNotObject_Discarded()
    {
      // A top-level JSON value that isn't an object (a bare number) is
      // discarded rather than throwing.
      var story = Read(":: StoryData\n42\n\n:: First\nbody");
      Assert.NotNull(story.GetPassage("First"));
      Assert.Equal("", story.Ifid);
    }

    [Fact]
    public void PassageName_EscapedBrackets_Unescaped()
    {
      // Per the Twee 3 spec, [ ] { } in a passage name are backslash-escaped.
      // The reader strips the escape and does NOT read \[A\] as a tag block.
      var story = Read(":: Choose \\[A\\]\nbody");
      var p = story.GetPassage("Choose [A]");
      Assert.NotNull(p);
      Assert.Empty(p.Tags);
    }

    [Fact]
    public void PassageName_EscapedBraces_Unescaped()
    {
      var p = Read(":: A\\{B\\}\nbody").GetPassage("A{B}");
      Assert.NotNull(p);
      Assert.Empty(p.Tags);
    }

    [Fact]
    public void PassageName_EscapedBackslash_Unescaped()
      => Assert.NotNull(Read(":: A\\\\B\nbody").GetPassage("A\\B"));

    [Fact]
    public void PassageName_EscapedBracketThenTags_BothParsed()
    {
      // An escaped bracket belongs to the name; a later unescaped [ still opens
      // the tag block.
      var p = Read(":: Choose \\[A\\] [forest]\nbody").GetPassage("Choose [A]");
      Assert.NotNull(p);
      Assert.Equal(new List<string> { "forest" }, p.Tags);
    }

    [Fact]
    public void PassageName_Plain_Unaffected()
    {
      // Regression: a name with no metacharacters scans exactly as before —
      // stops at the first [ which opens the tag block.
      var p = Read(":: First [tag]\nbody").GetPassage("First");
      Assert.NotNull(p);
      Assert.Equal(new List<string> { "tag" }, p.Tags);
    }

    [Fact]
    public void Body_RoutesThroughBodyParser_AstPopulated()
    {
      var story = Read(":: First\nGo to [[Second->Second]].");
      var p = story.GetPassage("First");
      Assert.NotNull(p.Ast);
      Assert.Single(p.Branches);
      Assert.Equal("Second", p.Branches[0].Name);
    }

    [Fact]
    public void Body_HoldsRawAuthorSource()
    {
      // Body now stores the verbatim author source — matches AddPassage and
      // the HTML loader's contract. The previous "macro-stripped prose" shape
      // diverged from those paths and produced an empty Body for parse-error-
      // recovered passages, indistinguishable from a missing passage.
      var story = Read(":: First\nClick [[here->Other]] now.");
      Assert.Equal("Click [[here->Other]] now.", story.GetPassage("First").Body);
    }

    [Fact]
    public void EscapedColonColon_StaysInBody()
    {
      // A line beginning with `\::` is body content; the leading backslash is
      // stripped per Twee 3 escape rules, so the body sees a literal `::`.
      var story = Read(":: First\n\\:: Not a header\nstill First");
      Assert.Equal(1, story.PassageCount);
      Assert.Equal(":: Not a header\nstill First", story.GetPassage("First").RawBody);
    }

    [Fact]
    public void PreambleBeforeFirstHeader_Discarded()
    {
      var story = Read("Some preamble\n\n:: First\nbody");
      Assert.Equal(1, story.PassageCount);
      Assert.NotNull(story.GetPassage("First"));
    }

    [Fact]
    public void CrlfLineEndings_HandledIdentically()
    {
      var story = Read(":: First\r\nA\r\n\r\n:: Second\r\nB");
      Assert.Equal(2, story.PassageCount);
      Assert.Equal("A", story.GetPassage("First").RawBody);
      Assert.Equal("B", story.GetPassage("Second").RawBody);
    }

    [Fact]
    public void EmptyBody_ParsesAsEmpty()
    {
      var story = Read(":: Empty\n\n:: Next\nbody");
      var p = story.GetPassage("Empty");
      Assert.Equal("", p.RawBody);
      Assert.NotNull(p.Ast);
    }

    [Fact]
    public void InvalidStoryDataJson_DiscardedNotThrown()
    {
      // Malformed StoryData JSON is discarded (metadata stays at defaults) and
      // the load continues rather than throwing — Twee 3 spec recommendation and
      // this library's never-throw loader policy. Was previously pinned to throw.
      var story = Read(":: StoryData\n{ bad json\n\n:: First\nbody");
      Assert.Equal("", story.Ifid);
      Assert.NotNull(story.GetPassage("First"));
    }

    [Fact]
    public void IsDirty_DefaultsFalse()
    {
      var story = Read(":: First\nbody");
      Assert.False(story.GetPassage("First").IsDirty);
    }

    [Fact]
    public void Pid_IsSequentialAcrossRegularPassagesOnly()
    {
      // StoryTitle/StoryData consume sequential slots? They do not — synthetic pids only count regular passages.
      var story = Read(":: StoryTitle\nT\n\n:: First\nA\n\n:: StoryData\n{}\n\n:: Second\nB");
      Assert.Equal("1", story.GetPassage("First").Pid);
      Assert.Equal("2", story.GetPassage("Second").Pid);
    }

    [Fact]
    public void HeaderJunkAfterTagBlock_IsIgnored()
    {
      var story = Read(":: First [alpha] junk\nbody");
      var p = story.GetPassage("First");
      Assert.NotNull(p);
      Assert.Contains("alpha", p.Tags);
      Assert.Equal("body", p.RawBody);
    }

    [Fact]
    public void UnclosedTagBlock_StillLoadsThePassage()
    {
      var story = Read(":: First [unclosed\nbody");
      Assert.Equal(1, story.PassageCount);
    }

    [Fact]
    public void Branches_CollectedFromPassageContainingHtml()
    {
      // BranchCollector must walk past HtmlNodes to reach the links.
      var story = Read(":: First\n<b>bold</b> [[Next]]");
      var branches = story.GetPassage("First").Branches;
      Assert.Contains(branches, b => b.Name == "Next");
    }

    [Fact]
    public void TokenizerFailure_RecoversPerPassage_AndRoundTrips()
    {
      // `=<` throws in the tokenizer itself (the reversed-operator authoring
      // hint), so this exercises the Twee loader's catch + stub substitution
      // rather than the body parser's per-node recovery.
      var story = Read(":: Bad\n(if: $x =< 5)[low]\n\n:: Good\nhello");
      Assert.NotNull(story.GetPassage("Good"));

      var bad = story.GetPassage("Bad");
      Assert.True(Ast.Body.ParseErrorNode.IsWhollyParseError(bad.Ast));
      Assert.Contains("=< 5", bad.RawBody);

      // RawBody is preserved verbatim, so write-out reproduces the broken source.
      string output = new TweeWriter().Write(story);
      Assert.Contains("(if: $x =< 5)[low]", output);
    }
  }
}
