using System;
using System.Collections.Generic;
using System.Globalization;
using Harlowe.Ast.Expression;
using Harlowe.Tokens;

namespace Harlowe.Parsing
{
  /// <summary>
  /// Precedence-climbing parser that turns a token stream into an
  /// <see cref="IExpressionNode"/> tree. Implements the precedence ordering
  /// from the Harlowe 3.3.8 manual's "Operators and order-of-operations"
  /// appendix; in that table <em>lower</em> order numbers bind tighter, so
  /// order 1 (grouping) is the innermost and order 16 (<c>to</c>/<c>into</c>)
  /// is the outermost binary level. Order 17 (the comma) is not handled here —
  /// it is the macro-argument separator owned by <see cref="ParseArgumentList"/>.
  ///
  /// <para>
  /// All binary operators are treated as left-associative. <c>and</c> and
  /// <c>or</c> share precedence 13, so <c>$a or $b and $c</c> parses as
  /// <c>($a or $b) and $c</c> — the order written in source.
  /// </para>
  /// </summary>
  public class HarloweExpressionParser : IExpressionParser
  {
    // Current expression-recursion depth and its ceiling. Real authored
    // expressions nest only a handful deep; the cap exists solely to convert
    // adversarial/degenerate nesting into an in-prose parse error instead of a
    // host-killing StackOverflowException. Restored via finally, so it stays
    // balanced across the body parser's per-node recovery.
    private int _depth;
    private const int MaxDepth = 128;

    /// <summary>
    /// Binary operator → precedence order. Lower order = tighter binding.
    /// Mirrors the manual's table; entries that look unary (e.g. <c>-</c>)
    /// also appear in <see cref="UnaryPrefixOps"/> with their unary order.
    /// </summary>
    private static readonly Dictionary<string, int> BinaryOps = new Dictionary<string, int>
    {
      // Order 16
      { "to", 16 }, { "into", 16 },
      // Order 15 (lambda construction) — `where` / `when` / `via` / `making` /
      // `each` aren't real binary ops; they introduce a LambdaNode. ParseBinary
      // detects them via LambdaClauseKeywords and switches into ParseLambdaTail.
      // Order 14 (TypedVar)
      { "-type", 14 },
      // Order 13 (logical — same level intentionally)
      { "and", 13 }, { "or", 13 },
      // Order 12 (identity)
      { "is", 12 }, { "is not", 12 },
      // Order 11 (containment)
      { "contains", 11 }, { "does not contain", 11 }, { "is in", 11 }, { "is not in", 11 },
      // Order 10 (type / pattern). The `an` spellings are the same two
      // operators; CanonicalOperators folds them as the node is built, so only
      // the markup printer ever sees the article the author chose.
      { "is a", 10 }, { "is an", 10 },
      { "is not a", 10 }, { "is not an", 10 },
      { "matches", 10 }, { "does not match", 10 },
      // Order 9 (inequality)
      { "<", 9 }, { "<=", 9 }, { ">=", 9 }, { ">", 9 },
      // Order 8 (additive)
      { "+", 8 }, { "-", 8 },
      // Order 7 (multiplicative)
      { "*", 7 }, { "/", 7 }, { "%", 7 },
      // Order 4 (belonging — data on the right)
      { "of", 4 },
      // Order 3 (possessive — data on the left)
      { "'s", 3 },
    };

    /// <summary>
    /// Binary operators that bind to the right when chained at the same
    /// precedence — so <c>a of b of c</c> parses as <c>a of (b of c)</c>,
    /// not <c>(a of b) of c</c>. Mirrors reference Harlowe's precedence
    /// table in <c>ts/twinescript/runner.ts</c>, which marks the
    /// <c>belongingProperty</c>/<c>belongingOperator</c> row as
    /// <c>rightAssociative</c>. <c>'s</c> (the possessive sibling of
    /// <c>of</c>) is intentionally NOT in this set — possessive chains group
    /// leftward (<c>$cake's name's len</c> = <c>($cake's name)'s len</c>),
    /// which makes the two operators produce mirror-image trees with the
    /// same evaluated value.
    /// </summary>
    public static readonly HashSet<string> RightAssociativeOps = new HashSet<string>
    {
      "of",
    };

