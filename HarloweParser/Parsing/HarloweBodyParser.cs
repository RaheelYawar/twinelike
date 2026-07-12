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
      var cursor = new TokenCursor(tokens, source);
      var children = ParseNodes(cursor, terminator: null);
      return new PassageBody { Children = children };
    }

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
      while (!cursor.IsAtEnd && (!terminator.HasValue || cursor.Current.Type != terminator.Value))
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
          string where = ex.Line > 0 ? $" at line {ex.Line}, column {ex.Column}" : string.Empty;
          bool advanced = TryAdvanceToResumePoint(
            cursor, terminator, cursor.NetMacroDepthSince(startIndex));
          nodes.Add(new ParseErrorNode
          {
            Message = $"parse error{where}: {ex.RawMessage ?? ex.Message}",
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
      }
      return FoldFormatting(nodes);
    }

    /// <summary>
    /// Resolve inline-formatting delimiter markers in a freshly-parsed sibling
    /// list into <see cref="FormatNode"/>s. Mirrors reference Harlowe's
    /// <c>foldChildren</c> (<c>ts/markup/lexer.ts</c>): a delimiter folds with
    /// the nearest open delimiter of the same kind; delimiters that cross
    /// (<c>''//x''//</c>) or never close degrade to literal text. Runs once per
    /// nesting level — each hook's contents are folded independently, so
    /// formatting never crosses a hook boundary (also matching reference, where
    /// each token's <c>innerText</c> is lexed on its own).
    ///
    /// <para>The common case — a passage with no <c>''</c>/<c>//</c> at all —
    /// hits the fast path and returns the original list untouched.</para>
    /// </summary>
    private static List<IBodyNode> FoldFormatting(List<IBodyNode> nodes)
    {
      bool hasMarker = false;
      for (int i = 0; i < nodes.Count; i++)
        if (nodes[i] is FormatDelimiterMarker) { hasMarker = true; break; }
      if (!hasMarker) return nodes;

      // A stack of open format frames over a synthetic root. Each frame collects
      // the nodes that become a FormatNode's children if a matching closer
      // arrives, or are flattened (behind a literal delimiter) into the parent
      // if the frame stays unmatched.
      var root = new FormatFrame { Children = new List<IBodyNode>() };
      var stack = new List<FormatFrame> { root };

      for (int i = 0; i < nodes.Count; i++)
      {
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
      return root.Children;
    }

    /// <summary>
    /// Pop the top (unmatched) format frame and splice its content into its
    /// parent behind a literal delimiter <see cref="TextNode"/>, so a crossed
    /// or unclosed <c>''</c>/<c>//</c> renders as the characters the author
    /// typed.
    /// </summary>
    private static void FlattenAsLiteral(List<FormatFrame> stack)
    {
      var open = stack[stack.Count - 1];
      stack.RemoveAt(stack.Count - 1);
      var parent = stack[stack.Count - 1];
      // Kind is non-null for every non-root frame, and the root is never flattened.
      parent.Children.Add(new TextNode { Content = InlineFormats.Delimiter(open.Kind.Value) });
      parent.Children.AddRange(open.Children);
    }

    /// <summary>One open inline-format span during the fold. <see cref="Kind"/> is null for the synthetic root.</summary>
    private sealed class FormatFrame
    {
      public InlineFormat? Kind;
      public List<IBodyNode> Children;
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
        if (atOuter && terminator.HasValue && t == terminator.Value)
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
        else if (t == TokenType.HookOpen) hookDepth++;
        else if (t == TokenType.HookClose && hookDepth > 0) hookDepth--;
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

        case TokenType.FormatDelimiter:
          // Emit a transient marker; ParseNodes folds matching pairs into
          // FormatNodes (and degrades unmatched ones to literal text) once the
          // whole sibling list at this level is built.
          cursor.Advance();
          return new FormatDelimiterMarker { Kind = InlineFormats.FromDelimiter(t.Value) };

        case TokenType.MacroOpen:
          return ParseMacro(cursor);

        case TokenType.HookOpen:
          cursor.Advance();
          return ParseHookContents(cursor, HookAnchor.None, name: null);

        case TokenType.HookNameRight:
          return ParseRightAnchoredHook(cursor);

        case TokenType.LinkOpen:
          return ParseLink(cursor);

        default:
          // Stray closers (HookClose without an opener, HookNameLeft outside a
          // hook, etc.) are silently skipped — the parser is tolerant of
          // malformed Harlowe rather than crashing.
          cursor.Advance();
          return null;
      }
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

        IExpressionNode expr = new MacroCallNode { Name = name, Arguments = args };
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
            Right = new MacroCallNode { Name = nextName, Arguments = nextArgs },
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
      if (cursor.Peek(offset).Type == TokenType.HookOpen)
      {
        for (int i = 0; i <= offset; i++) cursor.Advance();
        var hook = ParseHookContents(cursor, HookAnchor.None, name: null);
        return new ChangerChainNode
        {
          Expression = new VariableRefNode { Name = t.Value, IsTemporary = isTemporary },
          AttachedHook = hook,
        };
      }

      return new VariableNode { Name = t.Value, IsTemporary = isTemporary };
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
      if (cursor.Peek(offset).Type != TokenType.HookOpen) return;
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
      if (++_depth > MaxDepth)
      {
        _depth--;
        var deep = cursor.Current;
        throw new HarloweParseException("hooks nested too deeply", deep.Line, deep.Column);
      }
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
      finally { _depth--; }
    }

    /// <summary>
    /// Cursor is at <see cref="TokenType.HookNameRight"/> (<c>|name&gt;</c>).
    /// If the very next token is <see cref="TokenType.HookOpen"/>, builds a
    /// right-anchored hook with the given name. If no hook follows, returns
    /// an empty named hook so the name isn't lost — degraded but
    /// non-crashing.
    /// </summary>
    private HookNode ParseRightAnchoredHook(TokenCursor cursor)
    {
      string name = cursor.Current.Value;
      cursor.Advance();

      if (cursor.Current.Type == TokenType.HookOpen)
      {
        cursor.Advance();
        return ParseHookContents(cursor, HookAnchor.Right, name);
      }

      return new HookNode { Name = name, Anchor = HookAnchor.Right, Children = new List<IBodyNode>() };
    }

    /// <summary>
    /// Cursor is at <see cref="TokenType.LinkOpen"/>. Consumes through the
    /// matching <see cref="TokenType.LinkClose"/>, supporting all three Twine
    /// link forms:
    /// <list type="bullet">
    /// <item><c>[[target]]</c> — both <see cref="LinkNode.Text"/> and <see cref="LinkNode.Target"/> set to the inner text.</item>
    /// <item><c>[[display-&gt;target]]</c> — left side is display, right side is target.</item>
    /// <item><c>[[target&lt;-display]]</c> — left side is target, right side is display.</item>
    /// </list>
    /// </summary>
    private LinkNode ParseLink(TokenCursor cursor)
    {
      // The `[[` token, before it's consumed — the link's source position.
      var open = cursor.Current;
      cursor.Advance();

      var left = new System.Text.StringBuilder();
      var right = new System.Text.StringBuilder();
      bool sawArrow = false;
      bool arrowIsRight = false;

      while (!cursor.IsAtEnd && cursor.Current.Type != TokenType.LinkClose)
      {
        var t = cursor.Current;
        switch (t.Type)
        {
          case TokenType.Text:
            (sawArrow ? right : left).Append(t.Value);
            cursor.Advance();
            break;
          case TokenType.LinkArrowRight:
            sawArrow = true;
            arrowIsRight = true;
            cursor.Advance();
            break;
          case TokenType.LinkArrowLeft:
            sawArrow = true;
            arrowIsRight = false;
            cursor.Advance();
            break;
          default:
            cursor.Advance();
            break;
        }
      }
      if (cursor.Current.Type == TokenType.LinkClose) cursor.Advance();

      string leftStr = left.ToString();
      string rightStr = right.ToString();

      LinkNode node;
      if (!sawArrow) node = new LinkNode { Text = leftStr, Target = leftStr };
      else if (arrowIsRight) node = new LinkNode { Text = leftStr, Target = rightStr };
      else node = new LinkNode { Text = rightStr, Target = leftStr };

      node.Line = open.Line;
      node.Column = open.Column;
      return node;
    }
  }
}
