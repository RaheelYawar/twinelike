using System.Collections.Generic;
using System.Text;
using System.Threading;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Emphasis, verbatim and horizontal-rule markup, and how paired markup
  /// crosses. Every expected value in the theories was produced by real
  /// Harlowe 3.3.9 in a browser, except the bullets, which stay literal until
  /// lists are implemented.
  /// </summary>
  public class MarkupPairingTests
  {
    private static HarloweStory Story(string source, string version = "3.3.9")
    {
      var story = new HarloweStory { FormatVersion = version };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = source });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
      story.StartNode = story.GetPassage("test").Pid;
      return story;
    }

    /// <summary>Renders to a compact form with no escaping, so expected values stay readable.</summary>
    private static string Render(string source, string version = "3.3.9")
    {
      var sb = new StringBuilder();
      var open = new Stack<string>();
      foreach (var e in new StorySession(Story(source, version), 1).Render().Entries)
      {
        switch (e.Kind)
        {
          case BufferedRenderOutput.Kind.Text:
          case BufferedRenderOutput.Kind.Html:
            sb.Append(e.Content);
            break;
          case BufferedRenderOutput.Kind.Error:
            sb.Append("<error>");
            break;
          case BufferedRenderOutput.Kind.PushStyle:
            string tag = e.Style.Bold ? "b" : e.Style.Italic ? "i" : e.Style.Strikethrough ? "s" : "sup";
            sb.Append('<').Append(tag).Append('>');
            open.Push(tag);
            break;
          case BufferedRenderOutput.Kind.PopStyle:
            sb.Append("</").Append(open.Pop()).Append('>');
            break;
        }
      }
      return sb.ToString();
    }

    private static void FinishesQuickly(string source)
    {
      var worker = new Thread(() => new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source), source), 16 * 1024 * 1024)
      {
        IsBackground = true,
      };
      worker.Start();
      Assert.True(worker.Join(20000), "tokenizing and parsing did not finish");
    }

    private static string Repeat(string piece, int count)
    {
      var sb = new StringBuilder(piece.Length * count);
      for (int i = 0; i < count; i++) sb.Append(piece);
      return sb.ToString();
    }

    // ----- Emphasis -----

    [Theory]
    [InlineData("*a*", "<i>a</i>")]
    [InlineData("**a**", "<b>a</b>")]
    [InlineData("***a***", "<b><i>a</i></b>")]
    [InlineData("****a****", "a")]
    [InlineData("*a", "*a")]
    [InlineData("a*", "a*")]
    [InlineData("*a***", "<i>a</i>**")]
    [InlineData("***a*", "**<i>a</i>")]
    [InlineData("2 * 3 * 4", "2 <i> 3 </i> 4")]
    [InlineData("x *a* y", "x <i>a</i> y")]
    [InlineData("*a* *b*", "<i>a</i> <i>b</i>")]
    [InlineData("**a *b* c**", "<b>a <i>b</i> c</b>")]
    [InlineData("*a **b** c*", "<i>a </i><i>b</i><i> c</i>")]
    [InlineData("*a **b* c**", "<i>a </i><i>b</i> c**")]
    [InlineData("*a\nb*", "<i>a\nb</i>")]
    [InlineData("(print: \"*p*\")", "<i>p</i>")]
    [InlineData("{*a   b*}", "<i>a b</i>")]
    public void Emphasis_MatchesHarlowe(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("''a *b'' c*", "<b>a *b</b> c*")]
    [InlineData("''[x'']", "<b>[x</b>]")]
    [InlineData("*[a]*", "<i>a</i>")]
    [InlineData("[*a*]", "<i>a</i>")]
    [InlineData("*a [b* c]", "<i>a [b</i> c]")]
    [InlineData("[a *b] c*", "a *b c*")]
    [InlineData("{[x}]", "[x]")]
    [InlineData("[{x]}", "{x}")]
    public void PairedMarkup_CrossesLikeHarlowe(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("*~~~~*", "")]
    [InlineData("*<!-- c -->*", "")]
    [InlineData("*{}*", "")]
    public void EmptySpan_RendersNoStyle(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("* a *b*", "* a <i>b</i>")]
    [InlineData("** a", "** a")]
    [InlineData(" * a", " * a")]
    [InlineData("x\n* a", "x\n* a")]
    public void Bullet_IsNotEmphasis(string source, string expected)
      => Assert.Equal(expected, Render(source));

    // ----- Horizontal rules -----

    [Theory]
    [InlineData("---", "<hr>")]
    [InlineData("----", "<hr>")]
    [InlineData(" --- ", "<hr>")]
    [InlineData("\t---\t", "<hr>")]
    [InlineData("---\nb", "<hr>b")]
    [InlineData("a\n---", "a\n<hr>")]
    [InlineData("a\n---\nb", "a\n<hr>b")]
    [InlineData("a\n\n---\n\nb", "a\n\n<hr>\nb")]
    [InlineData("---\n---", "<hr><hr>")]
    [InlineData("--", "--")]
    [InlineData("--- a", "--- a")]
    [InlineData("a ---", "a ---")]
    [InlineData("---a", "---a")]
    [InlineData("[---]", "---")]
    [InlineData("[\n---\n]", "\n<hr>")]
    [InlineData("{---}", "---")]
    [InlineData("{\n---\n}", "<hr>")]
    [InlineData("(set: $v to 1)\n---", "\n<hr>")]
    [InlineData("(set: $v to 1)---", "---")]
    [InlineData("(print: \"a\n---\nb\")", "a\n<hr>b")]
    public void Rule_MatchesHarlowe(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Fact]
    public void BulletInsideAFoldedVerbatim_LeavesNoBlockStartBehind()
      => Assert.Equal("\n* ax<i> y</i>", Render("`\n* a`[x* y*]"));

    [Fact]
    public void Rule_OutranksCommentMarkupUnderHarlowe4()
      => Assert.Equal("<hr>x", Render("---\nx", "4.0.0"));

    [Fact]
    public void Rule_ReachesTheHostThroughTheHtmlChannel()
    {
      var entries = new StorySession(Story("---"), 1).Render().Entries;
      Assert.Single(entries);
      Assert.Equal(BufferedRenderOutput.Kind.Html, entries[0].Kind);
      Assert.Equal("<hr>", entries[0].Content);
    }

    // ----- Verbatim -----

    [Theory]
    [InlineData("`a`", "a")]
    [InlineData("``a``", "a")]
    [InlineData("`a", "`a")]
    [InlineData("a`", "a`")]
    [InlineData("`a``", "`a``")]
    [InlineData("``a`b``", "a`b")]
    [InlineData("`a`b`c`", "abc")]
    [InlineData("` a `", " a ")]
    [InlineData("``", "``")]
    [InlineData("`[a]`", "[a]")]
    [InlineData("`(print: 1)`", "(print: 1)")]
    [InlineData("`$a`", "$a")]
    [InlineData("`''a''`", "''a''")]
    [InlineData("`*a*`", "*a*")]
    [InlineData("`a\nb`", "a\nb")]
    [InlineData("`[[l->b]]`", "[[l->b]]")]
    [InlineData("`---`", "---")]
    [InlineData("`(print: \"`\")`", "(print: \"`\")")]
    [InlineData("(print: \"`a`\")", "a")]
    [InlineData("`a`[b]", "ab")]
    public void Verbatim_MatchesHarlowe(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("[`a]`]", "a]")]
    [InlineData("[a `b] c`", "[a b] c")]
    [InlineData("[a `b` c]", "a b c")]
    [InlineData("*a `b* c`", "*a b* c")]
    [InlineData("{a `b} c`", "{a b} c")]
    public void Verbatim_CannotBeCrossedByOtherClosers(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("{  `a   b`  }", "a   b")]
    [InlineData("{a `\n` b}", "a \nb")]
    public void Verbatim_KeepsItsWhitespaceWhenCollapsing(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Fact]
    public void Verbatim_ReachesTheHostAsEscapableText()
    {
      var entries = new StorySession(Story("`<b>x</b>`"), 1).Render().Entries;
      Assert.Single(entries);
      Assert.Equal(BufferedRenderOutput.Kind.Text, entries[0].Kind);
      Assert.Equal("<b>x</b>", entries[0].Content);
    }

    [Fact]
    public void Verbatim_HidesLinksFromDiagnosticsAndBranches()
    {
      var story = Story("`[[gone->nowhere]]`");
      Assert.Empty(story.GetBrokenLinks());
      Assert.Empty(story.GetPassage("test").Branches);
    }

    // ----- Links beside markup -----

    [Theory]
    [InlineData("[[[l->b]]", "[l")]
    [InlineData("|n>[[a]]", "a")]
    [InlineData("|n>[[a->b]]", "a->b")]
    public void LinkOpener_YieldsToAHookBracket(string source, string expectedText)
    {
      var text = new StringBuilder();
      foreach (var e in new StorySession(Story(source), 1).Render().Entries)
        if (e.Kind == BufferedRenderOutput.Kind.Text || e.Kind == BufferedRenderOutput.Kind.Link) text.Append(e.Content);
      Assert.Equal(expectedText, text.ToString());
    }

    // ----- Writing passages back out -----

    [Theory]
    [InlineData("*a* **b**")]
    [InlineData("***a***")]
    [InlineData("``a`b``")]
    [InlineData("` a\nb `")]
    [InlineData("a\n---\nb")]
    [InlineData("  ----  \nb")]
    [InlineData("*a `b* c")]
    public void Printer_RoundTrips(string source)
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source), source);
      Assert.Equal(source, new MarkupPrinter().Print(ast));
    }

    [Fact]
    public void Printer_EndsALastLineRuleSoLaterContentCanFollow()
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize("---"), "---");
      Assert.Equal("---\n", new MarkupPrinter().Print(ast));
    }

    // ----- Tokens -----

    [Fact]
    public void Tokenizer_RecordsEachPair()
    {
      var tokens = new HarloweTokenizer().Tokenize("*a* [b");
      Assert.Equal(TokenType.FormatDelimiter, tokens[0].Type);
      Assert.Equal(2, tokens[0].Partner);
      Assert.Equal(0, tokens[2].Partner);
      Assert.Equal(TokenType.HookOpen, tokens[4].Type);
      Assert.Equal(-1, tokens[4].Partner);
    }

    [Fact]
    public void Tokenizer_FoldsVerbatimIntoOneToken()
    {
      var tokens = new HarloweTokenizer().Tokenize("x``a`[b``y");
      Assert.Equal(TokenType.Verbatim, tokens[1].Type);
      Assert.Equal("``a`[b``", tokens[1].Value);
      Assert.Equal("y", tokens[2].Value);
    }

    // ----- Pathological input -----

    [Theory]
    [InlineData("*")]
    [InlineData("`")]
    [InlineData("` ")]
    [InlineData("* ")]
    [InlineData("''")]
    [InlineData("-")]
    [InlineData("---\n")]
    public void RepeatedMarkup_StaysLinear(string piece)
      => FinishesQuickly(Repeat(piece, 200000));

    [Fact]
    public void ManyUnmatchedFronts_DontMakeClosersQuadratic()
      => FinishesQuickly(new string('{', 150000) + new string(']', 150000));

    [Fact]
    public void ClosersBehindAVerbatimBarrier_StayLinear()
      => FinishesQuickly("[`" + new string('{', 150000) + new string(']', 150000));

    [Fact]
    public void NestedVerbatimRuns_StayLinear()
    {
      var sb = new StringBuilder();
      for (int k = 1200; k >= 1; k--) sb.Append('`', k).Append('x');
      sb.Append(new string('y', 500000));
      for (int k = 1; k <= 1200; k++) sb.Append('z').Append('`', k);
      FinishesQuickly(sb.ToString());
    }
  }
}