    /// <summary>
    /// Clause keywords that turn the surrounding expression into a lambda. Live
    /// at precedence 15 (between <c>to</c>/<c>into</c> and <c>-type</c>) but are
    /// not modeled as binary operators because the construct has up to three
    /// parts (parameter + <c>making</c> accumulator + <c>via</c> body) and a
    /// dedicated <see cref="LambdaNode"/>. <c>each</c> is the unary-prefix form
    /// (<c>each _x</c>) and handled separately.
    /// </summary>
    private static readonly HashSet<string> LambdaClauseKeywords = new HashSet<string>
    {
      "where", "when", "via", "making"
    };

    /// <summary>
    /// Unary prefix operator → precedence order. <c>+</c> and <c>-</c> appear
    /// here too; whether they are unary or binary is decided contextually by
    /// <see cref="ParseUnary"/> (unary at the start of an operand position).
    /// </summary>
    /// <summary>
    /// Operator spellings that mean an operator under a different name, mapped
    /// to the canonical one the evaluator dispatches on. Only the <c>is a</c>
    /// family needs this: reference accepts either English article
    /// (<c>is\s*an?\b</c>) for one operator. The author's article survives on
    /// <see cref="BinaryOpNode.SourceOperator"/> for reserialization.
    /// </summary>
    private static readonly Dictionary<string, string> CanonicalOperators =
      new Dictionary<string, string>
      {
        { "is an", "is a" },
        { "is not an", "is not a" },
      };

    /// <summary>
    /// Build a binary node, folding a non-canonical operator spelling onto its
    /// canonical form and remembering what the author wrote.
    /// </summary>
    private static BinaryOpNode MakeBinary(string spelling, IExpressionNode left, IExpressionNode right)
    {
      bool folded = CanonicalOperators.TryGetValue(spelling, out var canonical);
      return new BinaryOpNode
      {
        Operator = folded ? canonical : spelling,
        SourceOperator = folded ? spelling : null,
        Left = left,
        Right = right,
      };
    }

    private static readonly Dictionary<string, int> UnaryPrefixOps = new Dictionary<string, int>
    {
      // Order 6 (spread / bind)
      { "...", 6 }, { "bind", 6 }, { "2bind", 6 },
      // Order 5 (logical / sign)
      { "not", 5 }, { "+", 5 }, { "-", 5 },
      // Order 3 (possessive shorthand combining `it` with `'s`)
      { "its", 3 },
    };

    /// <summary>
    /// Parse a single expression at the loosest binary level (order 16). The
    /// cursor stops at the first token that does not extend the expression —
    /// typically a <c>,</c>, <c>)</c>, or end-of-file — and is left pointing
    /// at it.
    ///
    /// <para>
    /// Detects the implicit-<c>it</c> lambda shape (<c>where it &gt; 5</c>) at
    /// the entry: when the first token is a lambda clause keyword, build a
    /// <see cref="LambdaNode"/> with a null parameter — the evaluator binds
    /// <c>it</c> at invocation time.
    /// </para>
    /// </summary>
    public IExpressionNode ParseExpression(TokenCursor cursor)
      => ParseExpression(cursor, allowAssignmentAtTop: false);

    /// <summary>
    /// Internal entry that threads the assignment-allowed flag. The body
    /// parser hands <c>true</c> when an arg-list slot is the immediate
    /// argument of <c>(set:)</c> or <c>(put:)</c>; every sub-expression below
    /// resets to <c>false</c>, so an embedded <c>to</c>/<c>into</c> fails to
    /// parse rather than mutating during evaluation.
    /// </summary>
    private IExpressionNode ParseExpression(TokenCursor cursor, bool allowAssignmentAtTop)
    {
      SkipComments(cursor);
      var t = cursor.Current;
      // `each _x` is the body-iteration form for (for:). Parameter follows
      // the keyword rather than preceding it, so it's its own entry path.
      if (t.Type == TokenType.Operator && t.Value == "each")
        return ParseEachLambda(cursor);
      if (t.Type == TokenType.Operator && LambdaClauseKeywords.Contains(t.Value))
        return ParseLambdaTail(cursor, leftAsParam: null);
      return ParseBinary(cursor, 16, allowAssignmentAtTop);
    }

    /// <summary>
    /// Parse the <c>each _x</c> lambda form. Consumes <c>each</c>, expects a
    /// variable token, and produces a <see cref="LambdaNode"/> with
    /// <see cref="LambdaNode.IsEach"/> true. No clause body — <c>(for:)</c>
    /// uses this shape directly.
    /// </summary>
    private LambdaNode ParseEachLambda(TokenCursor cursor)
    {
      cursor.Advance(); // consume `each`
      var t = cursor.Current;
      if (t.Type != TokenType.Variable && t.Type != TokenType.TempVariable)
        throw new HarloweParseException("'each' must be followed by a variable", t.Line, t.Column);
      var node = new LambdaNode
      {
        ParameterName = t.Value,
        ParameterIsTemporary = t.Type == TokenType.TempVariable,
        IsEach = true
      };
      cursor.Advance();
      return node;
    }

