using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;
using Harlowe.Tokens;

namespace Harlowe.Parsing
{
  /// <summary>
  /// Recursive-descent body parser. Walks a token stream once, dispatching
  /// each token to the body-AST node it produces. Macro arguments are handed
  /// off to an <see cref="IExpressionParser"/> so they come back as proper
  /// expression trees. Hooks contain body content, so the parser recurses
  /// into them via <see cref="ParseHookContents"/>.
  /// </summary>
  public class HarloweBodyParser : IBodyParser
  {
    private readonly IExpressionParser _expressionParser;

    // Current hook-nesting depth and its ceiling. Bounds runaway `[[[…]]]`
    // nesting to an in-prose parse error rather than a StackOverflowException;
    // restored via finally so it stays balanced across per-node recovery.
    private int _depth;
    private const int MaxDepth = 128;

    // Spans of `{`, `{=` and `[=` open around the cursor, which nest in the AST
    // just as hooks do and so count toward MaxDepth: those in enclosing hooks,
    // the `{`s still open at this level (a `}` can close one), and the unclosed
    // markers at this level (nothing closes them).
    private int _outerSpans;
    private int _levelBraces;
    private int _levelUnclosed;

    private bool AtNestingLimit => _depth + _outerSpans + _levelBraces + _levelUnclosed >= MaxDepth;

    // An unclosed hook just attached to the node being parsed. ParseNodes
    // places its marker right after that node, so the fold can give it the
    // rest of the sibling list.
    private UnclosedHookMarker _pendingUnclosedHook;

    public HarloweBodyParser() : this(new HarloweExpressionParser()) { }

    public HarloweBodyParser(IExpressionParser expressionParser)
    {
      _expressionParser = expressionParser;
    }

    /// <summary>
    /// Top-level entry: build a <see cref="PassageBody"/> from <paramref name="tokens"/>
    /// by parsing nodes until end-of-file. Per-node recovery leaves any
    /// resulting <see cref="ParseErrorNode.OriginalSource"/> null since the
    /// parser has no source string to slice — see the overload that takes
    /// the source text for round-trip-safe recovery.
    /// </summary>
    public PassageBody Parse(IReadOnlyList<Token> tokens) => Parse(tokens, null);

    /// <summary>
    /// Source-aware entry: same as <see cref="Parse(IReadOnlyList{Token})"/>,
    /// but per-node recovery slices <paramref name="source"/> with the
    /// failure-span token positions and stashes the slice on
    /// <see cref="ParseErrorNode.OriginalSource"/>. Loaders and
    /// <c>AddPassage</c> use this so partial recovery round-trips the broken
    /// sub-source through <see cref="Twee.MarkupPrinter"/> instead of silently
    /// dropping it when the consumer mutates the AST and re-saves.
    /// </summary>
    public PassageBody Parse(IReadOnlyList<Token> tokens, string source)
    {
      _outerSpans = _levelBraces = _levelUnclosed = 0;
      _pendingUnclosedHook = null;
      var cursor = new TokenCursor(tokens, source);
      var children = ParseNodes(cursor, terminator: null);
      return new PassageBody { Children = children };
    }

    /// <summary>
    /// True when the token <paramref name="offset"/> ahead is a <c>[</c> with a
    /// matching <c>]</c>. An unpaired bracket isn't hook markup at all: it's
    /// literal text, so it can't take a macro's attachment or swallow the prose
    /// after it.
    /// </summary>
    private static bool IsHookAt(TokenCursor cursor, int offset)
    {
      var token = cursor.Peek(offset);
      return token.Type == TokenType.HookOpen && token.Partner >= 0;
    }

    /// <summary>True when the cursor is at the closer of the hook being parsed, rather than a literal <c>]</c>.</summary>
    private static bool AtTerminator(TokenCursor cursor, TokenType? terminator)
      => terminator.HasValue && cursor.Current.Type == terminator.Value && cursor.Current.Partner >= 0;

    /// <summary>
    /// Loops over <see cref="ParseNode"/> until either end-of-file or the
    /// optional <paramref name="terminator"/> token type appears at the cursor.
    /// Used both for the top-level body and for recursing into hook contents
    /// (which terminate at <see cref="TokenType.HookClose"/>). The terminator
    /// itself is not consumed; the caller decides what to do with it.
    ///
    /// <para>Recovers from a <see cref="HarloweParseException"/> by appending
    /// a <see cref="ParseErrorNode"/> in place of the failed node and then
    /// trying to resync the cursor to a safe body-mode resume point so
    /// sibling content after the broken construct still parses. The resync
    /// helper stops at the caller's terminator (so the broken hook still
    /// closes cleanly), skips to the next Newline at the same hook depth, or
    /// consumes exactly the macro-close parens the failed node still owes —
    /// counted via <see cref="TokenCursor.NetMacroDepthSince"/> so a
    /// well-formed sibling macro after the broken construct is never folded
    /// into the error span. When no resume point is available we stop
    /// parsing further siblings at this level. Loader recovery still handles
    /// tokenizer-level failures that can't be recovered here.</para>
    /// </summary>
    private List<IBodyNode> ParseNodes(TokenCursor cursor, TokenType? terminator)
    {
      var nodes = new List<IBodyNode>();
      while (!cursor.IsAtEnd && !AtTerminator(cursor, terminator))
      {
        // Capture the failure-span start BEFORE attempting the node so the
        // catch can slice the original source between the failed node's first
        // token and wherever the resync helper lands. The cursor owns the
        // source (passed in at construction) and slices on demand — tokens
        // alone can't reconstruct the run because the tokenizer drops
        // whitespace and punctuation that don't surface in Token.Value.
        int startPos = cursor.Current.Position;
        int startIndex = cursor.Index;
        IBodyNode node;
        try
        {
          node = ParseNode(cursor);
        }
        catch (HarloweParseException ex)
        {
          _pendingUnclosedHook = null;
          bool advanced = TryAdvanceToResumePoint(
            cursor, terminator, cursor.NetMacroDepthSince(startIndex));
          nodes.Add(new ParseErrorNode
          {
            Message = $"parse error{SourcePosition.AtClause(ex.Line, ex.Column)}: {ex.RawMessage ?? ex.Message}",
            Detail = ex.RawMessage ?? ex.Message,
            Line = ex.Line,
            Column = ex.Column,
            OriginalSource = cursor.SliceFrom(startPos),
          });
          if (!advanced) break;
          // The resync may resume at an unconsumed MacroOpen; termination
          // then rests on ParseNode having consumed at least one token
          // before throwing. Force progress if that invariant ever breaks,
          // so a bad throw site degrades to an extra error node rather than
          // an infinite loop.
          if (cursor.Index == startIndex) cursor.Advance();
          continue;
        }
        if (node != null) nodes.Add(node);
        if (_pendingUnclosedHook != null)
        {
          nodes.Add(_pendingUnclosedHook);
          _pendingUnclosedHook = null;
        }
      }
      return FoldFormatting(nodes);
    }

