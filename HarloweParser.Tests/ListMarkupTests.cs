using System.Collections.Generic;
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
  /// Bulleted and numbered list markup, reference's <c>bulleted</c> and
  /// <c>numbered</c> rules. Expected pages were rendered by the real 3.3.9
  /// build and written as <c>⟦ul:…⟧</c>, <c>⟦ol:…⟧</c>, <c>⟦li:…⟧</c>,
  /// <c>⟦b:…⟧</c>, <c>⟦i:…⟧</c>, <c>⟦link:…⟧</c>, <c>⟦hr⟧</c> and
  /// <c>⟦error⟧</c>, with <c>\n</c> for a line break.
  /// </summary>
  public class ListMarkupTests
  {
    private static HarloweStory Story(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.StartNode = story.GetPassage("test").Pid;
      return story;
    }

    private static string Shown(string body) => Normalise(new StorySession(Story(body), 42).Render());

    private static string Normalise(RenderResult result)
    {
      var sb = new StringBuilder();
      var closers = new Stack<int>();
      foreach (var e in result.Entries)
      {
        switch (e.Kind)
        {
          case BufferedRenderOutput.Kind.Text: sb.Append(e.Content); break;
          case BufferedRenderOutput.Kind.Link: sb.Append("⟦link:").Append(e.Content).Append('⟧'); break;
          case BufferedRenderOutput.Kind.Error: sb.Append("⟦error⟧"); break;
          case BufferedRenderOutput.Kind.BeginInteractive: sb.Append("⟦link:"); break;
          case BufferedRenderOutput.Kind.EndInteractive: sb.Append('⟧'); break;
          case BufferedRenderOutput.Kind.PushStyle:
            int n = 0;
            if (e.Style.Bold) { sb.Append("⟦b:"); n++; }
            if (e.Style.Italic) { sb.Append("⟦i:"); n++; }
            closers.Push(n);
            break;
          case BufferedRenderOutput.Kind.PopStyle: sb.Append('⟧', closers.Count > 0 ? closers.Pop() : 0); break;
          case BufferedRenderOutput.Kind.Html:
            switch (e.Content)
            {
              case "<ul>": sb.Append("⟦ul:"); break;
              case "<ol>": sb.Append("⟦ol:"); break;
              case "<li>": sb.Append("⟦li:"); break;
              case "</ul>": case "</ol>": case "</li>": sb.Append('⟧'); break;
              case "<hr>": sb.Append("⟦hr⟧"); break;
              case "<br>": sb.Append('\n'); break;
              default:
                if (e.Content.StartsWith("<b")) sb.Append("⟦b:");
                else if (e.Content.StartsWith("</b")) sb.Append('⟧');
                break;
            }
            break;
        }
      }
      return sb.ToString();
    }

    private static PassageBody Parse(string source)
      => new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source), source);

    [Theory]
    [InlineData("* a", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("* a\n* b", "⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("* a\n* b\nc", "⟦ul:⟦li:a⟧⟦li:b⟧⟧c")]
    [InlineData("* a\n\n* b", "⟦ul:⟦li:a⟧⟧\n⟦ul:⟦li:b⟧⟧")]
    [InlineData("a\n* b", "a\n⟦ul:⟦li:b⟧⟧")]
    [InlineData("* a\nb", "⟦ul:⟦li:a⟧⟧b")]
    [InlineData("* a\n\nb", "⟦ul:⟦li:a⟧⟧\nb")]
    [InlineData("* a\n\n\nb", "⟦ul:⟦li:a⟧⟧\n\nb")]
    [InlineData("* a\n", "⟦ul:⟦li:a⟧⟧")]
    [InlineData(" * a", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("\t* a", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("\u00a0* a", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("\u3000* a", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("  * a\n  * b", "⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("*a", "*a")]
    [InlineData("*\ta", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("*  a", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("* a *b*", "⟦ul:⟦li:a ⟦i:b⟧⟧⟧")]
    [InlineData("* a **b**", "⟦ul:⟦li:a ⟦b:b⟧⟧⟧")]
    [InlineData("** a", "⟦ul:⟦ul:⟦li:a⟧⟧⟧")]
    [InlineData("* a\n** b\n* c", "⟦ul:⟦li:a⟧⟦ul:⟦li:b⟧⟧⟦li:c⟧⟧")]
    [InlineData("** a\n* b", "⟦ul:⟦ul:⟦li:a⟧⟧⟦li:b⟧⟧")]
    [InlineData("*** a\n* b", "⟦ul:⟦ul:⟦ul:⟦li:a⟧⟧⟧⟦li:b⟧⟧")]
    [InlineData("* a\n*** b\n* c", "⟦ul:⟦li:a⟧⟦ul:⟦ul:⟦li:b⟧⟧⟧⟦li:c⟧⟧")]
    [InlineData("** a **", "⟦ul:⟦ul:⟦li:a **⟧⟧⟧")]
    [InlineData("**a**", "⟦b:a⟧")]
    [InlineData("* a\n**b**", "⟦ul:⟦li:a⟧⟧⟦b:b⟧")]
    [InlineData("* ", "⟦ul:⟦li:⟧⟧")]
    [InlineData("*", "*")]
    [InlineData("* \n* b", "⟦ul:⟦li:⟧⟦li:b⟧⟧")]
    [InlineData("* a\n* ", "⟦ul:⟦li:a⟧⟦li:⟧⟧")]
    [InlineData("x * a", "x * a")]
    [InlineData("* a \n* b ", "⟦ul:⟦li:a ⟧⟦li:b ⟧⟧")]
    [InlineData("* * a", "⟦ul:⟦li:⟧⟦li:a⟧⟧")]
    [InlineData("* * * a", "⟦ul:⟦li:⟧⟦li:⟧⟦li:a⟧⟧")]
    [InlineData("** * a", "⟦ul:⟦ul:⟦li:⟧⟧⟦li:a⟧⟧")]
    [InlineData("0. a", "⟦ol:⟦li:a⟧⟧")]
    [InlineData("0. a\n0. b", "⟦ol:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("0.0. a", "⟦ol:⟦ol:⟦li:a⟧⟧⟧")]
    [InlineData("0. a\n0.0. b\n0. c", "⟦ol:⟦li:a⟧⟦ol:⟦li:b⟧⟧⟦li:c⟧⟧")]
    [InlineData("1. a", "1. a")]
    [InlineData("0.a", "0.a")]
    [InlineData(" 0. a", "⟦ol:⟦li:a⟧⟧")]
    [InlineData("0.  a", "⟦ol:⟦li:a⟧⟧")]
    [InlineData("0.\ta", "⟦ol:⟦li:a⟧⟧")]
    [InlineData("0. 0. a", "⟦ol:⟦li:⟧⟦li:a⟧⟧")]
    [InlineData("0. a\n* b", "⟦ol:⟦li:a⟧⟧⟦ul:⟦li:b⟧⟧")]
    [InlineData("* a\n0. b", "⟦ul:⟦li:a⟧⟧⟦ol:⟦li:b⟧⟧")]
    [InlineData("* a\n0. b\n* c", "⟦ul:⟦li:a⟧⟧⟦ol:⟦li:b⟧⟧⟦ul:⟦li:c⟧⟧")]
    [InlineData("0.0.0. a\n0. b", "⟦ol:⟦ol:⟦ol:⟦li:a⟧⟧⟧⟦li:b⟧⟧")]
    [InlineData("00. a", "00. a")]
    [InlineData("0.. a", "0.. a")]
    [InlineData("0. a\n\n0. b", "⟦ol:⟦li:a⟧⟧\n⟦ol:⟦li:b⟧⟧")]
    [InlineData("---\n* a", "⟦hr⟧⟦ul:⟦li:a⟧⟧")]
    [InlineData("* ---\n* b", "⟦ul:⟦li:⟦hr⟧⟧⟦li:b⟧⟧")]
    [InlineData("* a\n---\nb", "⟦ul:⟦li:a⟧⟧⟦hr⟧b")]
    [InlineData("* ---", "⟦ul:⟦li:⟦hr⟧⟧⟧")]
    [InlineData("* 0. a", "⟦ul:⟦li:⟦ol:⟦li:⟧⟧a⟧⟧")]
    [InlineData("0. * a", "⟦ol:⟦li:⟦ul:⟦li:⟧⟧a⟧⟧")]
    [InlineData("[* a]", "* a")]
    [InlineData("[\n* a\n* b\n]", "\n⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("|h>[\n* a\n]", "\n⟦ul:⟦li:a⟧⟧")]
    [InlineData("(if: true)[\n* a\n]", "\n⟦ul:⟦li:a⟧⟧")]
    [InlineData("(if: true)[* a]", "* a")]
    [InlineData("* [a\nb]\n* c", "⟦ul:⟦li:a\nb⟧⟦li:c⟧⟧")]
    [InlineData("* [x]\n* |h>[y]", "⟦ul:⟦li:x⟧⟦li:y⟧⟧")]
    [InlineData("* a |h>[\n* b\n] c\n* d", "⟦ul:⟦li:a \n⟦ul:⟦li:b⟧⟧ c⟧⟦li:d⟧⟧")]
    [InlineData("[\n* a]\n* b", "\n⟦ul:⟦li:a⟧⟧\n⟦ul:⟦li:b⟧⟧")]
    [InlineData("* (link: \"x\")[y]\n* (print: 1)", "⟦ul:⟦li:⟦link:x⟧⟧⟦li:1⟧⟧")]
    [InlineData("* (set: $a to 1)a", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("(set: $a to 1)\n* a", "\n⟦ul:⟦li:a⟧⟧")]
    [InlineData("* [[a->b]]\n* [[c->b]]", "⟦ul:⟦li:⟦link:a⟧⟧⟦li:⟦link:c⟧⟧⟧")]
    [InlineData("* (if: true)\n[x]", "⟦ul:⟦li:⟦error⟧⟧⟧x")]
    [InlineData("* (if: false)[a]\n* (else:)[b]", "⟦ul:⟦li:⟧⟦li:b⟧⟧")]
    [InlineData("(set: _t to 1)\n* _t", "\n⟦ul:⟦li:1⟧⟧")]
    [InlineData("* $nope", "⟦ul:⟦li:0⟧⟧")]
    [InlineData("(print: \"* a\n* b\")", "⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("(print: \"* a\")", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("x(print: \"* a\")", "x⟦ul:⟦li:a⟧⟧")]
    [InlineData("(print: \"a\n* b\")", "a\n⟦ul:⟦li:b⟧⟧")]
    [InlineData("(for: each _i, 1, 2)[\n* _i]", "\n⟦ul:⟦li:1⟧⟧\n⟦ul:⟦li:2⟧⟧")]
    [InlineData("(for: each _i, 1, 2)[* _i\n]", "* 1\n* 2\n")]
    [InlineData("{\n* a\n* b\n}", "⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("{* a}", "* a")]
    [InlineData("{\n  * a\n  * b\n} c", "⟦ul:⟦li:a⟧⟦li:b⟧⟧ c")]
    [InlineData("x {\n* a\n}", "x ⟦ul:⟦li:a⟧⟧")]
    [InlineData("{\n* a  b \n* c\n}", "⟦ul:⟦li:a b ⟧⟦li:c⟧⟧")]
    [InlineData("{\n* (print: \" a \")\n}", "⟦ul:⟦li: a ⟧⟧")]
    [InlineData("{\n* a\n}\n* b", "⟦ul:⟦li:a⟧⟧\n⟦ul:⟦li:b⟧⟧")]
    [InlineData("* ''a\n* b''", "⟦ul:⟦li:⟦b:a\n⟦ul:⟦li:b⟧⟧⟧⟧⟧")]
    [InlineData("''* a''", "⟦b:* a⟧")]
    [InlineData("''x\n* a''", "⟦b:x\n⟦ul:⟦li:a⟧⟧⟧")]
    [InlineData("* //a//\n* b", "⟦ul:⟦li:⟦i:a⟧⟧⟦li:b⟧⟧")]
    [InlineData("*a*\n* b", "⟦i:a⟧\n⟦ul:⟦li:b⟧⟧")]
    [InlineData("<ul>\n* a\n</ul>x", "⟦ul:\n⟦ul:⟦li:a⟧⟧⟧x")]
    [InlineData("<b>* a</b>", "⟦b:* a⟧")]
    [InlineData("<br>* a", "\n* a")]
    [InlineData("<!-- c -->\n* a", "\n⟦ul:⟦li:a⟧⟧")]
    [InlineData("<!-- c -->* a", "* a")]
    [InlineData("<ol>\n0. a\n</ol>x", "⟦ol:\n⟦ol:⟦li:a⟧⟧⟧x")]
    [InlineData("<div>\n* a\n</div>", "\n⟦ul:⟦li:a⟧⟧")]
    [InlineData("`x`\n* a", "x\n⟦ul:⟦li:a⟧⟧")]
    [InlineData("* `a`", "⟦ul:⟦li:a⟧⟧")]
    [InlineData("* `a\nb`\n* c", "⟦ul:⟦li:a\nb⟧⟦li:c⟧⟧")]
    [InlineData("|h)[\n* a\n](show: ?h)", "\n⟦ul:⟦li:a⟧⟧")]
    [InlineData("* a\n(replace: ?passage)[* b]", "* b")]
    [InlineData("(append: ?passage)[\n* b]", "\n⟦ul:⟦li:b⟧⟧")]
    [InlineData("a\n(append: ?h)[\n* b]|h>[]", "a\n")]
    [InlineData("* a [=b\n* c", "⟦ul:⟦li:a b⟧⟦li:c⟧⟧")]
    [InlineData("* a {=b\n* c", "⟦ul:⟦li:a b⟧⟦li:c⟧⟧")]
    [InlineData("* a |h>[=b\n* c", "⟦ul:⟦li:a b⟧⟦li:c⟧⟧")]
    [InlineData("* ''(if: true)\n[x]''", "⟦ul:⟦li:⟦b:x⟧⟧⟧")]
    [InlineData("* (if: true) [x]", "⟦ul:⟦li:x⟧⟧")]
    [InlineData("* (if: true)\n\n[x]", "⟦ul:⟦li:⟦error⟧⟧⟧\nx")]
    [InlineData("[=\n* a\n* b", "\n⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("{=\n* a\n* b", "⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("* a\n[=b\n* c", "⟦ul:⟦li:a⟧⟧b\n⟦ul:⟦li:c⟧⟧")]
    [InlineData("* (link: \"x\")\n[y]", "⟦ul:⟦li:⟦error⟧⟧⟧y")]
    [InlineData("* $c\n[y]", "⟦ul:⟦li:0⟧⟧y")]
    [InlineData("(set: $c to (b:))* $c\n[y]", "⟦error⟧* 0\ny")]
    [InlineData("* a\n  b", "⟦ul:⟦li:a⟧⟧  b")]
    [InlineData("* a <br>\n* b", "⟦ul:⟦li:a \n⟧⟦li:b⟧⟧")]
    [InlineData("* a\\n* b", "⟦ul:⟦li:a\\n* b⟧⟧")]
    [InlineData("* (if: false)[=b\n* c", "⟦ul:⟦li:b⟧⟦li:c⟧⟧")]
    [InlineData("* a {=  b   c\n* d", "⟦ul:⟦li:a   b   c⟧⟦li:d⟧⟧")]
    [InlineData("* |h)[=b\n* c", "⟦ul:⟦li:b⟧⟦li:c⟧⟧")]
    [InlineData("* a (if: false)[=b\nc", "⟦ul:⟦li:a b⟧⟧c")]
    [InlineData("(if: false)[=b\n* c", "")]
    [InlineData("* a [=(if: false)[x] b\n* c", "⟦ul:⟦li:a  b⟧⟦li:c⟧⟧")]
    [InlineData("* (if: true) ''x\n[y]''", "⟦ul:⟦li:⟦error⟧ ⟦b:x\ny⟧⟧⟧")]
    [InlineData("* (if: true) [y]\n* b", "⟦ul:⟦li:y⟧⟦li:b⟧⟧")]
    [InlineData("|h)[\n* a\n* b\n](show: ?h)", "\n⟦ul:⟦li:a⟧⟦li:b⟧⟧")]
    [InlineData("|h)[* a](show: ?h)", "* a")]
    public void MatchesReference(string source, string expected) => Assert.Equal(expected, Shown(source));

    // Reference writes one more closing tag than it opened, and when a list
    // sits inside a {…} span or formatting inside a list item, that surplus
    // </ul> closes the outer list early, so " c" leaves the item. Here the
    // tags balance and " c" stays in it.
    [Theory]
    [InlineData("* {\n* b\n} c", "⟦ul:⟦li:⟦ul:⟦li:b⟧⟧ c⟧⟧")]
    [InlineData("* a {x\n* b\n} c\n* d", "⟦ul:⟦li:a x ⟦ul:⟦li:b⟧⟧ c⟧⟦li:d⟧⟧")]
    public void KeepsItsTagsBalanced(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Fact]
    public void ReachesHostsAsBalancedHtmlEvents()
    {
      var html = new StringBuilder();
      foreach (var e in new StorySession(Story("* a\n*** b\n** c"), 42).Render().Entries)
        if (e.Kind == BufferedRenderOutput.Kind.Html) html.Append(e.Content);
      Assert.Equal("<ul><li></li><ul><ul><li></li></ul><li></li></ul></ul>", html.ToString());
    }

    [Theory]
    [InlineData("* a", "* ")]
    [InlineData("  ** a", "  ** ")]
    [InlineData("0.0.\ta", "0.0.\t")]
    [InlineData("x\n* a", "* ")]
    public void TokenizesTheMarkerAsWritten(string source, string marker)
    {
      var token = new List<Token>(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source))
        .Find(t => t.Type == TokenType.ListMarker);
      Assert.NotNull(token);
      Assert.Equal(marker, token.Value);
    }

    [Theory]
    [InlineData("*a")]
    [InlineData("x * a")]
    [InlineData("[* a]")]
    [InlineData("1. a")]
    [InlineData("<br>* a")]
    public void IsNoMarkerAwayFromTheStartOfALine(string source)
      => Assert.DoesNotContain(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source), t => t.Type == TokenType.ListMarker);

    [Theory]
    [InlineData("* a\n** b\n* c\nafter")]
    [InlineData("0. a\n0.0. b\n\n* c")]
    [InlineData("* * a\n* ---\n* b")]
    [InlineData("  *   a [b\nc] d\n* e")]
    [InlineData("* 0. a")]
    [InlineData("* a [=b\n* c")]
    [InlineData("* a {=  b\n* c")]
    [InlineData("* ''a\n* b''")]
    public void MarkupPrinter_RoundTrips(string source)
      => Assert.Equal(source, new MarkupPrinter().Print(Parse(source)));

    [Fact]
    public void Parser_GroupsItemsByDepthAndKind()
    {
      var body = Parse("* a\n** b\n0. c");
      var bullets = Assert.IsType<ListNode>(body.Children[0]);
      Assert.False(bullets.Ordered);
      Assert.Equal(new[] { 1, 2 }, new[] { bullets.Items[0].Depth, bullets.Items[1].Depth });
      Assert.True(bullets.Items[1].EndsLine);
      var numbers = Assert.IsType<ListNode>(body.Children[1]);
      Assert.True(numbers.Ordered);
      Assert.False(Assert.Single(numbers.Items).EndsLine);
    }

    [Fact]
    public void ABrokenMacroInAnItem_EndsTheItemAtItsLineBreak()
    {
      var list = Assert.IsType<ListNode>(Assert.Single(Parse("* (if: 1 +)\n* b").Children));
      Assert.Equal(2, list.Items.Count);
      Assert.IsType<ParseErrorNode>(Assert.Single(list.Items[0].Children));
      Assert.Equal("⟦ul:⟦li:⟦error⟧⟧⟦li:b⟧⟧", Shown("* (if: 1 +)\n* b"));
    }

    [Fact]
    public void LinksInItems_AreBranchesAndBrokenLinks()
    {
      var story = Story("* [[Go->b]]\n* ''[[Lost->nowhere]]''");
      var branches = story.GetPassage("test").Branches;
      Assert.Equal(2, branches.Count);
      Assert.Equal("nowhere", Assert.Single(story.GetBrokenLinks()).Target);
    }

    [Fact]
    public void ParseErrorsInItems_AreReported()
      => Assert.Single(Story("* a\n* (if: 1 +)").GetParseErrors());

    [Fact]
    public void AHiddenHookHoldingAList_IsChargedForIt()
      => Assert.Equal("\n⟦ul:⟦li:a⟧⟦li:b⟧⟧", Shown("|h)[\n* a\n* b\n](show: ?h)"));
  }
}
