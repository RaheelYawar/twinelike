using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// <see cref="Harlowe.GetParseErrors"/> — the load-time report of passages that
  /// didn't parse. Sibling of <see cref="BrokenLinkTests"/>, and needed for the
  /// same reason: the loaders are deliberately tolerant, so a passage that won't
  /// parse doesn't abort the load — it gets a synthetic error stub and stays quiet
  /// until a player walks into it. A host engine calls this at load and shows the
  /// developer what's broken before it ships.
  /// </summary>
  public class ParseErrorTests
  {
    private static Harlowe Story(params (string name, string body)[] passages)
    {
      var s = new Harlowe();
      foreach (var (name, body) in passages)
        s.AddPassage(new HarlowePassage { Name = name, Body = body });
      s.StartNode = s.GetPassage(passages[0].name).Pid;
      return s;
    }

    [Fact]
    public void CleanStory_IsEmpty()
    {
      Assert.Empty(Story(("Start", "prose [[Go->Start]] (set: $x to 1)")).GetParseErrors());
    }

    [Fact]
    public void WhollyUnparseablePassage_IsReportedAsWhole()
    {
      var e = Assert.Single(Story(("Start", "(set: $x to 5 eq 6)")).GetParseErrors());

      Assert.Equal("Start", e.PassageName);
      Assert.True(e.IsWholePassage);
      Assert.Contains("eq", e.Detail);
      Assert.Equal(1, e.Line);
      Assert.True(e.Column > 0);
      Assert.Equal("(set: $x to 5 eq 6)", e.Source);
    }

    [Fact]
    public void RecoveredPassage_IsReportedAsPartial()
    {
      // The parser recovered around one bad construct; the prose either side of it
      // still renders. An engine needs to tell this apart from a dead passage.
      var e = Assert.Single(Story(("Start", "before\n(for: each)\nafter")).GetParseErrors());

      Assert.False(e.IsWholePassage);
      Assert.Equal(2, e.Line);
      Assert.Equal("(for: each)", e.Source);
    }

    [Fact]
    public void ErrorNestedInAHook_IsFound()
    {
      // Per-node recovery can leave the error deep inside an otherwise-fine
      // passage. A walk that only looked at top-level children would miss it.
      var e = Assert.Single(Story(("Start", "ok (if: $x)[(for: each)] ok")).GetParseErrors());
      Assert.False(e.IsWholePassage);
      Assert.Contains("each", e.Detail);
    }

    [Fact]
    public void ErrorInsideInlineFormatting_IsFoundAndRendersDecorated()
    {
      // FoldFormatting can nest a recovered error inside a FormatNode. Both the
      // report and the loader's passage-name decoration must reach it — the
      // decoration pass once walked hooks but not format spans, so this error
      // rendered without its "in passage" prefix.
      var story = Story(("Start", "//italic (a: 1 2) more//"));
      Assert.Single(story.GetParseErrors());

      var r = new StorySession(story).Render();
      bool decorated = false;
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && r.Entries[i].Content.Contains("in passage 'Start'"))
          decorated = true;
      Assert.True(decorated);
    }

    [Fact]
    public void ReportsEveryPassage_InPassageOrder()
    {
      var errors = Story(
        ("Start", "fine"),
        ("BadOne", "(set: $x to 5 eq 6)"),
        ("AlsoFine", "fine"),
        ("BadTwo", "(for: each)")).GetParseErrors();

      Assert.Equal(2, errors.Count);
      Assert.Equal("BadOne", errors[0].PassageName);
      Assert.Equal("BadTwo", errors[1].PassageName);
    }

    // ----- shaped for a host engine to display -----

    [Fact]
    public void Message_IsPrintable_AndFlagsADeadPassage()
    {
      var e = Assert.Single(Story(("Start", "(set: $x to 5 eq 6)")).GetParseErrors());

      Assert.StartsWith("In passage 'Start' (line 1, column ", e.Message);
      Assert.Contains("use 'is' instead of 'eq'", e.Message);
      Assert.EndsWith("The whole passage failed to parse and won't render.", e.Message);
      Assert.Equal(e.Message, e.ToString());
    }

    [Fact]
    public void Message_OmitsTheDeadPassageNote_WhenTheParserRecovered()
    {
      var e = Assert.Single(Story(("Start", "before\n(for: each)\nafter")).GetParseErrors());
      Assert.DoesNotContain("whole passage", e.Message);
    }

    [Fact]
    public void Message_OmitsThePositionWhenUnknown()
    {
      var e = new ParseError { PassageName = "P", Detail = "something went wrong" };
      Assert.Equal("In passage 'P': something went wrong.", e.Message);
    }

    // ----- interplay with GetBrokenLinks -----

    [Fact]
    public void ARecoveredPassage_StillGetsItsLinksScanned()
    {
      // The AST survives per-node recovery, so the good half of the passage is
      // still walkable — a dead link after a broken macro must not be swallowed.
      var story = Story(("Start", "(for: each)\n[[Go->Missing]]"));

      Assert.Single(story.GetParseErrors());
      var broken = Assert.Single(story.GetBrokenLinks());
      Assert.Equal("Missing", broken.Target);
    }

    [Fact]
    public void AWhollyDeadPassage_ReportsNoBrokenLinks()
    {
      // Nothing survived to point anywhere; GetParseErrors is what speaks for it.
      var story = Story(("Start", "(set: $x to 5 eq 6)[[Go->Missing]]"));

      Assert.Single(story.GetParseErrors());
      Assert.Empty(story.GetBrokenLinks());
    }
  }
}