    /// <summary>
    /// Resolve inline-formatting delimiters and <c>{…}</c> collapsing markers in
    /// a freshly-parsed sibling list into <see cref="FormatNode"/>s and
    /// <see cref="CollapsedNode"/>s. The tokenizer has already paired them
    /// across the whole passage, as reference's lexer does, so only real pairs
    /// reach here and they nest. Runs once per nesting level. The literal
    /// fallback covers a pair that error recovery split by skipping one half.
    ///
    /// <para>The common case — a passage with none of these markers — hits the
    /// fast path and returns the original list untouched.</para>
    /// </summary>
    private static List<IBodyNode> FoldFormatting(List<IBodyNode> nodes)
    {
      bool hasMarker = false;
      for (int i = 0; i < nodes.Count; i++)
        if (nodes[i] is FormatDelimiterMarker || nodes[i] is CollapseMarker
            || nodes[i] is UnclosedCollapseMarker || nodes[i] is UnclosedHookMarker)
        { hasMarker = true; break; }
      if (!hasMarker) return nodes;

      // A stack of open frames over a synthetic root. Each frame collects the
      // nodes that become a FormatNode's or CollapsedNode's children if a
      // matching closer arrives, or are flattened (behind a literal delimiter)
      // into the parent if the frame stays unmatched.
      var root = new FormatFrame { Children = new List<IBodyNode>() };
      var stack = new List<FormatFrame> { root };

      for (int i = 0; i < nodes.Count; i++)
      {
        if (nodes[i] is CollapseMarker collapse)
        {
          FoldCollapseMarker(collapse, stack);
          continue;
        }

        if (!(nodes[i] is FormatDelimiterMarker marker))
        {
          stack[stack.Count - 1].Children.Add(nodes[i]);
          continue;
        }

        int match = -1;
        for (int s = stack.Count - 1; s >= 1; s--) // index 0 is the root (no kind)
          if (stack[s].Kind == marker.Kind) { match = s; break; }

        if (match < 0)
        {
          // No matching opener: this delimiter opens a new span.
          stack.Add(new FormatFrame { Kind = marker.Kind, Children = new List<IBodyNode>() });
          continue;
        }

        // Close down to the matched frame. Any inner frames are crossed
        // (unmatched) openers — flatten them to a literal delimiter + content.
        while (stack.Count - 1 > match) FlattenAsLiteral(stack);

        var closed = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        stack[stack.Count - 1].Children.Add(
          new FormatNode { Format = closed.Kind.Value, Children = closed.Children });
      }

      // Whatever is still open never closed — degrade each to literal text.
      while (stack.Count > 1) FlattenAsLiteral(stack);
      return ApplyUnclosedMarkers(root.Children);
    }

    /// <summary>
    /// Handles one <c>{</c> or <c>}</c> during the fold. An opener starts a
    /// frame. A closer folds with the nearest open <c>{</c>, degrading any
    /// markers it crosses to literal text, or is literal itself when no
    /// <c>{</c> is open.
    /// </summary>
    private static void FoldCollapseMarker(CollapseMarker marker, List<FormatFrame> stack)
    {
      if (marker.IsOpener)
      {
        stack.Add(new FormatFrame { IsCollapse = true, Children = new List<IBodyNode>() });
        return;
      }

      int match = -1;
      for (int s = stack.Count - 1; s >= 1; s--)
        if (stack[s].IsCollapse) { match = s; break; }

      if (match < 0)
      {
        stack[stack.Count - 1].Children.Add(new TextNode { Content = "}" });
        return;
      }

      while (stack.Count - 1 > match) FlattenAsLiteral(stack);
      var closed = stack[stack.Count - 1];
      stack.RemoveAt(stack.Count - 1);
      stack[stack.Count - 1].Children.Add(new CollapsedNode { Children = closed.Children });
    }

