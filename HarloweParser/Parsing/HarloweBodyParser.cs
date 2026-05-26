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
    /// closes cleanly) or skips to the next Newline / closing macro paren at
    /// the same nesting level. When no resume point is available we stop
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
        IBodyNode node;
        try
        {
          node = ParseNode(cursor);
        }
        catch (HarloweParseException ex)
        {
          string where = ex.Line > 0 ? $" at line {ex.Line}, column {ex.Column}" : string.Empty;
          bool advanced = TryAdvanceToResumePoint(cursor, terminator);
          nodes.Add(new ParseErrorNode
          {
            Message = $"parse error{where}: {ex.RawMessage ?? ex.Message}",
            OriginalSource = cursor.SliceFrom(startPos),
          });
          if (!advanced) break;
          continue;
        }
        if (node != null) nodes.Add(node);
      }
      return nodes;
    }

    /// <summary>
    /// Advance the cursor to a safe body-mode resume point after a parse
    /// error, or to the caller's terminator. Returns true when the cursor
    /// landed on a resume position (a Newline or matching MacroClose was
    /// consumed and parsing can continue); false when the caller's
    /// terminator was reached at this depth or end-of-input was hit.
    ///
    /// <para>Hook nesting is tracked so a stray HookClose deeper than the
    /// outer ParseNodes call doesn't masquerade as the terminator. Macro
    /// parens are tracked the other direction: a MacroClose token after the
    /// failure point typically closes the broken macro itself (the parser
    /// had already advanced past the matching MacroOpen before the throw),
    /// so consuming it lands us back in body mode.</para>
    ///
    /// <para>Known edge case: nested malformed macros. For source like
    /// <c>(outer: (inner: bad))</c> where the throw fires inside <c>inner</c>'s
    /// args, the cursor is mid-args of <c>inner</c> when the catch runs. The
    /// first <c>MacroClose</c> this helper finds is <c>inner</c>'s closer;
    /// consuming it lands the cursor on <c>outer</c>'s <c>MacroClose</c>,
    /// which the body parser's <see cref="ParseNode"/> default branch then
    /// silently skips as a stray closer. Net result: the AST has a single
    /// <see cref="ParseErrorNode"/> for the inner failure, and <c>outer</c>'s
    /// wrapper is lost (no <see cref="MacroNode"/> for it). The captured
    /// <see cref="ParseErrorNode.OriginalSource"/> covers <c>(outer: (inner: bad)</c>
    /// — endPos lands on <c>outer</c>'s <c>MacroClose</c> position, before
    /// that closer is consumed — so the slice has the inner failure and
    /// outer's opener but not outer's closer. Round-tripping a mutated AST
    /// produces source with one fewer paren than the original.
    ///
    /// Fixing properly would require threading macro-call depth through the
    /// body parser (increment on <see cref="ParseMacro"/> entry, decrement
    /// on exit) so the catch site knows how many <c>MacroClose</c> tokens
    /// to consume. Out of scope for current recovery work — the edge case
    /// requires nested malformed macros + AST mutation + re-save to manifest,
    /// which is rare enough to defer until a real bug report shows it.</para>
    /// </summary>
    private static bool TryAdvanceToResumePoint(TokenCursor cursor, TokenType? terminator)
    {
      int hookDepth = 0;
      while (!cursor.IsAtEnd)
      {
        var t = cursor.Current.Type;
        bool atOuter = hookDepth == 0;
        if (atOuter && terminator.HasValue && t == terminator.Value)
          return false;
        if (atOuter && (t == TokenType.Newline || t == TokenType.MacroClose))
        {
          cursor.Advance();
          return true;
        }
        if (t == TokenType.HookOpen) hookDepth++;
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
      var macro = new MacroNode { Name = name, Arguments = args };
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
      if (!sawArrow) return new LinkNode { Text = leftStr, Target = leftStr };
      string rightStr = right.ToString();
      if (arrowIsRight) return new LinkNode { Text = leftStr, Target = rightStr };
      return new LinkNode { Text = rightStr, Target = leftStr };
    }
  }
}
