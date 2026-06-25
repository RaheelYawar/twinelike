using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Inline text-formatting markup: <c>''bold''</c> and <c>//italic//</c>.
  /// Covers tokenization (doubled delimiters vs lone apostrophes/slashes and
  /// the <c>scheme://</c> URL guard), the body parser's pair-folding (including
  /// symmetric nesting and degrade-to-literal for crossed/unclosed delimiters),
  /// the render path through the semantic style channel, and Twee round-trip.
  /// </summary>
  public class InlineFormattingTests
  {
    private static IReadOnlyList<Token> Tokenize(string src) => new HarloweTokenizer().Tokenize(src);

    private static PassageBody Parse(string src)
      => new HarloweBodyParser().Parse(Tokenize(src));

    /// <summary>Render through the HTML adapter so bold/italic become &lt;b&gt;/&lt;i&gt;.</summary>
    private static string RenderHtml(string src)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;
      var buf = new BufferedRenderOutput();
      new BodyRenderer(new HtmlRenderOutput(buf), registry, ctx).Render(Parse(src));
      return buf.Text;
    }

    /// <summary>Render to the raw semantic buffer so PushStyle specs are inspectable.</summary>
    private static BufferedRenderOutput RenderRaw(string src)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(Parse(src));
      return buf;
    }

    private static string RoundTrip(string src) => new MarkupPrinter().Print(Parse(src));

    // ----- Tokenizer -----

    [Fact]
    public void Tokenize_Bold_EmitsDelimiters()
    {
      var t = Tokenize("''bold''");
      Assert.Equal(TokenType.FormatDelimiter, t[0].Type);
      Assert.Equal("''", t[0].Value);
      Assert.Equal(TokenType.Text, t[1].Type);
      Assert.Equal("bold", t[1].Value);
      Assert.Equal(TokenType.FormatDelimiter, t[2].Type);
      Assert.Equal("''", t[2].Value);
    }

    [Fact]
    public void Tokenize_Italic_EmitsDelimiters()
    {
      var t = Tokenize("//x//");
      Assert.Equal(TokenType.FormatDelimiter, t[0].Type);
      Assert.Equal("//", t[0].Value);
      Assert.Equal(TokenType.FormatDelimiter, t[2].Type);
      Assert.Equal("//", t[2].Value);
    }

    [Fact]
    public void Tokenize_SingleApostrophe_StaysText()
    {
      var t = Tokenize("it's fine");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal("it's fine", t[0].Value);
      Assert.Equal(TokenType.EndOfFile, t[1].Type);
    }

    [Fact]
    public void Tokenize_SingleSlash_StaysText()
    {
      var t = Tokenize("and/or");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal("and/or", t[0].Value);
    }

    [Fact]
    public void Tokenize_SchemeUrl_SlashesStayText()
    {
      // The // in http:// must not become an italic delimiter (reference's url
      // token consumes the whole URL).
      var t = Tokenize("http://example.com");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal("http://example.com", t[0].Value);
      Assert.Equal(TokenType.EndOfFile, t[1].Type);
    }

    // ----- Render -----

    [Fact]
    public void Render_Bold() => Assert.Equal("<b>bold</b>", RenderHtml("''bold''"));

    [Fact]
    public void Render_Italic() => Assert.Equal("<i>italic</i>", RenderHtml("//italic//"));

    [Fact]
    public void Render_BoldInSentence()
      => Assert.Equal("a <b>bold</b> word", RenderHtml("a ''bold'' word"));

    [Fact]
    public void Render_NestedSymmetric_BoldItalic()
      => Assert.Equal("<b><i>both</i></b>", RenderHtml("''//both//''"));

    [Fact]
    public void Render_ApostropheProse_Unaffected()
      => Assert.Equal("it's a plan", RenderHtml("it's a plan"));

    [Fact]
    public void Render_SingleUrl_NotItalicised()
      => Assert.Equal("http://a.com", RenderHtml("http://a.com"));

    [Fact]
    public void Render_TwoUrls_NothingItalicisedBetween()
      => Assert.Equal("http://a.com and http://b.com", RenderHtml("http://a.com and http://b.com"));

    // --- URL token (real scheme://… consumption, replacing the old `:` guard) ---

    [Fact]
    public void Tokenize_UrlMidProse_OneTextToken()
    {
      // A URL after leading prose is swallowed whole, not split at its //.
      var t = Tokenize("see http://a.com/x today");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal("see http://a.com/x today", t[0].Value);
      Assert.Equal(TokenType.EndOfFile, t[1].Type);
    }

    [Fact]
    public void Render_UrlWithDoubleSlashInPath_StaysLiteral()
    {
      // Was under-protected by the old one-char guard (only :// was guarded, so
      // the two path // paired into italics). The url token consumes it whole.
      Assert.Equal("http://a//b//c", RenderHtml("http://a//b//c"));
    }

    [Fact]
    public void Render_NonSchemeColonSlashes_Italicise()
    {
      // Was over-suppressed by the old guard (the closer // sat after `a:`).
      // `a:` is not a scheme, so // is ordinary italic markup → reference parity.
      Assert.Equal("<i>a:</i>", RenderHtml("//a://"));
    }

    [Fact]
    public void Render_NonSchemeWordColon_Italicises()
    {
      // `note:` is not a recognised scheme, so the //…// is italic.
      Assert.Equal("note:<i>text</i>", RenderHtml("note://text//"));
    }

    [Fact]
    public void Render_UrlThenItalic_BothHonoured()
    {
      // The URL is protected; a following //…// still italicises.
      Assert.Equal("http://x.com <i>yes</i>", RenderHtml("http://x.com //yes//"));
    }

    [Fact]
    public void Render_TrailingPeriodAfterUrl_StaysProse()
    {
      // Reference excludes trailing sentence punctuation from the url token.
      Assert.Equal("see http://x.com.", RenderHtml("see http://x.com."));
    }

    [Fact]
    public void Render_UnmatchedBold_DegradesToLiteral()
      => Assert.Equal("''bold", RenderHtml("''bold"));

    [Fact]
    public void Render_CrossedNesting_DegradesPerReference()
    {
      // Reference: ''//x''// folds the bold across, leaving both slashes literal
      // -> <b>//x</b>//. (Documented: "''//text''// will not work".)
      Assert.Equal("<b>//x</b>//", RenderHtml("''//x''//"));
    }

    [Fact]
    public void Render_Toggles_TwoSpans()
      => Assert.Equal("<b>a</b>b<b>c</b>", RenderHtml("''a''b''c''"));

    [Fact]
    public void Render_FormattingDoesNotCrossHookBoundary()
    {
      // The opening '' is outside the hook, the closing '' inside it: neither
      // pairs across the boundary, so both stay literal.
      Assert.Equal("''x''", RenderHtml("''[x'']"));
    }

    [Fact]
    public void Render_FormatInsideHook_Works()
      => Assert.Equal("<b>bold</b>", RenderHtml("(if: true)[''bold'']"));

    [Fact]
    public void RenderRaw_Bold_EmitsBoldPushStyle()
    {
      var buf = RenderRaw("''hi''");
      var push = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.PushStyle);
      Assert.NotNull(push);
      Assert.True(push.Style.Bold);
      Assert.False(push.Style.Italic);
      Assert.Contains(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.PopStyle);
    }

    [Fact]
    public void RenderRaw_Italic_EmitsItalicPushStyle()
    {
      var buf = RenderRaw("//hi//");
      var push = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.PushStyle);
      Assert.NotNull(push);
      Assert.True(push.Style.Italic);
      Assert.False(push.Style.Bold);
    }

    // ----- Branch collection (links inside formatting) -----

    [Fact]
    public void BranchCollection_FindsLinkInsideBold()
    {
      // Exercises BranchCollector.Visit(FormatNode) through the public loader:
      // a link wrapped in '' must still surface in the passage's Branches.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage { Name = "P", Body = "''[[Go->Prose]]''" });
      var p = story.GetPassage("P");
      Assert.Single(p.Branches);
      Assert.Equal("Prose", p.Branches[0].Name);
    }

    // ----- Twee round-trip -----

    [Fact]
    public void RoundTrip_Bold() => Assert.Equal("''bold''", RoundTrip("''bold''"));

    [Fact]
    public void RoundTrip_Italic() => Assert.Equal("//italic//", RoundTrip("//italic//"));

    [Fact]
    public void RoundTrip_Nested() => Assert.Equal("''//x//''", RoundTrip("''//x//''"));
  }
}