    /// <summary>
    /// Gives each <c>{=</c> and <c>[=</c> marker the rest of its sibling list —
    /// reference's renderer does the same with <c>tokens.slice(i + 1, len)</c>,
    /// so a marker inside a formatted span runs only to the end of that span.
    /// Descends into the spans this fold built; hooks have already been folded
    /// on their own. Recursion is bounded because the parser counts these
    /// markers toward <see cref="MaxDepth"/>.
    /// </summary>
    private static List<IBodyNode> ApplyUnclosedMarkers(List<IBodyNode> nodes)
    {
      for (int i = 0; i < nodes.Count; i++)
      {
        switch (nodes[i])
        {
          case FormatNode format:
            format.Children = ApplyUnclosedMarkers(format.Children);
            break;
          case CollapsedNode collapsed:
            collapsed.Children = ApplyUnclosedMarkers(collapsed.Children);
            break;
          case UnclosedCollapseMarker marker:
          {
            var result = nodes.GetRange(0, i);
            result.Add(new CollapsedNode
            {
              UnclosedMarker = marker.Source,
              Children = ApplyUnclosedMarkers(nodes.GetRange(i + 1, nodes.Count - i - 1)),
            });
            return result;
          }
          case UnclosedHookMarker hookMarker:
          {
            hookMarker.Hook.Children = ApplyUnclosedMarkers(nodes.GetRange(i + 1, nodes.Count - i - 1));
            var result = nodes.GetRange(0, i);
            if (!hookMarker.IsAttached) result.Add(hookMarker.Hook);
            return result;
          }
        }
      }
      return nodes;
    }

    /// <summary>
    /// Pop the top (unmatched) frame and splice its content into its parent
    /// behind a literal delimiter <see cref="TextNode"/>, so a crossed or
    /// unclosed <c>''</c>, <c>//</c> or <c>{</c> renders as the characters the
    /// author typed.
    /// </summary>
    private static void FlattenAsLiteral(List<FormatFrame> stack)
    {
      var open = stack[stack.Count - 1];
      stack.RemoveAt(stack.Count - 1);
      var parent = stack[stack.Count - 1];
      // Every non-root frame is a collapse or has a Kind, and the root is never flattened.
      string literal = open.IsCollapse ? "{" : InlineFormats.Delimiter(open.Kind.Value);
      parent.Children.Add(new TextNode { Content = literal });
      parent.Children.AddRange(open.Children);
    }

    /// <summary>One open span during the fold: an inline format (<see cref="Kind"/>) or a <c>{…}</c> (<see cref="IsCollapse"/>). The synthetic root is neither.</summary>
    private sealed class FormatFrame
    {
      public InlineFormat? Kind;
      public bool IsCollapse;
      public List<IBodyNode> Children;
    }

    /// <summary>Transient stand-in for a <c>{</c> or <c>}</c> between parsing and <see cref="FoldFormatting"/>. Never survives a <see cref="ParseNodes"/> call.</summary>
    private sealed class CollapseMarker : IBodyNode
    {
      public bool IsOpener;
      public void Accept(IBodyVisitor visitor) =>
        throw new System.InvalidOperationException(
          "collapse marker should have been folded away during parsing");
    }

    /// <summary>Transient stand-in for a <c>{=</c> between parsing and <see cref="ApplyUnclosedMarkers"/>. Never survives a <see cref="ParseNodes"/> call.</summary>
    private sealed class UnclosedCollapseMarker : IBodyNode
    {
      public string Source;
      public void Accept(IBodyVisitor visitor) =>
        throw new System.InvalidOperationException(
          "unclosed collapse marker should have been folded away during parsing");
    }

    /// <summary>
    /// Transient stand-in for a <c>[=</c> between parsing and
    /// <see cref="ApplyUnclosedMarkers"/>. <see cref="IsAttached"/> means the
    /// hook is already held by the node before it (a macro, or a <c>|name&gt;</c>),
    /// so only its content is filled in.
    /// </summary>
    private sealed class UnclosedHookMarker : IBodyNode
    {
      public HookNode Hook;
      public bool IsAttached;
      public void Accept(IBodyVisitor visitor) =>
        throw new System.InvalidOperationException(
          "unclosed hook marker should have been folded away during parsing");
    }

    /// <summary>
    /// Transient body node standing in for an unfolded inline-format delimiter
    /// (<c>''</c>/<c>//</c>) between parsing and <see cref="FoldFormatting"/>.
    /// Never survives a <see cref="ParseNodes"/> call, so it is never rendered;
    /// <see cref="Accept"/> throws to surface the bug loudly if one ever leaks.
    /// </summary>
    private sealed class FormatDelimiterMarker : IBodyNode
    {
      public InlineFormat Kind;
      public void Accept(IBodyVisitor visitor) =>
        throw new System.InvalidOperationException(
          "format delimiter marker should have been folded away during parsing");
    }

