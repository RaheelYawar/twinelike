using System.Text;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Collapsing whitespace markup, <c>{…}</c> and <c>{=</c>, against the
  /// behaviour of reference's <c>collapse()</c> in <c>ts/utils/renderutils.ts</c>.
  /// </summary>
  public class CollapsedWhitespaceTests
  {
    private static StorySession Session(string start, params (string name, string body)[] others)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "Start", RawBody = start });
      foreach (var (name, body) in others)
        story.AddPassage(new HarlowePassage { Name = name, RawBody = body });
      story.StartNode = story.GetPassage("Start").Pid;
      return new StorySession(story);
    }

    private static string Render(string start, params (string name, string body)[] others)
      => Session(start, others).Render().Text;

    private static string FirstRegion(RenderResult result)
    {
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.BeginInteractive) return entry.Region.Id;
      return null;
    }

    [Theory]
    [InlineData("{\n\n  Hello\n}", "Hello")]
    [InlineData("{a   b}", "a b")]
    [InlineData("{a\n\n\nb}", "a b")]
    [InlineData("{  a  }", "a")]
    [InlineData("x{  a  }y", "xay")]
    [InlineData("{}", "")]
    [InlineData("{   }", "")]
    [InlineData("before  {  a  }  after", "before  a  after")]
    [InlineData("{a  b}", "a b")]
    public void Prose_Collapses(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Fact]
    public void ReferenceExample_FromTheMarkupArticle()
    {
      const string source = "{\n\tThis sentence\n\twill be\n\t(set: $event to true)\n\twritten on one line\n\twith only single spaces.\n}";
      Assert.Equal("This sentence will be written on one line with only single spaces.", Render(source));
    }

    [Theory]
    [InlineData("{Good  { gumballs!}}", "Good gumballs!")]
    [InlineData("{a {  } b}", "a b")]
    [InlineData("{a }{ b}", "a b")]
    [InlineData("{a}{b}", "ab")]
    public void Nested_AndAdjacentSpans(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("{ [ a   b ] }", "a b")]
    [InlineData("{|h>[B ]C}", "B C")]
    [InlineData("{ ''a   b'' }", "a b")]
    [InlineData("{ (if: true)[  a   b  ] }", "a b")]
    [InlineData("{(for: each _i, 1, 2)[ x ]}", "x x")]
    public void HooksAndFormatting_Collapse(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("{(print: \"a   b\")}", "a   b")]
    [InlineData("{(print: \"\\n\\n\\n\")}", "\n\n\n")]
    [InlineData("(set: $v to \"a   b\"){$v}", "a   b")]
    [InlineData("(set: $v to \"a   b\"){ x $v y }", "x a   b y")]
    [InlineData("{(display: \"Attic\")}", "dusty   room")]
    public void PrintedOutput_KeepsItsWhitespace(string source, string expected)
      => Assert.Equal(expected, Render(source, ("Attic", "dusty   room")));

    [Fact]
    public void CollapseInsidePrintedString_StillApplies()
      => Assert.Equal("x a b y", Render("{ x (print: \"{a   b}\") y }"));

    [Fact]
    public void LinkLabel_KeepsItsWhitespace()
    {
      var result = Session("{ see  [[a  b->Next]]  now }", ("Next", "n")).Render();
      Assert.Equal("a  b", LinkLabel(result));
      Assert.Equal("see  now", result.Text); // link labels aren't part of Text
    }

    private static string LinkLabel(RenderResult result)
    {
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Link) return entry.Content;
      return null;
    }

    [Fact]
    public void SpaceBesideAnError_IsKept()
    {
      // The error counts as visible text, so neither space collapses into the other.
      Assert.Equal("a  b", Session("{ a (print: _unset) b }").Render().Text);
    }

    [Theory]
    [InlineData("a {  b", "a {  b")]
    [InlineData("a }  b", "a }  b")]
    [InlineData("} a {", "} a {")]
    [InlineData("''{a''}", "{a}")]
    [InlineData("[{a]}", "{a}")]
    public void UnmatchedBraces_AreLiteralText(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("keep   this\n{=\ncollapse   this\nplease", "keep   this\ncollapse this please")]
    [InlineData("{==  a   b", "a b")]
    [InlineData("[x   {= a   b ]c   d", "x   a bc   d")]
    [InlineData("''a {= b   c''   d", "a b c   d")]
    public void UnclosedMarker_RunsToTheEndOfItsContainer(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Fact]
    public void Links_AndMacros_DoNotTreatBracesAsMarkup()
    {
      Assert.Equal("{x}", LinkLabel(Session("[[{x}->Next]]", ("Next", "n")).Render()));
      Assert.Equal("a", Render("(print: \"{a}\")")); // printed strings are markup
    }

    // ----- Revisions: whitespace follows where the hook was written -----

    [Fact]
    public void RevisionWrittenOutside_KeepsItsWhitespaceInsideASpan()
      => Assert.Equal("Good  golly!", Render("{[]<h|}(replace: ?h)[Good  golly!]"));

    [Fact]
    public void RevisionWrittenInside_CollapsesEvenWhenItLandsOutside()
      => Assert.Equal("a b", Render("[x]<h|{(replace: ?h)[  a   b  ]}"));

    [Fact]
    public void AppendFromOutside_LeavesTheHooksOwnTextCollapsed()
      => Assert.Equal("a bc  d", Render("{[a   b]<h|}(append: ?h)[c  d]"));

    [Fact]
    public void StringRevision_SeesTextBeforeItCollapses()
      => Assert.Equal("c", Render("{a   b}(replace: \"a   b\")[c]"));

    // ----- Deferred content -----

    [Fact]
    public void ClickRevealInsideASpan_Collapses()
    {
      var session = Session("{a (click: \"a\")[  b   c  ] d}");
      var first = session.Render();
      Assert.Equal("a d", first.Text);
      var after = session.DispatchEvent(FirstRegion(first));
      Assert.Equal("a b c d", after.Text);
    }

    [Fact]
    public void LinkLabelMacro_CollapsesInsideASpan()
    {
      var result = Session("{ (link: \"a   b\")[c] }").Render();
      Assert.Equal("a b", result.Text);
    }

    [Fact]
    public void ClickRevealOutsideASpan_KeepsItsWhitespace()
    {
      var session = Session("{x}go(click: \"go\")[  b   c  ]");
      var after = session.DispatchEvent(FirstRegion(session.Render()));
      Assert.Equal("xgo  b   c  ", after.Text);
    }

    [Fact]
    public void EnchantmentAfterCollapse_MatchesCollapsedText()
    {
      var result = Session("{a   b}(enchant: \"a b\", (text-style: \"bold\"))").Render();
      Assert.Equal("a b", result.Text);
      bool styled = false;
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.PushStyle && entry.Style.Bold) styled = true;
      Assert.True(styled);
    }

    [Fact]
    public void NoEmptyTextEventsReachTheHost()
    {
      var result = Session("{  (set: $x to 1)  a   (set: $y to 2)   }").Render();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Text) Assert.NotEqual("", entry.Content);
      Assert.Equal("a", result.Text);
    }

    [Fact]
    public void ExpressionPositionDisplay_Collapses()
      => Assert.Equal("a b", Render("(set: $x to (display: \"P\"))$x", ("P", "{ a   b }")));

    [Fact]
    public void SaveAndLoad_ReproduceTheCollapsedPassage()
    {
      var session = Session("{ a   (set: $v to 1)   b }");
      Assert.Equal("a b", session.Render().Text);
      Assert.True(session.SaveGame("slot"), session.LastSaveError);
      Assert.True(session.LoadGame("slot"), session.LastLoadError);
      Assert.Equal("a b", session.Render().Text);
    }

    // ----- Parsing and round-trip -----

    private static PassageBody Parse(string source)
      => new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source), source);

    [Theory]
    [InlineData("{a  b}")]
    [InlineData("x{a{b}c}y")]
    [InlineData("{= rest of passage")]
    [InlineData("{=== rest")]
    [InlineData("[{= in hook]after")]
    [InlineData("a { unmatched")]
    [InlineData("a } unmatched")]
    [InlineData("''{a}''")]
    public void MarkupPrinter_RoundTrips(string source)
      => Assert.Equal(source, new MarkupPrinter().Print(Parse(source)));

    [Fact]
    public void Parser_BuildsTheExpectedNodes()
    {
      var body = Parse("a{b}c");
      Assert.IsType<TextNode>(body.Children[0]);
      var collapsed = Assert.IsType<CollapsedNode>(body.Children[1]);
      Assert.Null(collapsed.UnclosedMarker);
      Assert.IsType<TextNode>(body.Children[2]);

      var unclosed = Assert.IsType<CollapsedNode>(Parse("{== b").Children[0]);
      Assert.Equal("{==", unclosed.UnclosedMarker);
    }

    [Fact]
    public void Links_InsideASpan_AreStillCollected()
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "Start", RawBody = "{ [[Missing]] }" });
      var broken = Assert.Single(story.GetBrokenLinks());
      Assert.Equal("Missing", broken.Target);
    }

    [Fact]
    public void Comment_EliminatesAWholeSpan_UnderV4()
    {
      var body = new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V4).Tokenize("a--{b}c"), "a--{b}c");
      var sb = new StringBuilder();
      foreach (var child in body.Children)
        if (child is TextNode t) sb.Append(t.Content);
      Assert.Equal("ac", sb.ToString());
    }
  }
}
