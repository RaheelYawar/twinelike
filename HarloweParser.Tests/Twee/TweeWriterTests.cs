using System.Collections.Generic;
using System.IO;
using Harlowe.Ast.Body;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Twee
{
  public class TweeWriterTests
  {
    private static string Write(Harlowe story) => new TweeWriter().Write(story);
    private static Harlowe Read(string source) => new TweeReader().Read(source);

    [Fact]
    public void NullStory_ReturnsEmpty()
      => Assert.Equal(string.Empty, Write(null));

    [Fact]
    public void EmptyStory_NoMetadata_ReturnsEmpty()
    {
      var story = new TweeReader().Read("");
      Assert.Equal(string.Empty, Write(story));
    }

    [Fact]
    public void SinglePassage_NoMetadata_EmitsHeaderAndBody()
    {
      var story = Read(":: First\nHello world.");
      string output = Write(story);
      Assert.Equal(":: First\nHello world.\n", output);
    }

    [Fact]
    public void PassageHeader_WithTags_EmitsBracketedList()
    {
      var story = Read(":: First [foo bar]\nbody");
      string output = Write(story);
      Assert.Contains(":: First [foo bar]", output);
    }

    [Fact]
    public void PassageHeader_NoTags_OmitsBrackets()
    {
      var story = Read(":: First\nbody");
      string output = Write(story);
      Assert.Contains(":: First\n", output);
      Assert.DoesNotContain("[]", output);
    }

    [Fact]
    public void PassageHeader_TagsWithSpecialChars_EscapeOnEmit()
    {
      // Brackets and backslashes inside a tag name must be backslash-escaped so
      // the reader doesn't terminate the tag block early or mis-split.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage
      {
        Name = "First",
        Tags = new List<string> { "a]b", "c[d", "back\\slash" },
        Body = "x"
      });
      string output = Write(story);
      Assert.Contains(":: First [a\\]b c\\[d back\\\\slash]", output);
    }

    [Fact]
    public void PassageHeader_TagsRoundTripThroughReader()
    {
      // End-to-end: the reader sees back the original tag values after the
      // writer's escapes are applied and parsed.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage
      {
        Name = "First",
        Tags = new List<string> { "a]b", "c[d", "back\\slash" },
        Body = "x"
      });
      string output = Write(story);

      var roundTripped = Read(output);
      var tags = roundTripped.GetPassage("First").Tags;
      Assert.Equal(3, tags.Count);
      Assert.Equal("a]b", tags[0]);
      Assert.Equal("c[d", tags[1]);
      Assert.Equal("back\\slash", tags[2]);
    }

    [Fact]
    public void PassageHeader_TagWithWhitespace_Throws()
    {
      // Twee 3 tags can't contain whitespace; refuse rather than silently
      // splitting the tag in two on round-trip.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage
      {
        Name = "First",
        Tags = new List<string> { "two words" },
        Body = "x"
      });
      var ex = Assert.Throws<HarloweParseException>(() => Write(story));
      Assert.Equal("First", ex.PassageName);
    }

    [Fact]
    public void PassageHeader_WithPosition_PreservedVerbatim()
    {
      var story = Read(":: First {\"position\":\"640,229\"}\nbody");
      string output = Write(story);
      Assert.Contains(":: First {\"position\":\"640,229\"}", output);
    }

    [Fact]
    public void PassageHeader_TagsAndPosition_BothEmitted()
    {
      var story = Read(":: First [foo] {\"position\":\"1,2\"}\nbody");
      string output = Write(story);
      Assert.Contains(":: First [foo] {\"position\":\"1,2\"}", output);
    }

    [Fact]
    public void StoryTitle_EmittedFirst()
    {
      var story = Read(":: StoryTitle\nDeathTrip\n\n:: First\nbody");
      string output = Write(story);
      Assert.StartsWith(":: StoryTitle\nDeathTrip", output);
    }

    [Fact]
    public void StoryData_EmittedWithTypedFields()
    {
      string src = ":: StoryData\n{\"ifid\":\"ABC\",\"format\":\"Harlowe\",\"format-version\":\"3.3.9\",\"start\":\"First\"}\n\n:: First\nbody";
      var story = Read(src);
      string output = Write(story);
      Assert.Contains(":: StoryData", output);
      Assert.Contains("\"ifid\": \"ABC\"", output);
      Assert.Contains("\"format\": \"Harlowe\"", output);
      Assert.Contains("\"format-version\": \"3.3.9\"", output);
      Assert.Contains("\"start\": \"First\"", output);
    }

    [Fact]
    public void StoryData_ClearedIfid_DropsFieldFromOutput()
    {
      // Loading a story with ifid stashes it onto StoryDataExtras as well as
      // the typed Ifid property. Clearing Ifid must remove it from output —
      // the original stashed copy on extras must not leak through.
      string src = ":: StoryData\n{\"ifid\":\"OLD-IFID\",\"format\":\"Harlowe\"}\n\n:: First\nbody";
      var story = Read(src);
      story.Ifid = string.Empty;
      string output = Write(story);
      Assert.DoesNotContain("OLD-IFID", output);
      Assert.DoesNotContain("\"ifid\"", output);
      Assert.Contains("\"format\": \"Harlowe\"", output);
    }

    [Fact]
    public void StoryData_ClearedFormatVersion_DropsFieldFromOutput()
    {
      string src = ":: StoryData\n{\"format\":\"Harlowe\",\"format-version\":\"3.3.9\"}\n\n:: First\nbody";
      var story = Read(src);
      story.FormatVersion = string.Empty;
      string output = Write(story);
      Assert.DoesNotContain("3.3.9", output);
      Assert.DoesNotContain("\"format-version\"", output);
    }

    [Fact]
    public void StoryData_RemovedStartPassage_DropsStartFromOutput()
    {
      // After removing the passage that `start` named, GetStartPassage returns
      // null. The stale start key from the originally-parsed JSON must not
      // survive on output.
      string src = ":: StoryData\n{\"format\":\"Harlowe\",\"start\":\"First\"}\n\n:: First\nbody\n\n:: Second\nB";
      var story = Read(src);
      story.RemovePassage("First");
      string output = Write(story);
      Assert.DoesNotContain("\"start\"", output);
      Assert.DoesNotContain("\"First\"", output);
    }

    [Fact]
    public void StoryData_PreservesUnknownFields()
    {
      // tag-colors and zoom are not surfaced as typed fields; they should
      // round-trip verbatim through StoryDataExtras.
      string src = ":: StoryData\n{\"format\":\"Harlowe\",\"start\":\"First\",\"tag-colors\":{\"foo\":\"red\"},\"zoom\":1.5}\n\n:: First\nbody";
      var story = Read(src);
      string output = Write(story);
      Assert.Contains("\"tag-colors\":", output);
      Assert.Contains("\"foo\": \"red\"", output);
      Assert.Contains("\"zoom\": 1.5", output);
    }

    [Fact]
    public void CleanPassage_EmitsRawBodyVerbatim()
    {
      // RawBody is captured by the reader; on a clean (non-dirty) passage,
      // the writer should emit it byte-for-byte rather than re-canonicalizing.
      string src = ":: First\n  weird   spacing  here  ";
      var story = Read(src);
      string output = Write(story);
      // The trailing whitespace was preserved on the raw line.
      Assert.Contains("  weird   spacing  here  ", output);
    }

    [Fact]
    public void DirtyPassage_RunsThroughMarkupPrinter()
    {
      var story = Read(":: First\nGo to [[Next]].");
      var passage = story.GetPassage("First");
      // Replace the AST with a fresh tree and mark dirty.
      passage.Ast = new PassageBody
      {
        Children = new List<IBodyNode>
        {
          new TextNode { Content = "Edited" }
        }
      };
      passage.IsDirty = true;

      string output = Write(story);
      Assert.Contains(":: First\nEdited\n", output);
      // Old RawBody should NOT appear because the passage was marked dirty.
      Assert.DoesNotContain("[[Next]]", output);
    }

    [Fact]
    public void DirtyAndCleanMix_OnlyDirtyReformatted()
    {
      var story = Read(":: First\noriginal one\n\n:: Second\noriginal two");
      story.GetPassage("First").Ast = new PassageBody
      {
        Children = new List<IBodyNode> { new TextNode { Content = "rewritten" } }
      };
      story.GetPassage("First").IsDirty = true;

      string output = Write(story);
      Assert.Contains(":: First\nrewritten", output);
      Assert.Contains(":: Second\noriginal two", output);
    }

    [Fact]
    public void ParseErrorPassage_WithIsDirty_PreservesOriginalSourceFromRawBody()
    {
      // A parse-error-recovered passage's AST is a synthetic ParseErrorNode
      // with no representable source. If TweeWriter routed it through
      // MarkupPrinter on IsDirty==true the body would silently become empty,
      // destroying the broken-but-recoverable original. ResolveBody must
      // always prefer RawBody for parse-error AST regardless of IsDirty.
      var story = Read(":: Bad\n(set: $x to )\n\n:: Good\nhello");
      var bad = story.GetPassage("Bad");
      Assert.True(Ast.Body.ParseErrorNode.IsWhollyParseError(bad.Ast));

      bad.IsDirty = true;
      string output = Write(story);

      Assert.Contains("(set: $x to )", output);
      Assert.Contains(":: Good\nhello", output);
    }

    [Fact]
    public void ParseErrorPassage_FromAddPassage_RoundTripsThroughTweeWriter()
    {
      // AddPassage recovers from parse errors via the same MakeParseErrorAst
      // path the loaders use, so its broken passages also need the RawBody
      // round-trip preservation.
      var story = new Harlowe { StoryName = "T" };
      story.AddPassage(new HarlowePassage { Name = "Bad", Body = "(set: $x to )" });
      story.GetPassage("Bad").IsDirty = true;

      string output = Write(story);
      Assert.Contains("(set: $x to )", output);
    }

    [Fact]
    public void PartialParseError_IsDirty_DoesNotDiscardAstEdits()
    {
      // After per-node parser recovery, AST may be [Text, Link, ParseErrorNode]
      // with RawBody = original source. A consumer who edits the AST and sets
      // IsDirty=true expects MarkupPrinter to re-canonicalize — not the
      // original RawBody returned verbatim. The earlier IsParseErrorBody guard
      // was an any-child check; it fired for partial recovery too and silently
      // dropped every consumer edit on save.
      var story = Read(":: P\nHello [[Town]] middle (notamacro: $x to 5)");
      var p = story.GetPassage("P");
      // Verify shape: AST has a link and a ParseErrorNode among multiple children.
      Assert.False(Ast.Body.ParseErrorNode.IsWhollyParseError(p.Ast));
      // Append a new child and mark dirty.
      p.Ast.Children.Add(new Ast.Body.TextNode { Content = " EDITED" });
      p.IsDirty = true;
      string output = Write(story);
      Assert.Contains("EDITED", output);
    }

    [Fact]
    public void HandBuiltParseErrorPassage_WithoutRawBody_PreservesSourceViaOriginalSource()
    {
      // A programmatically built passage that hands AddPassage a Body that
      // fails to parse: HydratePassageFromBody routes through MakeParseErrorAst,
      // which stashes the source on ParseErrorNode.OriginalSource. RawBody is
      // populated from passage.Body. With IsDirty=true, MarkupPrinter falls
      // back to OriginalSource on the node so the body still round-trips
      // even if RawBody were stripped.
      var story = new Harlowe { StoryName = "T" };
      story.AddPassage(new HarlowePassage { Name = "Bad", Body = "(set: $x to )" });
      var bad = story.GetPassage("Bad");
      Assert.True(Ast.Body.ParseErrorNode.IsWhollyParseError(bad.Ast));
      var err = (Ast.Body.ParseErrorNode)bad.Ast.Children[0];
      Assert.Equal("(set: $x to )", err.OriginalSource);

      // Even if a consumer cleared RawBody, MarkupPrinter still emits the
      // captured source — round-trip survives.
      bad.RawBody = null;
      bad.IsDirty = true;
      string output = Write(story);
      Assert.Contains("(set: $x to )", output);
    }

    [Fact]
    public void BodyLineStartingWithColonColon_ReEscaped()
    {
      // A line beginning with `::` would be misread as a header on round-trip;
      // the writer must prepend a backslash.
      string src = ":: First\nintro\n\\:: not a header\nstill First";
      var story = Read(src);
      string output = Write(story);
      Assert.Contains("\\:: not a header", output);
    }

    [Fact]
    public void BodyLineWithColonColonInMiddle_NotEscaped()
    {
      // Only line-leading `::` need escaping. Mid-line content is fine.
      var story = Read(":: First\nsome text :: with colons");
      string output = Write(story);
      Assert.Contains("some text :: with colons", output);
      Assert.DoesNotContain("\\::", output);
    }

    [Fact]
    public void BlankLineBetweenBlocks()
    {
      var story = Read(":: First\nA\n\n:: Second\nB");
      string output = Write(story);
      Assert.Contains("A\n\n:: Second", output);
    }

    [Fact]
    public void TrailingNewlineAtEnd()
    {
      var story = Read(":: First\nA");
      string output = Write(story);
      Assert.EndsWith("\n", output);
    }

    [Fact]
    public void RoundTrip_SimpleStory_Stable()
    {
      // Idempotence: read → write → read → write should produce identical output.
      string src = ":: StoryTitle\nMyStory\n\n:: StoryData\n{\"ifid\":\"ABC\",\"format\":\"Harlowe\",\"format-version\":\"3.3.9\",\"start\":\"First\"}\n\n:: First [tag]\nA\n\n:: Second\nB";
      string first = Write(Read(src));
      string second = Write(Read(first));
      Assert.Equal(first, second);
    }

    [Fact]
    public void RoundTrip_PreservesPassageOrder()
    {
      var story = Read(":: First\nA\n\n:: Second\nB\n\n:: Third\nC");
      string output = Write(story);
      int firstIdx = output.IndexOf(":: First");
      int secondIdx = output.IndexOf(":: Second");
      int thirdIdx = output.IndexOf(":: Third");
      Assert.True(firstIdx < secondIdx);
      Assert.True(secondIdx < thirdIdx);
    }

    [Fact]
    public void HtmlLoaded_WriteAsTwee_PassagesPresent()
    {
      // Cross-format check: load the testFile.html fixture, write it as Twee,
      // re-read it, verify the passage names round-trip.
      string html = File.ReadAllText(Path.Combine("TestFiles", "testFile.html"));
      var story = new Harlowe(html);
      string twee = Write(story);
      var roundTripped = Read(twee);
      Assert.Equal(story.PassageCount, roundTripped.PassageCount);
      Assert.NotNull(roundTripped.GetPassage("Disclaimer"));
      Assert.NotNull(roundTripped.GetPassage("FirstPassage"));
    }

    [Fact]
    public void HtmlLoaded_WriteAsTwee_PreservesStoryName()
    {
      string html = File.ReadAllText(Path.Combine("TestFiles", "testFile.html"));
      var story = new Harlowe(html);
      string twee = Write(story);
      var roundTripped = Read(twee);
      Assert.Equal(story.StoryName, roundTripped.StoryName);
    }

    [Fact]
    public void HtmlLoaded_WriteAsTwee_PreservesFormatMetadata()
    {
      string html = File.ReadAllText(Path.Combine("TestFiles", "testFile.html"));
      var story = new Harlowe(html);
      string twee = Write(story);
      var roundTripped = Read(twee);
      Assert.Equal(story.Ifid, roundTripped.Ifid);
      Assert.Equal(story.Format, roundTripped.Format);
      Assert.Equal(story.FormatVersion, roundTripped.FormatVersion);
    }

    [Fact]
    public void HtmlLoaded_WriteAsTwee_StartPassageRoundTrips()
    {
      // The start passage's name must match across formats — pids differ
      // (HTML uses Twine-assigned pids; Twee re-synthesizes them) but the
      // start passage *name* should stay stable.
      string html = File.ReadAllText(Path.Combine("TestFiles", "testFile.html"));
      var story = new Harlowe(html);
      string twee = Write(story);
      var roundTripped = Read(twee);
      Assert.Equal(story.GetStartPassage().Name, roundTripped.GetStartPassage().Name);
    }

    [Fact]
    public void EmptyStoryName_OmitsStoryTitleBlock()
    {
      var story = Read(":: First\nbody");
      string output = Write(story);
      Assert.DoesNotContain(":: StoryTitle", output);
    }

    [Fact]
    public void EmptyPassageBody_EmitsHeaderOnly()
    {
      var story = Read(":: Empty\n\n:: Next\nbody");
      string output = Write(story);
      Assert.Contains(":: Empty\n\n:: Next", output);
    }
  }
}