    /// <summary>
    /// Advance the cursor to a safe body-mode resume point after a parse
    /// error, or to the caller's terminator. Returns true when the cursor
    /// landed on a resume position (parsing can continue); false when the
    /// caller's terminator was reached at this depth or end-of-input was hit.
    ///
    /// <para><paramref name="owedMacroCloses"/> is the number of MacroClose
    /// tokens the broken construct still owes — the net count of MacroOpen
    /// tokens the failed node consumed before throwing (1 when an expression
    /// error fires mid-args, 2 when it fires inside a nested macro call's
    /// args, 0 when the throw came after the construct's parens balanced,
    /// like the assignment-macro-in-chain check in <see cref="ParseMacro"/>).
    /// The scan consumes exactly that many balancing closes (counting any
    /// further opens it passes on the way) and resumes right after the last
    /// one, so the whole broken construct — outer closers included — lands in
    /// the error span and nothing beyond it does. With nothing owed, the next
    /// MacroOpen at the outer hook level is itself the resume point, returned
    /// to unconsumed: it starts a well-formed sibling that must not be folded
    /// into the error.</para>
    ///
    /// <para>Hook nesting is tracked so a stray HookClose deeper than the
    /// outer ParseNodes call doesn't masquerade as the terminator. A Newline
    /// at the outer hook level is always a resume point, owed closes or not,
    /// so an unterminated macro can't swallow the rest of the passage.</para>
    /// </summary>
    private static bool TryAdvanceToResumePoint(TokenCursor cursor, TokenType? terminator, int owedMacroCloses)
    {
      int hookDepth = 0;
      int owed = owedMacroCloses;
      while (!cursor.IsAtEnd)
      {
        var t = cursor.Current.Type;
        bool atOuter = hookDepth == 0;
        if (atOuter && AtTerminator(cursor, terminator))
          return false;
        if (atOuter && t == TokenType.Newline)
        {
          cursor.Advance();
          return true;
        }
        if (t == TokenType.MacroOpen)
        {
          if (atOuter && owed == 0) return true;
          owed++;
        }
        else if (t == TokenType.MacroClose && owed > 0)
        {
          owed--;
          cursor.Advance();
          if (owed == 0 && hookDepth == 0) return true;
          continue;
        }
        else if (t == TokenType.HookOpen && cursor.Current.Partner >= 0) hookDepth++;
        else if (t == TokenType.HookClose && cursor.Current.Partner >= 0 && hookDepth > 0) hookDepth--;
        cursor.Advance();
      }
      return false;
    }

    /// <summary>
    /// Dispatch one body token to its node constructor. Returns null for
    /// tokens that are silently skipped (stray closers, etc.) so the caller
    /// can drop them without polluting the children list.
    /// </summary>
    private IBodyNode ParseNode(TokenCursor cursor)
    {
      var t = cursor.Current;
      switch (t.Type)
      {
        case TokenType.Text:
          cursor.Advance();
          return new TextNode { Content = t.Value };

        case TokenType.Newline:
          cursor.Advance();
          return new NewlineNode();

        case TokenType.Variable:
          return ParseVariable(cursor, isTemporary: false);

        case TokenType.TempVariable:
          return ParseVariable(cursor, isTemporary: true);

        case TokenType.HtmlTag:
          cursor.Advance();
          return new HtmlNode { RawHtml = t.Value };

        case TokenType.Verbatim:
        {
          cursor.Advance();
          int run = 0;
          while (t.Value[run] == '`') run++;
          return new VerbatimNode
          {
            Delimiter = t.Value.Substring(0, run),
            Text = t.Value.Substring(run, t.Value.Length - 2 * run),
          };
        }

        case TokenType.HorizontalRule:
          cursor.Advance();
          return new HorizontalRuleNode { OriginalSource = t.Value };

        case TokenType.FormatDelimiter:
          // A pair becomes a transient marker that ParseNodes folds into a
          // FormatNode once the whole sibling list at this level is built.
          cursor.Advance();
          if (t.Partner < 0) return new TextNode { Content = t.Value };
          return new FormatDelimiterMarker { Kind = InlineFormats.FromDelimiter(t.Value) };

        case TokenType.CollapseOpen:
          // Folded with the format delimiters once this level is complete.
          // Past the nesting limit a brace is prose, so input like 100,000
          // `{` can't build an AST deep enough to overflow the stack.
          cursor.Advance();
          if (t.Partner < 0 || AtNestingLimit) return new TextNode { Content = "{" };
          _levelBraces++;
          return new CollapseMarker { IsOpener = true };

        case TokenType.CollapseClose:
          cursor.Advance();
          if (t.Partner < 0) return new TextNode { Content = "}" };
          if (_levelBraces > 0) _levelBraces--;
          return new CollapseMarker { IsOpener = false };

        case TokenType.CollapseUnclosed:
          cursor.Advance();
          if (AtNestingLimit) return new TextNode { Content = t.Value };
          _levelUnclosed++;
          return new UnclosedCollapseMarker { Source = t.Value };

        case TokenType.HookOpenUnclosed:
          cursor.Advance();
          if (AtNestingLimit) return new TextNode { Content = t.Value };
          _levelUnclosed++;
          return new UnclosedHookMarker
          {
            Hook = new HookNode { UnclosedMarker = t.Value, Children = new List<IBodyNode>() },
          };

        case TokenType.MacroOpen:
          return ParseMacro(cursor);

        case TokenType.HookOpen:
          if (!IsHookAt(cursor, 0))
          {
            cursor.Advance();
            return new TextNode { Content = "[" };
          }
          cursor.Advance();
          return ParseHookContents(cursor, HookAnchor.None, name: null);

        case TokenType.HookClose:
          // A paired closer is the enclosing hook's terminator and never
          // reaches here, so this `]` has no opener: literal text.
          cursor.Advance();
          return new TextNode { Content = "]" };

        case TokenType.HookNameLeft:
          // Only a name directly after a hook's `]` is markup, and
          // ParseHookContents consumes that one.
          cursor.Advance();
          return new TextNode { Content = "<" + t.Value + "|" };

        case TokenType.HookNameRight:
          return ParseRightAnchoredHook(cursor);

        case TokenType.LinkOpen:
          return ParseLink(cursor);

        case TokenType.Comment:
          return ParseComment(cursor);

        case TokenType.HtmlComment:
          // Already one self-contained token; renders as nothing (reference's
          // `htmlComment` renderer case is an empty break).
          cursor.Advance();
          return new CommentNode { OriginalSource = t.Value };

        default:
          // Tokens that can't begin a body node are skipped: the parser is
          // tolerant of malformed Harlowe rather than crashing.
          cursor.Advance();
          return null;
      }
    }

