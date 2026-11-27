using System.Text;
using System.Threading;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Unclosed hooks (<c>[=</c>) and the limits on span nesting, against
  /// reference's <c>unclosedHook</c> rule and <c>test/spec/markup/hooks.js</c>.
  /// </summary>
  public class UnclosedHookTests
  {
    private static HarloweStory Story(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "Start", RawBody = body });
      story.StartNode = story.GetPassage("Start").Pid;
      return story;
    }

    private static string Render(string body) => new StorySession(Story(body)).Render().Text;

    private static PassageBody Parse(string source)
      => new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source), source);

    [Theory]
    [InlineData("foo[=bar", "foobar")]
    [InlineData("foo[=====bar", "foobar")]
    [InlineData("[foo[==bar]baz", "foobarbaz")]
    [InlineData("foo[[==bar]baz", "foobarbaz")]
    [InlineData("[foo(if:false)[==bar]baz", "foobaz")]
    [InlineData("(if:false)[==gone", "")]
    [InlineData("(if:true)[==shown", "shown")]
    [InlineData("foo|grault>[==barbaz", "foobarbaz")]
    [InlineData("foo[|grault>[==barbaz](replace:?grault)[qux]", "fooqux")]
    [InlineData("[a[==b]c[==d", "abcd")]
    [InlineData("{[==  a   b  }  c", "a b  c")]
    public void UnclosedHook_RunsToTheEndOfItsContainer(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Fact]
    public void UnclosedHook_AttachesToAMacroAcrossWhitespace()
    {
      var macro = Assert.IsType<MacroNode>(Parse("(if: true)  [== rest").Children[0]);
      Assert.Equal("[==", macro.AttachedHook.UnclosedMarker);
      Assert.IsType<TextNode>(macro.AttachedHook.Children[0]);
    }

    [Fact]
    public void UnclosedHook_AttachesToAVariable()
    {
      var chain = Assert.IsType<ChangerChainNode>(Parse("$x[= rest").Children[0]);
      Assert.Equal("[=", chain.AttachedHook.UnclosedMarker);
    }

    [Fact]
    public void Comment_EliminatesANamedUnclosedHook_ButNotWhatFollows()
    {
      var buf = new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V4).Tokenize("a--|x>[=b"), "a--|x>[=b");
      var sb = new StringBuilder();
      foreach (var child in buf.Children)
        if (child is TextNode t) sb.Append(t.Content);
      Assert.Equal("ab", sb.ToString());
    }

    [Theory]
    [InlineData("{", TokenType.CollapseOpen, "{")]
    [InlineData("}", TokenType.CollapseClose, "}")]
    [InlineData("{=", TokenType.CollapseUnclosed, "{=")]
    [InlineData("{===", TokenType.CollapseUnclosed, "{===")]
    [InlineData("[=", TokenType.HookOpenUnclosed, "[=")]
    [InlineData("[===", TokenType.HookOpenUnclosed, "[===")]
    public void Tokenizer_RecognisesSpanMarkers(string source, TokenType type, string value)
    {
      var tokens = new HarloweTokenizer(HarloweProfile.V3).Tokenize(source);
      Assert.Equal(type, tokens[0].Type);
      Assert.Equal(value, tokens[0].Value);
      Assert.Equal(TokenType.EndOfFile, tokens[1].Type);
    }

    [Theory]
    [InlineData("--''bold [unmatched ''rest", "rest")]
    [InlineData("--{a [unmatched }rest", "rest")]
    [InlineData("--''a ] b''rest", "rest")]
    [InlineData("--{a ] b}rest", "rest")]
    [InlineData("[--''a ] b''", "a  b''")]
    public void Comment_SeesPastLiteralBracketsWhenEliminatingASpan(string source, string expected)
    {
      var story = new HarloweStory { FormatVersion = "4.0.0" };
      story.AddPassage(new HarlowePassage { Name = "Start", RawBody = source });
      story.StartNode = story.GetPassage("Start").Pid;
      Assert.Equal(expected, new StorySession(story).Render().Text);
    }

    [Theory]
    [InlineData("foo[=bar")]
    [InlineData("foo[===bar")]
    [InlineData("|n>[==bar")]
    [InlineData("(if: true)[==bar")]
    [InlineData("[a[=b]c")]
    public void MarkupPrinter_RoundTrips(string source)
      => Assert.Equal(source, new MarkupPrinter().Print(Parse(source)));

    // ----- Nesting limits: every one of these used to be able to kill the process -----

    private static string Repeat(string s, int n)
    {
      var sb = new StringBuilder(s.Length * n);
      for (int i = 0; i < n; i++) sb.Append(s);
      return sb.ToString();
    }

    public static TheoryData<string> DeepInputs()
    {
      const int n = 100000;
      return new TheoryData<string>
      {
        Repeat("{", n),
        Repeat("{", n) + Repeat("}", n),
        Repeat("{=", n),
        Repeat("[=", n),
        Repeat("[", n) + Repeat("]", n),
        Repeat("{= }", n),
        Repeat("{[", 5000) + Repeat("]}", 5000),
        Repeat("(if: true)[=", 5000),
        Repeat("|a>[=", n),
      };
    }

    [Theory]
    [MemberData(nameof(DeepInputs))]
    public void DeeplyNestedMarkup_LoadsRendersAndPrintsOnASmallStack(string source)
    {
      System.Exception caught = null;
      var thread = new Thread(() =>
      {
        try
        {
          var story = Story(source);
          story.GetBrokenLinks();
          story.GetParseErrors();
          new TweeWriter().Write(story);
          new MarkupPrinter().Print(story.GetPassage("Start").Ast);
          new StorySession(story).Render();
        }
        catch (System.Exception e) { caught = e; }
      }, 512 * 1024);
      thread.Start();
      thread.Join();
      Assert.Null(caught);
    }

    [Fact]
    public void DeepSpansInsideRecursion_FitASmallThreadStack()
    {
      string body = Repeat("{ ", 120) + "(display: \"Start\")" + Repeat(" }", 120);
      string text = null;
      var thread = new Thread(() => text = new StorySession(Story(body)).Render().Text, 512 * 1024);
      thread.Start();
      thread.Join();
      Assert.NotNull(text);
    }
  }
}