    /// <summary>
    /// Parses a comma-separated argument list terminated by <see cref="TokenType.MacroClose"/>.
    /// Tolerates an empty list (<c>(foo:)</c>) and a single trailing comma
    /// (per the Harlowe manual, "trailing commas in macro calls are valid").
    /// Consumes the terminating <c>MacroClose</c> so the caller is left just
    /// past the macro. Throws <see cref="HarloweParseException"/> when an
    /// argument is followed by neither <c>,</c> nor <c>)</c> (e.g.
    /// <c>(if: $x $y)</c>) so malformed lists surface as in-prose parse
    /// errors instead of silently dropping tokens.
    /// </summary>
    public List<IExpressionNode> ParseArgumentList(TokenCursor cursor)
      => ParseArgumentList(cursor, allowAssignment: false);

    public List<IExpressionNode> ParseArgumentList(TokenCursor cursor, bool allowAssignment)
    {
      var args = new List<IExpressionNode>();

      // Comments first, so `(a: --2)` is `(a:)` — reference slices the
      // commented-out token from the arg stream before evaluating.
      SkipComments(cursor);
      if (cursor.Current.Type == TokenType.MacroClose)
      {
        cursor.Advance();
        return args;
      }

      while (true)
      {
        args.Add(ParseExpression(cursor, allowAssignmentAtTop: allowAssignment));

        if (cursor.Current.Type == TokenType.Comma)
        {
          cursor.Advance();
          if (cursor.Current.Type == TokenType.MacroClose)
          {
            cursor.Advance();
            return args;
          }
          continue;
        }

        if (cursor.Current.Type == TokenType.MacroClose)
        {
          cursor.Advance();
          return args;
        }

        // The expression ended but the cursor is on neither ',' nor ')'.
        // Throw rather than bail silently: the body parser's per-node
        // recovery turns this into an in-prose ParseErrorNode, whereas
        // returning partial args would silently drop the stray tokens and
        // detach any hook that followed the macro (rendering it
        // unconditionally).
        var stray = cursor.Current;
        string got = string.IsNullOrEmpty(stray.Value) ? stray.Type.ToString() : stray.Value;
        throw new HarloweParseException(
          $"expected ',' or ')' in macro argument list; got '{got}'",
          stray.Line, stray.Column);
      }
    }

    /// <summary>
    /// The macro names whose argument lists may carry top-level
    /// <c>to</c>/<c>into</c> assignment expressions. Hardcoded — there is no
    /// extension point for user-defined assignment macros in Harlowe.
    /// Name-normalized so case/dash variants ((Set:), (PUT:)) still permit the
    /// assignment form, matching the registry's case/dash-insensitive dispatch.
    /// </summary>
    public static bool IsAssignmentMacro(string name)
    {
      string n = MacroNames.Normalize(name);
      return n == "set" || n == "put" || n == "move" || n == "unpack";
    }