    /// <summary>
    /// Cursor is at a <see cref="TokenType.Comment"/> (<c>--</c>) marker.
    /// Consumes it plus the one construct it comments out, per reference's
    /// renderer (<c>ts/renderer.ts</c>, case <c>comment</c>: "The comment
    /// syntax eliminates the next fully-wrapped token"). A "fully-wrapped
    /// token" in reference is one folded lex tree — mapped here onto our
    /// flat token stream:
    ///
    /// <list type="bullet">
    /// <item>a prose run — all consecutive <see cref="TokenType.Text"/>
    /// tokens, since reference lexes an unbroken prose stretch as a single
    /// text token where our tokenizer may split it;</item>
    /// <item>a comment hook <c>--[…]</c> — the whole hook, nesting included
    /// (hooks nest naturally, which is what makes reference's comment hooks
    /// nestable);</item>
    /// <item>one whole macro call — skipped <em>structurally</em> (balanced
    /// <see cref="TokenType.MacroOpen"/>/<see cref="TokenType.MacroClose"/>
    /// counting, no argument parse), so a syntax error inside a commented-out
    /// macro stays silent exactly as in reference, where the macro's folded
    /// token is eliminated before anything evaluates it. The attached hook,
    /// if any, is <em>not</em> part of the macro's token in reference and
    /// survives as an ordinary anonymous hook — same here, since we never
    /// reach the attachment lookahead;</item>
    /// <item>an inline-format span — <c>--''bold''</c> eats through the
    /// matching delimiter (reference folds the whole styled span into one
    /// token) via lookahead at the same hook depth; an unmatched delimiter is
    /// eaten alone (reference: it degrades to a literal-text token, which is
    /// then the eliminated token);</item>
    /// <item>a link, a named hook, a variable, an HTML tag, a line break, or
    /// any other single construct.</item>
    /// </list>
    ///
    /// A comment with nothing after it (end of input, or the enclosing
    /// hook's <c>]</c>) eliminates nothing. The consumed span, marker
    /// included, lands in <see cref="CommentNode.OriginalSource"/> so a dirty
    /// passage round-trips its comments through <see cref="Twee.MarkupPrinter"/>.
    /// </summary>
    private IBodyNode ParseComment(TokenCursor cursor)
    {
      int startPos = cursor.Current.Position;
      cursor.Advance(); // the -- marker

      var next = cursor.Current;
      switch (next.Type)
      {
        case TokenType.EndOfFile:
          break; // nothing to eliminate

        case TokenType.HookClose:
          // A real closer is the caller's terminator. An unpaired one is text.
          if (next.Partner < 0) cursor.Advance();
          break;

        case TokenType.Text:
          while (cursor.Current.Type == TokenType.Text) cursor.Advance();
          break;

        case TokenType.HookOpen:
          // An unmatched `[` is literal text, so it's the one token eliminated.
          bool isHook = IsHookAt(cursor, 0);
          cursor.Advance();
          if (isHook) ParseHookContents(cursor, HookAnchor.None, name: null); // discarded
          break;

        case TokenType.MacroOpen:
          SkipBalancedMacro(cursor);
          break;

        case TokenType.LinkOpen:
          // Discarded. Always terminated — the tokenizer only emits LinkOpen
          // when a ]] closer is ahead.
          ParseLink(cursor);
          break;

        case TokenType.HookNameRight:
          ParseRightAnchoredHook(cursor); // discarded — |name>[…] is one construct
          break;

        case TokenType.FormatDelimiter:
        case TokenType.CollapseOpen:
          SkipPairedSpan(cursor);
          break;

        default:
          // Newline, variable, HTML tag, a following comment marker, a stray
          // closer — all single tokens. Reference eats a br here too, so
          // `--` at line end joins the lines.
          cursor.Advance();
          break;
      }

      // A commented-out `|name>[=` is eliminated alone: it must not go on to
      // claim the prose after it.
      _pendingUnclosedHook = null;
      return new CommentNode { OriginalSource = cursor.SliceFrom(startPos) };
    }

    /// <summary>
    /// Cursor is at <see cref="TokenType.MacroOpen"/>: advance past the whole
    /// call by balanced open/close counting, with no argument parsing. Used
    /// only by <see cref="ParseComment"/> — a commented-out macro is
    /// eliminated before anything inspects it, so its contents can't raise
    /// parse errors. Grouping parens and brackets inside the args are strictly
    /// nested between the macro's own open/close pair and need no counting of
    /// their own. Stops at end-of-input for an unterminated call.
    /// </summary>
    private static void SkipBalancedMacro(TokenCursor cursor)
    {
      int depth = 0;
      do
      {
        var t = cursor.Current.Type;
        if (t == TokenType.EndOfFile) return;
        if (t == TokenType.MacroOpen) depth++;
        else if (t == TokenType.MacroClose) depth--;
        cursor.Advance();
      } while (depth > 0);
    }

    /// <summary>
    /// Cursor is at an inline-format delimiter or <c>{</c> right after a comment
    /// marker. A paired span is one folded token in reference, so the comment
    /// eliminates all of it through its closer. An unpaired one is literal text
    /// and is eaten alone.
    /// </summary>
    private static void SkipPairedSpan(TokenCursor cursor)
    {
      int closer = cursor.Current.Partner;
      if (closer <= cursor.Index)
      {
        cursor.Advance();
        return;
      }
      while (cursor.Index <= closer) cursor.Advance();
    }

