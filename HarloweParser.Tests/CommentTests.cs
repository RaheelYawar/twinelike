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
  /// The comment family (reference Harlowe 4.0's <c>comment</c> markup in
  /// <c>ts/markup/patterns.ts</c>): the <c>--</c> marker eliminating the next
  /// fully-wrapped construct — a prose run, a comment hook <c>--[…]</c>
  /// (nestable), one macro call, one expression token inside macro args — plus
  /// <c>&lt;!-- … --&gt;</c> HTML comments rendering as nothing. Covers
  /// tokenization, both parsers' skip semantics, rendering, and Twee
  /// round-trip. Before this slice every form was shown to the player.
  /// </summary>
  public class CommentTests
  {
    private static IReadOnlyList<Token> Tokenize(string src) => new HarloweTokenizer().Tokenize(src);

    private static PassageBody Parse(string src)
      => new HarloweBodyParser().Parse(Tokenize(src), src);

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

    private static string RenderText(string src) => RenderRaw(src).Text;

    private static int CountErrors(BufferedRenderOutput buf)
    {
      int n = 0;
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.Error) n++;
      return n;
    }

    private static string RoundTrip(string src) => new MarkupPrinter().Print(Parse(src));

    // ----- Tokenizer -----

    [Fact]
    public void Tokenize_DoubleHyphen_EmitsComment()
    {
      var t = Tokenize("a--b");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal("a", t[0].Value);
      Assert.Equal(TokenType.Comment, t[1].Type);
      Assert.Equal(TokenType.Text, t[2].Type);
      Assert.Equal("b", t[2].Value);
    }

    [Fact]
    public void Tokenize_SingleHyphen_StaysText()
    {
      var t = Tokenize("well-known");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal("well-known", t[0].Value);
      Assert.Equal(TokenType.EndOfFile, t[1].Type);
    }

    [Fact]
    public void Tokenize_DoubleHyphenInsideLink_StaysLiteral()
    {
      // The link interior is a plain text run; comments are markup-suppressed
      // there, so a passage named "a--b" is still linkable.
      var t = Tokenize("[[a--b]]");
      Assert.Equal(TokenType.LinkOpen, t[0].Type);
      Assert.Equal(TokenType.Text, t[1].Type);
      Assert.Equal("a--b", t[1].Value);
      Assert.Equal(TokenType.LinkClose, t[2].Type);
    }

    [Fact]
    public void Tokenize_HtmlComment_OneTokenWithFullSource()
    {
      var t = Tokenize("a <!-- note --> b");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal(TokenType.HtmlComment, t[1].Type);
      Assert.Equal("<!-- note -->", t[1].Value);
      Assert.Equal(TokenType.Text, t[2].Type);
      Assert.Equal(" b", t[2].Value);
    }

    [Fact]
    public void Tokenize_UnterminatedHtmlComment_StaysProse()
    {
      var t = Tokenize("a <!-- never closed");
      foreach (var tok in t)
        Assert.NotEqual(TokenType.HtmlComment, tok.Type);
    }

    [Fact]
    public void Tokenize_ExpressionDoubleHyphen_EmitsComment()
    {
      // Reference's rule order puts `comment` before `subtraction`.
      var t = Tokenize("(print: 3--2)");
      Assert.Equal(TokenType.MacroOpen, t[0].Type);
      Assert.Equal(TokenType.NumberLiteral, t[1].Type);
      Assert.Equal(TokenType.Comment, t[2].Type);
      Assert.Equal(TokenType.NumberLiteral, t[3].Type);
    }

    // ----- Body: line comments -----

    [Fact]
    public void LineComment_HidesProseUpToLineBreak()
    {
      // The line break itself is NOT part of the comment — it ends it.
      Assert.Equal("A \nB", RenderText("A --hidden note\nB"));
    }

    [Fact]
    public void LineComment_EndsAtMacro_MacroStillRenders()
    {
      // Reference's documented behaviour: "--This is commented out
      // (print:\"But this isn't!\")" — the macro is still in the passage.
      Assert.Equal("shown", RenderText("--hidden (print: \"shown\")"));
    }

    [Fact]
    public void Comment_AtLineEnd_EatsTheLineBreak()
    {
      // The next token after `--` is the line break, so the lines join —
      // reference's renderer skips whatever the next token is.
      Assert.Equal("AB", RenderText("A--\nB"));
    }

    [Fact]
    public void Comment_AtEndOfInput_EliminatesNothing()
    {
      Assert.Equal("tail", RenderText("tail--"));
    }

    [Fact]
    public void Comment_BeforeVariable_HidesJustTheVariable()
    {
      Assert.Equal(" after", RenderText("(set: $x to \"X\")--$x after"));
    }

    [Fact]
    public void Comment_BeforeFormatSpan_EatsWholeSpan()
    {
      // Reference folds ''…'' into one token, so the comment eats it whole.
      Assert.Equal(" tail", RenderText("--''bold'' tail"));
    }

    [Fact]
    public void Comment_BeforeLink_EmitsNoLinkEvent()
    {
      var buf = RenderRaw("--[[Somewhere]]done");
      foreach (var e in buf.Entries)
        Assert.NotEqual(BufferedRenderOutput.Kind.Link, e.Kind);
      Assert.Equal("done", buf.Text);
    }

    [Fact]
    public void DoubleDashProse_HidesBetweenDashes()
    {
      // The em-dash idiom is markup in Harlowe 4.0: the first -- comments out
      // the prose run up to the second --, which then comments out the rest
      // of the line. Pinned so the (deliberate, reference-matching) breakage
      // is visible in the suite rather than a surprise.
      Assert.Equal("it was ", RenderText("it was -- and remains -- fine"));
    }

    // ----- Body: comment hooks -----

    [Fact]
    public void CommentHook_HidesMultilineContent()
    {
      Assert.Equal("AB", RenderText("A--[a multi\nline note]B"));
    }

    [Fact]
    public void CommentHook_Nested_OuterEatsInner()
    {
      // Reference: comment hooks nest — an outer --[ ] can enclose inner
      // comment hooks without their ] terminating it early.
      Assert.Equal("after", RenderText("--[outer --[inner note] more (set: $x to 1)]after"));
    }

    [Fact]
    public void CommentHook_MacroInside_NeverRuns()
    {
      Assert.Equal("1", RenderText("(set: $x to 1)--[(set: $x to 5)](print: $x)"));
    }

    [Fact]
    public void CommentHook_Unclosed_CommentsToEndOfInput()
    {
      var buf = RenderRaw("A--[never closed\nB");
      Assert.Equal("A", buf.Text);
      Assert.Equal(0, CountErrors(buf));
    }

    [Fact]
    public void Comment_InsideHook_ScopedToHook()
    {
      Assert.Equal("A ", RenderText("(if: true)[A --note]"));
    }

    // ----- Body: commented-out macros -----

    [Fact]
    public void Comment_BeforeMacro_MacroNeverRuns()
    {
      Assert.Equal("shown", RenderText("--(print: \"hidden\")(print: \"shown\")"));
    }

    [Fact]
    public void Comment_BeforeSetMacro_NoAssignment()
    {
      Assert.Equal("1", RenderText("(set: $x to 1)--(set: $x to 5)(print: $x)"));
    }

    [Fact]
    public void Comment_SyntaxErrorInsideCommentedMacro_StaysSilent()
    {
      // The macro is eliminated structurally, before anything parses its
      // args — reference removes the folded token wholesale, so a typo in
      // disabled code never surfaces.
      var buf = RenderRaw("--(set: $x ti 5) ok");
      Assert.Equal(" ok", buf.Text);
      Assert.Equal(0, CountErrors(buf));
    }

    [Fact]
    public void Comment_BeforeChangerMacro_AttachedHookSurvives()
    {
      // Reference skips only the macro's own token; the hook after it is not
      // part of that token and renders as an ordinary anonymous hook.
      Assert.Equal("shown", RenderText("--(if: false)[shown]"));
    }

    // ----- HTML comments -----

    [Fact]
    public void HtmlComment_RendersNothing()
    {
      // Reference's renderer case for htmlComment is an empty break. Before
      // this slice the whole thing flowed through as escaped prose text.
      Assert.Equal("a  b", RenderText("a <!-- note --> b"));
    }

    [Fact]
    public void HtmlComment_ContainingMacroText_NeverRunsIt()
    {
      var buf = RenderRaw("(set: $x to 1)<!-- (set: $x to 5) -->(print: $x)");
      Assert.Equal("1", buf.Text);
      Assert.Equal(0, CountErrors(buf));
    }

    // ----- Expression-mode comments -----

    [Fact]
    public void ExpressionComment_SkipsNextToken_ReferenceDocExample()
    {
      // The reference doc's own example: (set: $loyalty to --2 6) assigns 6.
      Assert.Equal("6", RenderText("(set: $x to --2 6)(print: $x)"));
    }

    [Fact]
    public void ExpressionComment_BetweenOperandAndOperator()
    {
      // 3 --2 + 6: the comment slices out the 2, leaving 3 + 6.
      Assert.Equal("9", RenderText("(print: 3 --2 + 6)"));
    }

    [Fact]
    public void ExpressionComment_AdjacentNumbers_BreakingChangePin()
    {
      // 5--3 was 5 - (-3) = 8 before the comment slice; in Harlowe 4.0 the
      // comment rule outranks subtraction, so the 3 is commented out.
      Assert.Equal("5", RenderText("(print: 5--3)"));
    }

    [Fact]
    public void ExpressionComment_SpacedUnaryMinus_StillSubtraction()
    {
      Assert.Equal("5", RenderText("(print: 3 - -2)"));
    }

    [Fact]
    public void ExpressionComment_CommentHookInArgs()
    {
      // Reference: "(goto: (cond:--[…] $easyMode, …))" — a comment hook in
      // macro args is a code hook, eliminated as one unit.
      Assert.Equal("5", RenderText("(print: --[authorial note] 5)"));
    }

    [Fact]
    public void ExpressionComment_SkipsWholeNestedMacroCall()
    {
      Assert.Equal("x", RenderText("(print: --(a: 1, 2) \"x\")"));
    }

    [Fact]
    public void ExpressionComment_OnlyArg_EmptyArgList()
    {
      // (a: --2) is (a:) — reference slices the 2 out before evaluation.
      Assert.Equal("0", RenderText("(print: (a: --2)'s length)"));
    }

    // ----- Round-trip -----

    [Fact]
    public void RoundTrip_LineComment_PreservedVerbatim()
    {
      Assert.Equal("A --hidden note\nB", RoundTrip("A --hidden note\nB"));
    }

    [Fact]
    public void RoundTrip_CommentHook_PreservedVerbatim()
    {
      Assert.Equal("--[a note]after", RoundTrip("--[a note]after"));
    }

    [Fact]
    public void RoundTrip_NestedCommentHook_PreservedVerbatim()
    {
      Assert.Equal("--[outer --[inner] tail]x", RoundTrip("--[outer --[inner] tail]x"));
    }

    [Fact]
    public void RoundTrip_CommentedMacro_PreservedVerbatim()
    {
      Assert.Equal("--(set: $x to 5) ok", RoundTrip("--(set: $x to 5) ok"));
    }

    [Fact]
    public void RoundTrip_HtmlComment_PreservedVerbatim()
    {
      Assert.Equal("a <!-- note --> b", RoundTrip("a <!-- note --> b"));
    }

    [Fact]
    public void RoundTrip_ExpressionComment_DropsCommentKeepsSemantics()
    {
      // Documented boundary: a token-skip comment inside macro args isn't
      // retained in the expression AST, so a dirty passage re-canonicalizes
      // without it — the evaluated meaning is unchanged. (Clean passages
      // round-trip verbatim through RawBody as always.)
      Assert.Equal("(set: $x to 6)", RoundTrip("(set: $x to --2 6)"));
    }
  }
}