    /// <summary>
    /// Precedence-climbing core. Parses an operand via <see cref="ParseUnary"/>,
    /// then loops while the next token is a binary operator with order
    /// &lt;= <paramref name="maxOrder"/> (i.e. loose enough for this level).
    /// Recurses on the right-hand side at <c>order - 1</c> so binary operators
    /// stay left-associative.
    ///
    /// <para>
    /// <paramref name="allowAssignmentAtTop"/> gates the order-16
    /// <c>to</c>/<c>into</c> assignment operators. Only the immediate argument
    /// expression of <c>(set:)</c>/<c>(put:)</c>/<c>(move:)</c>/<c>(unpack:)</c> arrives with the flag set; any
    /// recursion into a sub-expression (RHS of a binary op, parenthesised
    /// group, macro argument, lambda clause) clears it, so a stray
    /// <c>to</c>/<c>into</c> below the top-of-arg position is a parse error
    /// rather than a silent mutation during evaluation.
    /// </para>
    /// </summary>
    private IExpressionNode ParseBinary(TokenCursor cursor, int maxOrder, bool allowAssignmentAtTop)
    {
      // Depth ceiling: ParseBinary sits on every expression recursion path
      // (parenthesised/nested-macro descent, right-associative RHS, and unary
      // chains), so capping it here bounds them all. Throwing keeps degenerate
      // input — thousands of nested '(' or 'not' — from blowing the C# stack
      // with an uncatchable StackOverflowException; the loader/per-node
      // recovery turns the throw into an in-prose error.
      if (++_depth > MaxDepth)
      {
        _depth--;
        var deep = cursor.Current;
        throw new HarloweParseException("expression nested too deeply", deep.Line, deep.Column);
      }
      try
      {
      var left = ParseUnary(cursor);

      while (true)
      {
        // A comment between operand and operator (or in place of the
        // operator) deletes itself + the next token, so `3 --2 + 6` is
        // `3 + 6` — matching reference's runner, where `comment` has the
        // loosest precedence and slices out its following token.
        SkipComments(cursor);
        var t = cursor.Current;
        if (t.Type != TokenType.Operator) break;
        if (LambdaClauseKeywords.Contains(t.Value))
        {
          // Lambda construction lives at precedence 15. If the surrounding
          // context is tighter (e.g. parsing the RHS of `is`), let the outer
          // level claim the keyword.
          if (maxOrder < 15) break;
          return ParseLambdaTail(cursor, leftAsParam: left);
        }
        if (!BinaryOps.TryGetValue(t.Value, out int order)) break;
        if (order > maxOrder) break;

        if (t.Value == "to" || t.Value == "into")
        {
          if (!allowAssignmentAtTop)
            throw new HarloweParseException(
              $"'{t.Value}' assignment is only allowed at the top of a (set:), (put:), (move:), or (unpack:) argument",
              t.Line, t.Column);
        }

        cursor.Advance();
        // RHS is a sub-expression — assignment is never allowed there.
        // Right-associative operators (currently just `of`) pull equal-
        // precedence siblings into the RHS subtree: passing `order` instead
        // of `order - 1` lets a chained `of` be claimed by the recursive
        // call. Matches reference Harlowe's rightAssociative row in
        // ts/twinescript/runner.ts.
        int rhsMaxOrder = RightAssociativeOps.Contains(t.Value) ? order : order - 1;
        var right = ParseBinary(cursor, rhsMaxOrder, allowAssignmentAtTop: false);
        left = MakeBinary(t.Value, left, right);
      }

      return left;
      }
      finally { _depth--; }
    }

    /// <summary>
    /// Consumes a lambda's clause tail and returns a <see cref="LambdaNode"/>.
    /// <paramref name="leftAsParam"/> is the already-parsed parameter
    /// expression (which must be a bare <see cref="VariableRefNode"/>) or
    /// <c>null</c> for the implicit-<c>it</c> form.
    ///
    /// <para>
    /// Accepts <c>where</c> alone, <c>via</c> alone, the chained
    /// <c>where ... via ...</c> filter-then-transform, a trailing
    /// <c>via ... where ...</c> (either shape, one <c>where</c> total), and
    /// the fold shape <c>_item making _acc via ...</c> (item parameter first,
    /// accumulator after <c>making</c>, body required, optional trailing
    /// <c>where</c> filter). <c>when</c> remains unsupported.
    /// </para>
    /// </summary>
    private LambdaNode ParseLambdaTail(TokenCursor cursor, IExpressionNode leftAsParam)
    {
      var node = new LambdaNode();

      if (leftAsParam != null)
      {
        if (!(leftAsParam is VariableRefNode vref))
        {
          var bad = cursor.Current;
          throw new HarloweParseException("lambda parameter must be a variable", bad.Line, bad.Column);
        }
        node.ParameterName = vref.Name;
        node.ParameterIsTemporary = vref.IsTemporary;
      }

      var t = cursor.Current;

      // `making` introduces a fold lambda's accumulator. Per Harlowe spec the
      // shape is `_item making _acc via <body>` — the leading param binds
      // each item, the post-`making` variable holds the running total, and
      // `via` is required to give the fold body.
      if (t.Type == TokenType.Operator && t.Value == "making")
      {
        if (leftAsParam == null)
          throw new HarloweParseException("'making' requires an item parameter before it (e.g. `_item making _acc via ...`)", t.Line, t.Column);
        cursor.Advance();
        var accTok = cursor.Current;
        if (accTok.Type != TokenType.Variable && accTok.Type != TokenType.TempVariable)
          throw new HarloweParseException("'making' must be followed by a variable for the accumulator", accTok.Line, accTok.Column);
        node.MakingName = accTok.Value;
        node.MakingIsTemporary = accTok.Type == TokenType.TempVariable;
        cursor.Advance();
        SkipComments(cursor);
        t = cursor.Current;
        if (t.Type != TokenType.Operator || t.Value != "via")
          throw new HarloweParseException("'making' must be followed by 'via' giving the fold body", t.Line, t.Column);
      }

      if (t.Type != TokenType.Operator || (t.Value != "where" && t.Value != "via"))
        throw new HarloweParseException($"'{t.Value}' lambda clause is not yet supported (v2.3 ships `where`, `via`, `making`)", t.Line, t.Column);

      // `where` may be followed by `via` to filter-then-transform; `via` alone
      // is the pure transform or fold body. Both clause bodies parse at order
      // 14 — one tighter than the lambda level itself — so a stray clause
      // keyword belongs to an outer construct rather than getting swallowed
      // here.
      if (t.Value == "where")
      {
        cursor.Advance();
        node.WhereClause = ParseBinary(cursor, 14, allowAssignmentAtTop: false);
        t = cursor.Current;
      }

      if (t.Type == TokenType.Operator && t.Value == "via")
      {
        cursor.Advance();
        node.ViaClause = ParseBinary(cursor, 14, allowAssignmentAtTop: false);
      }

      // A trailing `where` after the via body — the fold-filter shape from
      // reference's (folded:) doc, `_item making _total via _total + _item
      // where _item > 0`. The via clause parses at order 14 (one tighter than
      // the lambda level), so the keyword survives to here. Only claimed when
      // no `where` was given up front; a second one is a stray token and
      // errors through the caller as before.
      var trailing = cursor.Current;
      if (node.WhereClause == null && node.ViaClause != null
          && trailing.Type == TokenType.Operator && trailing.Value == "where")
      {
        cursor.Advance();
        node.WhereClause = ParseBinary(cursor, 14, allowAssignmentAtTop: false);
      }

      return node;
    }