    /// <summary>
    /// Cursor is at <see cref="TokenType.MacroOpen"/>. Parses a body macro,
    /// then handles two follow-up shapes:
    ///
    /// <list type="number">
    /// <item><b>Changer chain</b>: <c>(m1)+(m2)+...[hook]</c>. After the macro,
    /// the parser peeks for a <c>+</c> followed by another macro and folds the
    /// run into a <see cref="ChangerChainNode"/> whose
    /// <see cref="ChangerChainNode.Expression"/> is a <see cref="BinaryOpNode"/>
    /// chain over <see cref="MacroCallNode"/>s.</item>
    /// <item><b>Plain body macro</b>: a <see cref="MacroNode"/> with an
    /// optional <see cref="MacroNode.AttachedHook"/>, the v1 path.</item>
    /// </list>
    ///
    /// The two paths are mutually exclusive: a <c>+</c> after the first macro
    /// commits to the chain shape; otherwise the parser falls through to the
    /// macro-with-hook lookahead.
    /// </summary>
    private IBodyNode ParseMacro(TokenCursor cursor)
    {
      var nameToken = cursor.Current;
      string name = nameToken.Value;
      cursor.Advance();
      var args = _expressionParser.ParseArgumentList(cursor, HarloweExpressionParser.IsAssignmentMacro(name));

      // Try changer chain first — committing as soon as we see `+ (macro)`.
      if (LooksLikeChainContinuation(cursor))
      {
        // Reject (set:)/(put:) as a chain component: the chain is evaluated
        // as a value expression, which would silently execute the assignment
        // *before* producing the (inevitable) chain-type error. The first
        // arg list has already been parsed with assignment enabled — we
        // catch the misuse before the AST escapes so no mutation occurs at
        // render time.
        if (HarloweExpressionParser.IsAssignmentMacro(name))
          throw new HarloweParseException(
            $"({name}:) cannot appear as a changer-chain component — it's an assignment macro, not a value-position macro",
            nameToken.Line, nameToken.Column);

        IExpressionNode expr = new MacroCallNode { Name = name, Arguments = args, Line = nameToken.Line, Column = nameToken.Column };
        while (TryAdvanceToChainContinuation(cursor))
        {
          // Cursor is now at the next MacroOpen. Parse one macro call.
          // Changer-chain components are value-position macros: a stray
          // assignment here would never reach (set:)/(put:) semantics, so
          // forbid `to`/`into` in their arg lists.
          var nextNameToken = cursor.Current;
          string nextName = nextNameToken.Value;
          cursor.Advance();
          if (HarloweExpressionParser.IsAssignmentMacro(nextName))
            throw new HarloweParseException(
              $"({nextName}:) cannot appear as a changer-chain component — it's an assignment macro, not a value-position macro",
              nextNameToken.Line, nextNameToken.Column);
          var nextArgs = _expressionParser.ParseArgumentList(cursor, allowAssignment: false);
          expr = new BinaryOpNode
          {
            Operator = "+",
            Left = expr,
            Right = new MacroCallNode { Name = nextName, Arguments = nextArgs, Line = nextNameToken.Line, Column = nextNameToken.Column },
          };
        }

        var chain = new ChangerChainNode { Expression = expr };
        AttachOptionalHook(cursor, h => chain.AttachedHook = h);
        return chain;
      }

      // Plain body macro path
      var macro = new MacroNode
      {
        Name = name,
        Arguments = args,
        Line = nameToken.Line,
        Column = nameToken.Column
      };
      AttachOptionalHook(cursor, h => macro.AttachedHook = h);
      return macro;
    }

    /// <summary>
    /// Cursor is at <see cref="TokenType.Variable"/> or
    /// <see cref="TokenType.TempVariable"/>. Consumes the variable, then
    /// peeks past optional whitespace for an attached hook. If a hook
    /// follows, returns a <see cref="ChangerChainNode"/> wrapping a
    /// <see cref="VariableRefNode"/> (the stored-changer-then-hook pattern);
    /// otherwise returns the existing <see cref="VariableNode"/> shape so
    /// plain interpolation (<c>Hello $name.</c>) is unchanged.
    /// </summary>
    private IBodyNode ParseVariable(TokenCursor cursor, bool isTemporary)
    {
      var t = cursor.Current;
      cursor.Advance();

      int offset = SkipBodyWhitespace(cursor);
      if (IsHookAt(cursor, offset))
      {
        for (int i = 0; i <= offset; i++) cursor.Advance();
        var hook = ParseHookContents(cursor, HookAnchor.None, name: null);
        return new ChangerChainNode
        {
          Expression = new VariableRefNode { Name = t.Value, IsTemporary = isTemporary },
          AttachedHook = hook,
        };
      }
      if (TryAttachUnclosedHook(cursor, offset, out var unclosed))
      {
        return new ChangerChainNode
        {
          Expression = new VariableRefNode { Name = t.Value, IsTemporary = isTemporary },
          AttachedHook = unclosed,
        };
      }

      return new VariableNode { Name = t.Value, IsTemporary = isTemporary };
    }

    /// <summary>
    /// If a <c>[=</c> follows at <paramref name="offset"/>, consumes through it
    /// and returns the hook for the caller to attach, queuing its marker so the
    /// fold fills it with the rest of this level.
    /// </summary>
    private bool TryAttachUnclosedHook(TokenCursor cursor, int offset, out HookNode hook)
    {
      hook = null;
      var token = cursor.Peek(offset);
      if (token.Type != TokenType.HookOpenUnclosed || AtNestingLimit) return false;
      for (int i = 0; i <= offset; i++) cursor.Advance();
      hook = new HookNode { UnclosedMarker = token.Value, Children = new List<IBodyNode>() };
      _levelUnclosed++;
      _pendingUnclosedHook = new UnclosedHookMarker { Hook = hook, IsAttached = true };
      return true;
    }

