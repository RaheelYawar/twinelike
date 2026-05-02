using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Tokens;

namespace Harlowe.Parsing
{
  /// <summary>
  /// Recursive-descent body parser. Walks a token stream once, dispatching
  /// each token to the body-AST node it produces. Macro arguments are handed
  /// off to an <see cref="IExpressionParser"/> so they come back as proper
  /// expression trees. Hooks contain body content, so the parser recurses
  /// into them via <see cref="ParseHookContents"/>.
  ///
  /// <para>
  /// This parser is built standalone — it does not yet replace the legacy
  /// <c>Harlowe.ParseBody</c>/<c>Harlowe.ParseBranches</c>. Wiring it into the
  /// public API (and deriving <c>HarlowePassage.Branches</c> from
  /// <see cref="LinkNode"/>s) is step 4 of the roadmap.
  /// </para>
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
    /// by parsing nodes until end-of-file.
    /// </summary>
    public PassageBody Parse(IReadOnlyList<Token> tokens)
    {
      var cursor = new TokenCursor(tokens);
      var children = ParseNodes(cursor, terminator: null);
      return new PassageBody { Children = children };
    }

    /// <summary>
    /// Loops over <see cref="ParseNode"/> until either end-of-file or the
    /// optional <paramref name="terminator"/> token type appears at the cursor.
    /// Used both for the top-level body and for recursing into hook contents
    /// (which terminate at <see cref="TokenType.HookClose"/>). The terminator
    /// itself is not consumed; the caller decides what to do with it.
    /// </summary>
    private List<IBodyNode> ParseNodes(TokenCursor cursor, TokenType? terminator)
    {
      var nodes = new List<IBodyNode>();
      while (!cursor.IsAtEnd && (!terminator.HasValue || cursor.Current.Type != terminator.Value))
      {
        var node = ParseNode(cursor);
        if (node != null) nodes.Add(node);
      }
      return nodes;
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
          cursor.Advance();
          return new VariableNode { Name = t.Value, IsTemporary = false };

        case TokenType.TempVariable:
          cursor.Advance();
          return new VariableNode { Name = t.Value, IsTemporary = true };

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
    /// Cursor is at <see cref="TokenType.MacroOpen"/>. Builds a <see cref="MacroNode"/>
    /// whose <see cref="MacroNode.Arguments"/> is populated by the expression
    /// parser (which consumes through the matching <see cref="TokenType.MacroClose"/>).
    /// Then peeks past any whitespace-only text and newlines: if a
    /// <see cref="TokenType.HookOpen"/> follows, it is parsed and attached as
    /// <see cref="MacroNode.AttachedHook"/>; otherwise the hook stays a
    /// sibling.
    /// </summary>
    private MacroNode ParseMacro(TokenCursor cursor)
    {
      string name = cursor.Current.Value;
      cursor.Advance();
      var args = _expressionParser.ParseArgumentList(cursor);
      var macro = new MacroNode { Name = name, Arguments = args };

      int offset = SkipBodyWhitespace(cursor);
      if (cursor.Peek(offset).Type == TokenType.HookOpen)
      {
        for (int i = 0; i <= offset; i++) cursor.Advance();
        macro.AttachedHook = ParseHookContents(cursor, HookAnchor.None, name: null);
      }

      return macro;
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

      string left = string.Empty;
      string right = string.Empty;
      bool sawArrow = false;
      bool arrowIsRight = false;

      while (!cursor.IsAtEnd && cursor.Current.Type != TokenType.LinkClose)
      {
        var t = cursor.Current;
        switch (t.Type)
        {
          case TokenType.Text:
            if (sawArrow) right += t.Value;
            else left += t.Value;
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

      if (!sawArrow) return new LinkNode { Text = left, Target = left };
      if (arrowIsRight) return new LinkNode { Text = left, Target = right };
      return new LinkNode { Text = right, Target = left };
    }
  }
}
