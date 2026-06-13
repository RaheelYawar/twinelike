using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Direct tests for <see cref="HarloweBodyParser"/>. Drives the parser over
  /// real <see cref="HarloweTokenizer"/> output so the tests double as
  /// integration checks for the tokenizer + body parser + expression parser
  /// pipeline.
  /// </summary>
  public class HarloweBodyParserTests
  {
    /// <summary>
    /// Tokenises <paramref name="src"/> and parses the result into a
    /// <see cref="PassageBody"/>. Centralised so individual tests stay focused
    /// on the AST shape.
    /// </summary>
    private static PassageBody Parse(string src)
    {
      var tokens = new HarloweTokenizer().Tokenize(src);
      return new HarloweBodyParser().Parse(tokens);
    }

    // --- Plain content ---

    [Fact]
    public void EmptyInput_ProducesEmptyBody()
    {
      var body = Parse("");
      Assert.Empty(body.Children);
    }

    [Fact]
    public void PlainText_ProducesSingleTextNode()
    {
      var body = Parse("hello world");
      var node = Assert.Single(body.Children);
      Assert.Equal("hello world", Assert.IsType<TextNode>(node).Content);
    }

    [Fact]
    public void Newline_ProducesNewlineNode()
    {
      var body = Parse("a\nb");
      Assert.Equal(3, body.Children.Count);
      Assert.IsType<TextNode>(body.Children[0]);
      Assert.IsType<NewlineNode>(body.Children[1]);
      Assert.IsType<TextNode>(body.Children[2]);
    }

    [Fact]
    public void Variable_ProducesVariableNode()
    {
      var body = Parse("Hello $name.");
      Assert.Equal(3, body.Children.Count);
      var v = Assert.IsType<VariableNode>(body.Children[1]);
      Assert.Equal("name", v.Name);
      Assert.False(v.IsTemporary);
    }

    [Fact]
    public void TempVariable_ProducesVariableNodeWithTemporaryFlag()
    {
      var body = Parse("Hello _name.");
      var v = Assert.IsType<VariableNode>(body.Children[1]);
      Assert.Equal("name", v.Name);
      Assert.True(v.IsTemporary);
    }

    [Fact]
    public void HtmlTag_ProducesHtmlNode()
    {
      var body = Parse("Line<br/>break");
      Assert.Equal(3, body.Children.Count);
      var html = Assert.IsType<HtmlNode>(body.Children[1]);
      Assert.Equal("<br/>", html.RawHtml);
    }

    // --- Macros ---

    [Fact]
    public void Macro_ProducesMacroNodeWithExpressionArguments()
    {
      var body = Parse("(set: $x to 1)");
      var macro = Assert.IsType<MacroNode>(Assert.Single(body.Children));
      Assert.Equal("set", macro.Name);
      Assert.Single(macro.Arguments);
      var arg = Assert.IsType<BinaryOpNode>(macro.Arguments[0]);
      Assert.Equal("to", arg.Operator);
      Assert.Null(macro.AttachedHook);
    }

    [Fact]
    public void Macro_WithoutFollowingHook_HasNullAttachedHook()
    {
      var body = Parse("(print: 1)");
      var macro = Assert.IsType<MacroNode>(Assert.Single(body.Children));
      Assert.Null(macro.AttachedHook);
    }

    [Fact]
    public void Macro_FollowedImmediatelyByHook_AttachesHook()
    {
      var body = Parse("(if: $x)[hi]");
      var macro = Assert.IsType<MacroNode>(Assert.Single(body.Children));
      Assert.NotNull(macro.AttachedHook);
      Assert.Equal(HookAnchor.None, macro.AttachedHook.Anchor);
      Assert.Null(macro.AttachedHook.Name);
      var hookText = Assert.IsType<TextNode>(Assert.Single(macro.AttachedHook.Children));
      Assert.Equal("hi", hookText.Content);
    }

    [Fact]
    public void Macro_FollowedByHookAcrossWhitespace_StillAttaches()
    {
      var body = Parse("(if: $x)  [hi]");
      var macro = Assert.IsType<MacroNode>(Assert.Single(body.Children));
      Assert.NotNull(macro.AttachedHook);
    }

    [Fact]
    public void Macro_FollowedByHookAcrossNewline_StillAttaches()
    {
      var body = Parse("(if: $x)\n[hi]");
      var children = body.Children;
      var macro = Assert.IsType<MacroNode>(children[0]);
      Assert.NotNull(macro.AttachedHook);
    }

    [Fact]
    public void Macro_FollowedByOtherText_DoesNotAttachHook()
    {
      var body = Parse("(set: $x to 1) hello");
      Assert.Equal(2, body.Children.Count);
      var macro = Assert.IsType<MacroNode>(body.Children[0]);
      Assert.Null(macro.AttachedHook);
      Assert.IsType<TextNode>(body.Children[1]);
    }

    // --- Hooks ---

    [Fact]
    public void AnonymousHook_ProducesHookNodeWithNoneAnchor()
    {
      var body = Parse("[hello]");
      var hook = Assert.IsType<HookNode>(Assert.Single(body.Children));
      Assert.Null(hook.Name);
      Assert.Equal(HookAnchor.None, hook.Anchor);
      var text = Assert.IsType<TextNode>(Assert.Single(hook.Children));
      Assert.Equal("hello", text.Content);
    }

    [Fact]
    public void LeftAnchoredHook_PicksUpHookNameLeft()
    {
      var body = Parse("[hello]<greeting|");
      var hook = Assert.IsType<HookNode>(Assert.Single(body.Children));
      Assert.Equal("greeting", hook.Name);
      Assert.Equal(HookAnchor.Left, hook.Anchor);
    }

    [Fact]
    public void RightAnchoredHook_BindsHookNameRightToFollowingHook()
    {
      var body = Parse("|greeting>[hello]");
      var hook = Assert.IsType<HookNode>(Assert.Single(body.Children));
      Assert.Equal("greeting", hook.Name);
      Assert.Equal(HookAnchor.Right, hook.Anchor);
      Assert.Equal("hello", Assert.IsType<TextNode>(Assert.Single(hook.Children)).Content);
    }

    [Fact]
    public void NestedHook_RecursesIntoChildren()
    {
      var body = Parse("[outer [inner]]");
      var outer = Assert.IsType<HookNode>(Assert.Single(body.Children));
      Assert.Equal(2, outer.Children.Count);
      Assert.Equal("outer ", Assert.IsType<TextNode>(outer.Children[0]).Content);
      var inner = Assert.IsType<HookNode>(outer.Children[1]);
      Assert.Equal("inner", Assert.IsType<TextNode>(Assert.Single(inner.Children)).Content);
    }

    [Fact]
    public void HookContainingMacro_BothNodesAppear()
    {
      var body = Parse("[hi (set: $x to 1)]");
      var hook = Assert.IsType<HookNode>(Assert.Single(body.Children));
      Assert.Equal(2, hook.Children.Count);
      Assert.IsType<TextNode>(hook.Children[0]);
      Assert.IsType<MacroNode>(hook.Children[1]);
    }

    // --- Links ---

    [Fact]
    public void BareLink_TextEqualsTarget()
    {
      var body = Parse("[[Next]]");
      var link = Assert.IsType<LinkNode>(Assert.Single(body.Children));
      Assert.Equal("Next", link.Text);
      Assert.Equal("Next", link.Target);
    }

    [Fact]
    public void LinkWithRightArrow_LeftIsDisplayRightIsTarget()
    {
      var body = Parse("[[Continue->FirstPassage]]");
      var link = Assert.IsType<LinkNode>(Assert.Single(body.Children));
      Assert.Equal("Continue", link.Text);
      Assert.Equal("FirstPassage", link.Target);
    }

    [Fact]
    public void LinkWithLeftArrow_LeftIsTargetRightIsDisplay()
    {
      var body = Parse("[[FirstPassage<-Continue]]");
      var link = Assert.IsType<LinkNode>(Assert.Single(body.Children));
      Assert.Equal("Continue", link.Text);
      Assert.Equal("FirstPassage", link.Target);
    }

    [Fact]
    public void LinkInsideProse_SitsBetweenTextNodes()
    {
      var body = Parse("Click [[here->Target]] now.");
      Assert.Equal(3, body.Children.Count);
      Assert.IsType<TextNode>(body.Children[0]);
      var link = Assert.IsType<LinkNode>(body.Children[1]);
      Assert.Equal("here", link.Text);
      Assert.Equal("Target", link.Target);
      Assert.IsType<TextNode>(body.Children[2]);
    }

    // --- Mixed end-to-end ---

    [Fact]
    public void Set_AcceptsTopLevelTo()
    {
      // (set:)'s arg list is the canonical home for `to`; the body parser
      // recognises the macro name and routes through ParseArgumentList with
      // assignment allowed.
      var body = Parse("(set: $x to 5)");
      var macro = Assert.IsType<MacroNode>(body.Children[0]);
      Assert.Equal("set", macro.Name);
      var assign = Assert.IsType<BinaryOpNode>(macro.Arguments[0]);
      Assert.Equal("to", assign.Operator);
    }

    [Fact]
    public void Put_AcceptsTopLevelInto()
    {
      var body = Parse("(put: 5 into $x)");
      var macro = Assert.IsType<MacroNode>(body.Children[0]);
      Assert.Equal("put", macro.Name);
      var assign = Assert.IsType<BinaryOpNode>(macro.Arguments[0]);
      Assert.Equal("into", assign.Operator);
    }

    [Fact]
    public void NonAssignmentMacro_RejectsTopLevelTo()
    {
      // Any macro other than set/put must reject `to`/`into` in its arg list.
      // Without this guard `(print: $x to 5)` would silently mutate $x during
      // argument evaluation. The parser self-recovers and emits a
      // ParseErrorNode for the failing call instead of throwing — but the
      // diagnostic still fires before the (print:) MacroCallNode is produced,
      // so the assignment is never reachable from any evaluation path.
      var body = Parse("(print: $x to 5)");
      var err = Assert.IsType<ParseErrorNode>(body.Children[body.Children.Count - 1]);
      Assert.Contains("to", err.Message);
    }

    [Fact]
    public void IfMacro_RejectsTopLevelInto()
    {
      // (if:) reading a boolean is the classic shape; an `into` here used to
      // mutate during arg evaluation. Now it's a parse error materialized as
      // a ParseErrorNode in the body; per-node recovery resumes after the
      // broken macro's closing paren so trailing siblings (the `[ok]` hook)
      // may follow — we just verify the diagnostic landed somewhere in the
      // children, naming `into`.
      var body = Parse("(if: 5 into $x)[ok]");
      ParseErrorNode err = null;
      for (int i = 0; i < body.Children.Count; i++)
        if (body.Children[i] is ParseErrorNode n) { err = n; break; }
      Assert.NotNull(err);
      Assert.Contains("into", err.Message);
    }

    [Fact]
    public void MalformedArgumentList_EmitsParseErrorInsteadOfSilentDrop()
    {
      // (if: $x $y) — the expression ends at $x but the cursor lands on $y,
      // neither ',' nor ')'. ParseArgumentList used to bail silently: the
      // partial (if:) lost its hook attachment, [secret] became a free
      // anonymous hook, and guarded content rendered unconditionally.
      var body = Parse("(if: $x $y)[secret]");
      ParseErrorNode err = null;
      for (int i = 0; i < body.Children.Count; i++)
        if (body.Children[i] is ParseErrorNode n) { err = n; break; }
      Assert.NotNull(err);
      Assert.Contains("expected ',' or ')'", err.Message);
    }

    [Fact]
    public void MalformedArgumentList_StrayTokenAfterAssignment_EmitsParseError()
    {
      // (set: $x to 5 6) used to assign 5 and silently discard the 6.
      var body = Parse("(set: $x to 5 6)");
      ParseErrorNode err = null;
      for (int i = 0; i < body.Children.Count; i++)
        if (body.Children[i] is ParseErrorNode n) { err = n; break; }
      Assert.NotNull(err);
      Assert.Contains("got '6'", err.Message);
    }

    [Fact]
    public void DeeplyNestedParens_SurfaceParseError_NotStackOverflow()
    {
      // Thousands of nested '(' would otherwise recurse the expression parser
      // into an uncatchable StackOverflowException. The depth cap converts it
      // into an in-prose parse error; the macro recovers as a ParseErrorNode.
      var src = "(print: " + new string('(', 5000) + "1" + new string(')', 5000) + ")";
      var body = Parse(src);
      bool sawError = false;
      for (int i = 0; i < body.Children.Count; i++)
        if (body.Children[i] is ParseErrorNode) { sawError = true; break; }
      Assert.True(sawError);
    }

    [Fact]
    public void DeeplyNestedHooks_DoNotStackOverflow()
    {
      // Thousands of nested '[' with no closers would otherwise recurse the
      // body parser into a StackOverflowException. The depth cap keeps the
      // load alive (returns a tree rather than crashing the host).
      var body = Parse(new string('[', 5000));
      Assert.NotNull(body);
    }

    [Fact]
    public void ModeratelyNestedParens_ParseWithoutError()
    {
      // Nesting well under the cap must still parse cleanly — the ceiling only
      // guards against degenerate depth, not ordinary grouping.
      var src = "(print: " + new string('(', 40) + "1" + new string(')', 40) + ")";
      var body = Parse(src);
      for (int i = 0; i < body.Children.Count; i++)
        Assert.IsNotType<ParseErrorNode>(body.Children[i]);
    }

    [Fact]
    public void ParseError_PreservesValidPrefix()
    {
      // Per-node parser recovery: when one node throws partway through, the
      // body parser captures the diagnostic as a ParseErrorNode and keeps
      // every successfully-parsed sibling that came before. Previously the
      // whole AST was discarded by loader-level catch, so a passage like
      // `[[Town]] (oops to` lost its branches even though tokenization +
      // parsing of `[[Town]]` had already succeeded.
      var body = Parse("Hello [[Town]] middle (notamacro: $x to 5) trailing");
      // Prefix nodes survive — at minimum the link and the leading text.
      bool sawLink = false;
      bool sawError = false;
      for (int i = 0; i < body.Children.Count; i++)
      {
        if (body.Children[i] is LinkNode) sawLink = true;
        if (body.Children[i] is ParseErrorNode) sawError = true;
      }
      Assert.True(sawLink, "valid prefix link should survive parser recovery");
      Assert.True(sawError, "failed node should be replaced by a ParseErrorNode");
    }

    [Fact]
    public void ParseError_InsideHook_PreservesOuterStructure()
    {
      // Per-node recovery inside a hook left the cursor mid-stream, so the
      // hook's closing `]` was never consumed and trailing prose got
      // reparented to the outer scope — corrupting the AST shape and any
      // downstream branch/round-trip processing. The recovery now advances
      // to the balanced terminator so the hook closes cleanly and outer
      // siblings remain at the outer level.
      var body = Parse("before [content (badmacro: $x to ) trailing] after");
      // The first node is the leading "before " text.
      Assert.IsType<TextNode>(body.Children[0]);
      // The hook is the second meaningful child (the parser surfaces it as a
      // HookNode at the outer scope, not as text shoveled into the outer
      // sibling list).
      HookNode hook = null;
      bool sawAfter = false;
      for (int i = 0; i < body.Children.Count; i++)
      {
        if (body.Children[i] is HookNode h && hook == null) hook = h;
        if (body.Children[i] is TextNode t && t.Content.Contains("after")) sawAfter = true;
      }
      Assert.NotNull(hook);
      // The hook closed cleanly: trailing prose after `]` is a sibling of
      // the hook, not orphaned inside the hook's child list.
      Assert.True(sawAfter, "trailing prose after the broken hook should sit at the outer level");
      // The ParseErrorNode lives inside the hook, not at the outer scope.
      bool hookHasError = false;
      for (int i = 0; i < hook.Children.Count; i++)
        if (hook.Children[i] is ParseErrorNode) hookHasError = true;
      Assert.True(hookHasError, "ParseErrorNode should live inside the hook that contained the error");
    }

    [Fact]
    public void ParseError_InsideNestedHooks_RecoversAtCorrectLevel()
    {
      // The skip-to-balanced-terminator helper must track HookOpen/HookClose
      // depth: a parse error inside an inner hook should leave the inner hook
      // closed and the outer hook intact, even when intervening hook tokens
      // appear in the skipped region.
      var body = Parse("[outer [inner (bad to ) tail] more]");
      HookNode outer = null;
      for (int i = 0; i < body.Children.Count; i++)
        if (body.Children[i] is HookNode h) { outer = h; break; }
      Assert.NotNull(outer);
      // The outer hook should contain at minimum: prose, the inner hook, and
      // the "more" trailing text — all as direct outer children.
      bool sawInner = false, sawMore = false;
      for (int i = 0; i < outer.Children.Count; i++)
      {
        if (outer.Children[i] is HookNode) sawInner = true;
        if (outer.Children[i] is TextNode t && t.Content.Contains("more")) sawMore = true;
      }
      Assert.True(sawInner, "inner hook should be a child of the outer hook");
      Assert.True(sawMore, "post-inner-hook trailing prose should still sit inside the outer hook");
    }

    [Fact]
    public void MixedContent_ProducesExpectedSequence()
    {
      var body = Parse("Hello $name.\n(if: $brave)[Step inside]\n[[Continue->Next]]");
      Assert.Equal(7, body.Children.Count);
      Assert.IsType<TextNode>(body.Children[0]);
      Assert.IsType<VariableNode>(body.Children[1]);
      Assert.IsType<TextNode>(body.Children[2]);
      Assert.IsType<NewlineNode>(body.Children[3]);
      var macro = Assert.IsType<MacroNode>(body.Children[4]);
      Assert.NotNull(macro.AttachedHook);
      Assert.IsType<NewlineNode>(body.Children[5]);
      var link = Assert.IsType<LinkNode>(body.Children[6]);
      Assert.Equal("Continue", link.Text);
      Assert.Equal("Next", link.Target);
    }
  }
}