    /// <summary>
    /// Skips whitespace from the current cursor position; if a
    /// <see cref="TokenType.HookOpen"/> follows, consumes through the open and
    /// passes the parsed hook to <paramref name="setHook"/>. No-ops when no
    /// hook is present.
    /// </summary>
    private void AttachOptionalHook(TokenCursor cursor, System.Action<HookNode> setHook)
    {
      int offset = SkipBodyWhitespace(cursor);
      if (TryAttachUnclosedHook(cursor, offset, out var unclosed))
      {
        setHook(unclosed);
        return;
      }
      if (!IsHookAt(cursor, offset)) return;
      for (int i = 0; i <= offset; i++) cursor.Advance();
      setHook(ParseHookContents(cursor, HookAnchor.None, name: null));
    }

    /// <summary>
    /// Pure-lookahead: returns true iff the cursor position is followed (after
    /// optional whitespace) by a <c>+</c>-marker Text token whose trimmed
    /// content is exactly <c>+</c>, then more whitespace, then a
    /// <see cref="TokenType.MacroOpen"/>. Does not advance the cursor.
    ///
    /// <para>The <c>+</c> marker is a <see cref="TokenType.Text"/> token
    /// because <c>+</c> is not body-special at the tokenizer level — it falls
    /// into <c>ScanText</c> just like any other character. We peek into the
    /// Text content to recognise the marker.</para>
    ///
    /// <para>Parameterless <c>Trim()</c> is deliberate. Reference Harlowe's
    /// <c>ts/markup/patterns.ts</c> defines its syntactic-whitespace class
    /// (the <c>ws</c> pattern at line 63) as the union of ASCII space,
    /// <c>\f</c>, <c>\t</c>, <c>\v</c>, U+00A0 (NBSP), U+2000..U+200A (em /
    /// en / quad / thin / hair spaces), U+2028..U+2029 (line / paragraph
    /// separators), U+202F (narrow NBSP), U+205F (medium math space), and
    /// U+3000 (ideographic space), explicitly excluding zero-width
    /// characters. <c>char.IsWhiteSpace</c> matches almost exactly the same
    /// set (it returns false for ZWSP/ZWNJ/ZWJ/BOM, matching reference). An
    /// ASCII-only Trim would diverge from reference and reject NBSP-padded
    /// chain markers that reference accepts.</para>
    /// </summary>
    private static bool LooksLikeChainContinuation(TokenCursor cursor)
    {
      int offset = SkipBodyWhitespace(cursor);
      var marker = cursor.Peek(offset);
      if (marker.Type != TokenType.Text || marker.Value.Trim() != "+") return false;

      int after = offset + 1;
      while (true)
      {
        var t = cursor.Peek(after);
        if (t.Type == TokenType.Newline) { after++; continue; }
        if (t.Type == TokenType.Text && string.IsNullOrWhiteSpace(t.Value)) { after++; continue; }
        break;
      }
      return cursor.Peek(after).Type == TokenType.MacroOpen;
    }

    /// <summary>
    /// If the cursor is at a chain continuation (per
    /// <see cref="LooksLikeChainContinuation"/>), advances past the
    /// whitespace/<c>+</c>/whitespace tokens to position the cursor at the
    /// next <see cref="TokenType.MacroOpen"/>. Returns true on success, false
    /// (cursor untouched) when the pattern does not match.
    /// </summary>
    private static bool TryAdvanceToChainContinuation(TokenCursor cursor)
    {
      if (!LooksLikeChainContinuation(cursor)) return false;
      int offset = SkipBodyWhitespace(cursor);
      int after = offset + 1;
      while (true)
      {
        var t = cursor.Peek(after);
        if (t.Type == TokenType.Newline) { after++; continue; }
        if (t.Type == TokenType.Text && string.IsNullOrWhiteSpace(t.Value)) { after++; continue; }
        break;
      }
      for (int i = 0; i < after; i++) cursor.Advance();
      return true;
    }

    /// <summary>
    /// Peeks ahead past any pure-whitespace <see cref="TokenType.Text"/> tokens
    /// and <see cref="TokenType.Newline"/> tokens, returning the count. The
    /// cursor is not moved. Used to allow whitespace between a macro and its
    /// attached hook (<c>(if: $x) [hi]</c> attaches even with the space).
    /// </summary>
    private static int SkipBodyWhitespace(TokenCursor cursor)
    {
      int offset = 0;
      while (true)
      {
        var t = cursor.Peek(offset);
        if (t.Type == TokenType.Newline) { offset++; continue; }
        if (t.Type == TokenType.Text && string.IsNullOrWhiteSpace(t.Value)) { offset++; continue; }
        break;
      }
      return offset;
    }