    /// <summary>
    /// Unary-prefix dispatch. If the current token is a registered unary prefix
    /// (e.g. <c>not</c>, unary <c>-</c>, <c>...</c>, <c>bind</c>, <c>2bind</c>,
    /// <c>its</c>), consumes it and recursively parses the operand at one
    /// level tighter than the prefix. Otherwise falls through to
    /// <see cref="ParseAtom"/>.
    /// </summary>
    private IExpressionNode ParseUnary(TokenCursor cursor)
    {
      SkipComments(cursor);
      var t = cursor.Current;
      if (t.Type == TokenType.Operator && UnaryPrefixOps.TryGetValue(t.Value, out int order))
      {
        cursor.Advance();
        // Operand is a sub-expression; assignment is never allowed there.
        var operand = ParseBinary(cursor, order - 1, allowAssignmentAtTop: false);
        return new UnaryOpNode { Operator = t.Value, Operand = operand };
      }
      return ParseAtom(cursor);
    }

    /// <summary>
    /// Parses the smallest indivisible expression: a literal, variable,
    /// identifier, parenthesised sub-expression, or a nested macro call (which
    /// is a value-producing macro — see <see cref="MacroCallNode"/>). Throws
    /// when the current token cannot start an expression; the body parser is
    /// expected to validate the surrounding token stream and avoid feeding us
    /// junk.
    /// </summary>
    private IExpressionNode ParseAtom(TokenCursor cursor)
    {
      var t = cursor.Current;
      switch (t.Type)
      {
        case TokenType.NumberLiteral:
          cursor.Advance();
          return new LiteralNode
          {
            Kind = LiteralKind.Number,
            Value = double.Parse(t.Value, CultureInfo.InvariantCulture)
          };

        case TokenType.StringLiteral:
          cursor.Advance();
          return new LiteralNode { Kind = LiteralKind.String, Value = t.Value };

        case TokenType.BoolLiteral:
          cursor.Advance();
          return new LiteralNode { Kind = LiteralKind.Bool, Value = t.Value == "true" };

        case TokenType.ColourLiteral:
          cursor.Advance();
          // Value keeps the raw lexeme ("red", "#a4e") so MarkupPrinter
          // round-trips the author's spelling; the evaluator converts.
          return new LiteralNode { Kind = LiteralKind.Colour, Value = t.Value };

        case TokenType.DatatypeLiteral:
          cursor.Advance();
          // Same deal: the raw lexeme ("num", "number") round-trips, and the
          // evaluator folds the long spellings onto the canonical name.
          return new LiteralNode { Kind = LiteralKind.Datatype, Value = t.Value };

        case TokenType.Variable:
          cursor.Advance();
          return new VariableRefNode { Name = t.Value, IsTemporary = false };

        case TokenType.TempVariable:
          cursor.Advance();
          return new VariableRefNode { Name = t.Value, IsTemporary = true };

        case TokenType.Identifier:
          cursor.Advance();
          return new IdentifierNode { Name = t.Value };

        case TokenType.HookRef:
          cursor.Advance();
          var hookRef = new HookRefNode { Name = t.Value };
          // Fold `'s <ordinal>` chains into ordinal narrowing steps right here,
          // ahead of the precedence climber: `?cake's 1st` is one narrowed
          // reference, not a `'s` BinaryOpNode. A `'s` followed by a
          // non-ordinal name is left for ParseBinary to claim as ordinary
          // property access (which the evaluator then rejects for a hook name).
          while (cursor.Current.Type == TokenType.Operator && cursor.Current.Value == "'s"
                 && cursor.Peek().Type == TokenType.Identifier
                 && Ordinals.TryParse(cursor.Peek().Value, out int ordIndex, out bool ordFromEnd))
          {
            cursor.Advance(); // 's
            cursor.Advance(); // ordinal identifier
            hookRef.Steps.Add(new HookRefStep { Index = ordIndex, FromEnd = ordFromEnd });
          }
          return hookRef;

        case TokenType.MacroOpen:
          string name = t.Value;
          cursor.Advance();
          // Nested macro: arg-list assignment-allowed iff this call is (set:)/(put:).
          return new MacroCallNode
          {
            Name = name,
            Arguments = ParseArgumentList(cursor, IsAssignmentMacro(name)),
            Line = t.Line,
            Column = t.Column
          };

        case TokenType.ParenOpen:
          cursor.Advance();
          // Grouping paren is a sub-expression; assignment never allowed inside.
          var inner = ParseExpression(cursor, allowAssignmentAtTop: false);
          if (cursor.Current.Type == TokenType.ParenClose) cursor.Advance();
          return inner;
      }

      throw new HarloweParseException($"Unexpected token in expression: {t.Type}({t.Value})", t.Line, t.Column);
    }

