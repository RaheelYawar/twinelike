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

    // ----- Harlowe 3: no comment markup -----
    //
    // Every fact above runs under the newest profile (the parameterless
    // helpers). Harlowe 3 has no `comment` rule at all, so `--` is ordinary
    // prose there and each behaviour above inverts. The `<!-- … -->` HTML
    // form exists in both majors and is deliberately not mirrored here.

    private static IReadOnlyList<Token> TokenizeV3(string src)
      => new HarloweTokenizer(HarloweProfile.V3).Tokenize(src);

    private static PassageBody ParseV3(string src)
      => new HarloweBodyParser().Parse(TokenizeV3(src), src);

    private static BufferedRenderOutput RenderRawV3(string src)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext
      {
        Store = new HarloweVariableStore(),
        Invoker = registry,
        Profile = HarloweProfile.V3,
      };
      registry.Context = ctx;
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(ParseV3(src));
      return buf;
    }

    private static string RenderTextV3(string src) => RenderRawV3(src).Text;

    private static string RoundTripV3(string src) => new MarkupPrinter().Print(ParseV3(src));

    [Fact]
    public void V3_Tokenize_DoubleHyphen_StaysOneProseRun()
    {
      // The inversion of Tokenize_DoubleHyphen_EmitsComment: no Comment token,
      // and — because guard 3 leaves the prose run unbroken — not a
      // fragmented "a" / "--" / "b" trio either. One Text token.
      var t = TokenizeV3("a--b");
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal("a--b", t[0].Value);
      Assert.Equal(TokenType.EndOfFile, t[1].Type);
    }

    [Fact]
    public void V3_Tokenize_EmDashIdiom_StaysOneProseRun()
    {
      // The bug this switch exists to fix: under the newest profile the first
      // `--` comments out the rest of the sentence.
      const string src = "it was -- and remains -- fine";
      var t = TokenizeV3(src);
      Assert.Equal(TokenType.Text, t[0].Type);
      Assert.Equal(src, t[0].Value);
      Assert.Equal(TokenType.EndOfFile, t[1].Type);
    }

    [Fact]
    public void V3_Tokenize_NoCommentTokenAnywhere()
    {
      foreach (var tok in TokenizeV3("a--b --[hook] --(set: $x to 5)"))
        Assert.NotEqual(TokenType.Comment, tok.Type);
    }

    [Fact]
    public void V3_Tokenize_ExpressionDoubleHyphen_IsSubtractionThenNegation()
    {
      // With the expression-mode emit suppressed, `3--2` falls through to the
      // operator arm twice: 3 - (-2). No Comment token, and the `-type`
      // suffix scan is reachable again.
      var t = TokenizeV3("(print: 3--2)");
      Assert.Equal(TokenType.MacroOpen, t[0].Type);
      Assert.Equal(TokenType.NumberLiteral, t[1].Type);
      Assert.Equal("3", t[1].Value);
      Assert.Equal(TokenType.Operator, t[2].Type);
      Assert.Equal("-", t[2].Value);
      Assert.Equal(TokenType.Operator, t[3].Type);
      Assert.Equal("-", t[3].Value);
      Assert.Equal(TokenType.NumberLiteral, t[4].Type);
      Assert.Equal("2", t[4].Value);
    }

    [Fact]
    public void V3_Tokenize_HtmlComment_StillOneToken()
    {
      // Not gated: `<!-- … -->` predates the `--` markup and exists in both
      // majors. It is scanned at the '<' dispatch, so it never reaches the
      // guarded '-' arm.
      var t = TokenizeV3("a <!-- note --> b");
      Assert.Equal(TokenType.HtmlComment, t[1].Type);
      Assert.Equal("<!-- note -->", t[1].Value);
    }

    // ----- Harlowe 3: prose -----

    [Fact]
    public void V3_EmDashIdiom_RendersWhole()
    {
      // The headline: the live rendering bug this switch exists to fix. Under
      // the newest profile this truncates to "it was ".
      Assert.Equal("it was -- and remains -- fine", RenderTextV3("it was -- and remains -- fine"));
    }

    [Fact]
    public void V3_LineComment_ShowsProse()
    {
      Assert.Equal("A --hidden note\nB", RenderTextV3("A --hidden note\nB"));
    }

    [Fact]
    public void V3_ProseBeforeMacro_BothShow()
    {
      Assert.Equal("--hidden shown", RenderTextV3("--hidden (print: \"shown\")"));
    }

    [Fact]
    public void V3_DashesAtLineEnd_KeepTheLineBreak()
    {
      Assert.Equal("A--\nB", RenderTextV3("A--\nB"));
    }

    [Fact]
    public void V3_DashesAtEndOfInput_StayProse()
    {
      Assert.Equal("tail--", RenderTextV3("tail--"));
    }

    [Fact]
    public void V3_DashesBeforeVariable_VariableStillPrints()
    {
      Assert.Equal("--X after", RenderTextV3("(set: $x to \"X\")--$x after"));
    }

    [Fact]
    public void V3_DashesBeforeFormatSpan_SpanStillStyled()
    {
      var buf = RenderRawV3("--''bold'' tail");
      Assert.Equal("--bold tail", buf.Text);
      int styles = 0;
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.PushStyle) styles++;
      Assert.Equal(1, styles);
    }

    [Fact]
    public void V3_DashesBeforeLink_LinkStillEmitted()
    {
      var buf = RenderRawV3("--[[Somewhere]]done");
      int links = 0;
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.Link) links++;
      Assert.Equal(1, links);
      Assert.Equal("--done", buf.Text);
    }

    // ----- Harlowe 3: `--[` is prose plus an ordinary hook -----

    [Fact]
    public void V3_DashesBeforeHook_HookContentShows()
    {
      // Not a comment hook — just prose `--` followed by an anonymous hook,
      // whose content renders normally.
      Assert.Equal("A--a multi\nline noteB", RenderTextV3("A--[a multi\nline note]B"));
    }

    [Fact]
    public void V3_DashesBeforeHook_MacroInsideRuns()
    {
      // The inversion of CommentHook_MacroInside_NeverRuns: the assignment
      // inside the hook takes effect, so $x is 5.
      Assert.Equal("--5", RenderTextV3("(set: $x to 1)--[(set: $x to 5)](print: $x)"));
    }

    [Fact]
    public void V3_UnclosedHook_ContentStillShows()
    {
      var buf = RenderRawV3("A--[never closed\nB");
      Assert.Equal("A--never closed\nB", buf.Text);
      Assert.Equal(0, CountErrors(buf));
    }

    [Fact]
    public void V3_DashesInsideHook_StayProse()
    {
      Assert.Equal("A --note", RenderTextV3("(if: true)[A --note]"));
    }

    // ----- Harlowe 3: macros after `--` are live code -----

    [Fact]
    public void V3_DashesBeforeMacro_MacroRuns()
    {
      Assert.Equal("--hiddenshown", RenderTextV3("--(print: \"hidden\")(print: \"shown\")"));
    }

    [Fact]
    public void V3_DashesBeforeSetMacro_AssignmentHappens()
    {
      Assert.Equal("--5", RenderTextV3("(set: $x to 1)--(set: $x to 5)(print: $x)"));
    }

    [Fact]
    public void V3_SyntaxErrorAfterDashes_Surfaces()
    {
      // The inversion of Comment_SyntaxErrorInsideCommentedMacro_StaysSilent.
      // Under Harlowe 3 nothing is disabled, so the typo is a real error the
      // author must see — the `--` is just prose in front of broken code.
      var buf = RenderRawV3("--(set: $x ti 5) ok");
      Assert.Equal(1, CountErrors(buf));
      Assert.Equal("-- ok", buf.Text);
    }

    [Fact]
    public void V3_DashesBeforeChangerMacro_ChangerStillApplies()
    {
      // `(if: false)` genuinely hides its hook, and the `--` prints as prose.
      Assert.Equal("--", RenderTextV3("--(if: false)[shown]"));
    }

    // ----- Harlowe 3: expression mode is subtraction, not comments -----

    [Fact]
    public void V3_AdjacentNumbers_IsSubtractionOfANegative()
    {
      // The free win from suppressing the expression-mode emit: `5--3` falls
      // through to two operator arms and means 5 - (-3).
      Assert.Equal("8", RenderTextV3("(print: 5--3)"));
    }

    [Fact]
    public void V3_SpacedDashes_SubtractThenNegate()
    {
      // 3 - (-2) + 6 = 11, where Harlowe 4 slices the 2 out and gets 9.
      Assert.Equal("11", RenderTextV3("(print: 3 --2 + 6)"));
    }

    [Fact]
    public void V3_DashesInCollectionArg_ValueSurvives()
    {
      // (a: --2) is (a: 2) — one element, where Harlowe 4 empties the list.
      Assert.Equal("1", RenderTextV3("(print: (a: --2)'s length)"));
    }

    [Fact]
    public void V3_SpacedUnaryMinus_UnchangedAcrossProfiles()
    {
      // Profile-invariant: a spaced `- -2` never reached the comment arm in
      // either major. Pinned on both sides so a regression can't hide here.
      Assert.Equal("5", RenderTextV3("(print: 3 - -2)"));
      Assert.Equal("5", RenderText("(print: 3 - -2)"));
    }

    [Fact]
    public void V3_ReferenceDocExample_IsNowAParseError()
    {
      // Harlowe 4's own doc example, (set: $x to --2 6), assigns 6. Under
      // Harlowe 3 it reads as `$x to -(-2)` followed by a stray 6 — a genuine
      // syntax error, which is what a 3.x author would have seen.
      var buf = RenderRawV3("(set: $x to --2 6)(print: $x)");
      Assert.True(CountErrors(buf) > 0);
    }

    [Fact]
    public void V3_CommentHookInArgs_IsNowAParseError()
    {
      // `--[…]` in macro args is a code hook in Harlowe 4; in Harlowe 3 the
      // bracket has no meaning in expression position.
      var buf = RenderRawV3("(print: --[authorial note] 5)");
      Assert.True(CountErrors(buf) > 0);
    }

    // ----- Harlowe 3: HTML comments are unchanged -----

    [Fact]
    public void V3_HtmlComment_StillRendersNothing()
    {
      Assert.Equal("a  b", RenderTextV3("a <!-- note --> b"));
    }

    [Fact]
    public void V3_HtmlComment_StillNeverRunsItsContents()
    {
      var buf = RenderRawV3("(set: $x to 1)<!-- (set: $x to 5) -->(print: $x)");
      Assert.Equal("1", buf.Text);
      Assert.Equal(0, CountErrors(buf));
    }

    // ----- Harlowe 3: round-trip -----

    [Fact]
    public void V3_RoundTrip_ProseDashes_PreservedVerbatim()
    {
      Assert.Equal("A --hidden note\nB", RoundTripV3("A --hidden note\nB"));
    }

    [Fact]
    public void V3_RoundTrip_HtmlComment_PreservedVerbatim()
    {
      Assert.Equal("a <!-- note --> b", RoundTripV3("a <!-- note --> b"));
    }
  }
}