    /// <summary>
    /// Cursor is just past the opening <see cref="TokenType.HookOpen"/>.
    /// Recursively collects body nodes until <see cref="TokenType.HookClose"/>,
    /// consumes that close, and then checks for a trailing
    /// <see cref="TokenType.HookNameLeft"/> (<c>[content]&lt;name|</c>) to set
    /// the hook's left anchor. Anonymous hooks pass <paramref name="name"/> =
    /// null and <paramref name="anchor"/> = <see cref="HookAnchor.None"/>;
    /// right-anchored hooks pass the name in advance and have their anchor
    /// preset.
    /// </summary>
    private HookNode ParseHookContents(TokenCursor cursor, HookAnchor anchor, string name)
    {
      // Depth ceiling for nested hooks (`[[[…]]]`). Throwing past the cap keeps
      // thousands of nested '[' from blowing the C# stack with an uncatchable
      // StackOverflowException; the per-node recovery / loader catch turns it
      // into an in-prose error. Restored via finally so it stays balanced.
      if (AtNestingLimit)
      {
        var deep = cursor.Current;
        throw new HarloweParseException("hooks nested too deeply", deep.Line, deep.Column);
      }

      // Spans can't cross a hook boundary, so this level's become outer ones
      // while the hook parses, and come back when it closes.
      int outerSpans = _outerSpans, levelBraces = _levelBraces, levelUnclosed = _levelUnclosed;
      _outerSpans += _levelBraces + _levelUnclosed;
      _levelBraces = _levelUnclosed = 0;
      _depth++;
      try
      {
        var children = ParseNodes(cursor, terminator: TokenType.HookClose);
        if (cursor.Current.Type == TokenType.HookClose) cursor.Advance();

        if (anchor == HookAnchor.None && cursor.Current.Type == TokenType.HookNameLeft)
        {
          name = cursor.Current.Value;
          anchor = HookAnchor.Left;
          cursor.Advance();
        }

        return new HookNode { Name = name, Anchor = anchor, Children = children };
      }
      finally
      {
        _depth--;
        _outerSpans = outerSpans;
        _levelBraces = levelBraces;
        _levelUnclosed = levelUnclosed;
      }
    }

    /// <summary>
    /// Cursor is at <see cref="TokenType.HookNameRight"/> (<c>|name&gt;</c>).
    /// If a hook follows immediately, builds a right-anchored hook with the
    /// given name. Otherwise the characters are prose: reference's pattern is
    /// <c>|name&gt;[</c>, so a name with no hook after it never becomes markup.
    /// </summary>
    private IBodyNode ParseRightAnchoredHook(TokenCursor cursor)
    {
      string name = cursor.Current.Value;
      cursor.Advance();

      if (IsHookAt(cursor, 0))
      {
        cursor.Advance();
        return ParseHookContents(cursor, HookAnchor.Right, name);
      }

      if (TryAttachUnclosedHook(cursor, 0, out var unclosed))
      {
        unclosed.Name = name;
        unclosed.Anchor = HookAnchor.Right;
        return unclosed;
      }

      return new TextNode { Content = "|" + name + ">" };
    }

    /// <summary>
    /// Cursor is at <see cref="TokenType.LinkOpen"/>. Consumes through the
    /// matching <see cref="TokenType.LinkClose"/>, supporting all three Twine
    /// link forms:
    /// <list type="bullet">
    /// <item><c>[[target]]</c> — both <see cref="LinkNode.Text"/> and <see cref="LinkNode.Target"/> set to the inner text.</item>
    /// <item><c>[[display-&gt;target]]</c> / <c>[[display|target]]</c> — left side is display, right side is target.</item>
    /// <item><c>[[target&lt;-display]]</c> — left side is target, right side is display.</item>
    /// </list>
    /// With multiple separators, reference's rule applies ("the rightmost right
    /// arrow or leftmost left arrow is regarded as the canonical separator",
    /// <c>ts/markup/patterns.ts</c>, right separators tried first): the rightmost
    /// <c>-&gt;</c>/<c>|</c> splits, else the leftmost <c>&lt;-</c>, and every
    /// unchosen separator stays literal text — <c>[[A-&gt;B-&gt;C]]</c> is the
    /// display <c>A-&gt;B</c> linking to <c>C</c>.
    /// </summary>
    private LinkNode ParseLink(TokenCursor cursor)
    {
      // The `[[` token, before it's consumed — the link's source position.
      var open = cursor.Current;
      cursor.Advance();

      // Collect the content verbatim: text runs and separators, in order.
      var pieces = new List<string>();
      int lastRightSep = -1;   // index in pieces of the rightmost -> or |
      int firstLeftSep = -1;   // index in pieces of the leftmost <-

      while (!cursor.IsAtEnd && cursor.Current.Type != TokenType.LinkClose)
      {
        var t = cursor.Current;
        switch (t.Type)
        {
          case TokenType.Text:
            pieces.Add(t.Value);
            break;
          case TokenType.LinkArrowRight:
            lastRightSep = pieces.Count;
            pieces.Add(t.Value);
            break;
          case TokenType.LinkArrowLeft:
            if (firstLeftSep < 0) firstLeftSep = pieces.Count;
            pieces.Add(t.Value);
            break;
        }
        cursor.Advance();
      }
      if (cursor.Current.Type == TokenType.LinkClose) cursor.Advance();

      LinkNode node;
      if (lastRightSep >= 0)
        node = new LinkNode { Text = JoinPieces(pieces, 0, lastRightSep), Target = JoinPieces(pieces, lastRightSep + 1, pieces.Count) };
      else if (firstLeftSep >= 0)
        node = new LinkNode { Text = JoinPieces(pieces, firstLeftSep + 1, pieces.Count), Target = JoinPieces(pieces, 0, firstLeftSep) };
      else
      {
        string whole = JoinPieces(pieces, 0, pieces.Count);
        node = new LinkNode { Text = whole, Target = whole };
      }

      node.Line = open.Line;
      node.Column = open.Column;
      return node;
    }

    /// <summary>Concatenates <paramref name="pieces"/>[<paramref name="from"/>..<paramref name="to"/>) verbatim.</summary>
    private static string JoinPieces(List<string> pieces, int from, int to)
    {
      var sb = new System.Text.StringBuilder();
      for (int i = from; i < to; i++) sb.Append(pieces[i]);
      return sb.ToString();
    }
  }
}