    /// <summary>
    /// Consumes every <see cref="TokenType.Comment"/> (<c>--</c>) at the
    /// cursor along with the one construct each eliminates — reference's
    /// <c>runner.ts</c> <c>comment</c> branch: "Treat the next token as if it
    /// didn't exist (by slicing it out)". Called wherever the parser is about
    /// to read an operand or operator, so <c>(set: $x to --2 6)</c> assigns 6
    /// and <c>3 --2 + 6</c> is <c>3 + 6</c>. No-op when the cursor isn't at a
    /// comment.
    /// </summary>
    private static void SkipComments(TokenCursor cursor)
    {
      while (cursor.Current.Type == TokenType.Comment)
      {
        cursor.Advance();
        SkipOneAtomSpan(cursor);
      }
    }

    /// <summary>
    /// Advances past the one thing a <c>--</c> comment eliminates. Reference
    /// removes one <em>folded</em> token — a whole nested macro call, grouping
    /// paren, or code hook is a single unit there — so an opener here skips
    /// its balanced span (which is also what makes <c>--[a note]</c> comment
    /// hooks work inside macro args: the hook lexes as a bracket span in
    /// expression mode). A closer is never consumed: reference's folded
    /// stream ends before the construct's own closing token, so eating one
    /// here would desync the parser from its enclosing macro/group.
    /// </summary>
    private static void SkipOneAtomSpan(TokenCursor cursor)
    {
      switch (cursor.Current.Type)
      {
        case TokenType.EndOfFile:
        case TokenType.MacroClose:
        case TokenType.ParenClose:
        case TokenType.BracketClose:
          return;

        case TokenType.MacroOpen:
        case TokenType.ParenOpen:
        case TokenType.BracketOpen:
          int depth = 0;
          do
          {
            var t = cursor.Current.Type;
            if (t == TokenType.EndOfFile) return;
            if (t == TokenType.MacroOpen || t == TokenType.ParenOpen || t == TokenType.BracketOpen) depth++;
            else if (t == TokenType.MacroClose || t == TokenType.ParenClose || t == TokenType.BracketClose) depth--;
            cursor.Advance();
          } while (depth > 0);
          return;

        default:
          cursor.Advance();
          return;
      }
    }
  }
}
