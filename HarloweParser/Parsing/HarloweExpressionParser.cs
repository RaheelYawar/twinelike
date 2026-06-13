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
      // Order 10 (type / pattern)
      { "is a", 10 }, { "is not a", 10 }, { "matches", 10 }, { "does not match", 10 },
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
      return n == "set" || n == "put";
    }

    /// <summary>
    /// True if <paramref name="node"/> is a `'s` chain (the AST shape for
    /// property/ordinal access like <c>$x's name</c> or <c>$arr's 1st</c>).
    /// Used to reject property assignment for either side of `to`/`into`
    /// before the AST escapes the parser.
    /// </summary>
    private static bool IsPropertyAccess(IExpressionNode node)
      => node is BinaryOpNode bin && bin.Operator == "'s";

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
    /// expression of <c>(set:)</c>/<c>(put:)</c> arrives with the flag set; any
    /// recursion into a sub-expression (RHS of a binary op, parenthesised
    /// group, macro argument, lambda clause) clears it, so a stray
    /// <c>to</c>/<c>into</c> below the top-of-arg position is a parse error
    /// rather than a silent mutation during evaluation.
    /// </para>
    /// </summary>
    private IExpressionNode ParseBinary(TokenCursor cursor, int maxOrder, bool allowAssignmentAtTop)
    {
      var left = ParseUnary(cursor);

      while (true)
      {
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
              $"'{t.Value}' assignment is only allowed at the top of a (set:) or (put:) argument",
              t.Line, t.Column);
          // Property assignment (`$x's name to ...`, `$arr's 1st to ...`) is
          // documented in Harlowe but not yet implemented here. Surface a
          // pointed parse-time error so authors don't get the generic
          // "assignment target must be a variable" from the evaluator —
          // that message hides which shape they tried to write. Targets
          // sit on the LHS of `to` and on the RHS of `into`, so the check
          // runs for both sides below.
          if (t.Value == "to" && IsPropertyAccess(left))
            throw new HarloweParseException(
              $"property assignment ('$x's ... to ...') is not supported; assign to a whole variable instead",
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
        if (t.Value == "into" && IsPropertyAccess(right))
          throw new HarloweParseException(
            $"property assignment ('... into $x's ...') is not supported; assign to a whole variable instead",
            t.Line, t.Column);
        left = new BinaryOpNode { Operator = t.Value, Left = left, Right = right };
      }

      return left;
    }

    /// <summary>
    /// Consumes a lambda's clause tail and returns a <see cref="LambdaNode"/>.
    /// <paramref name="leftAsParam"/> is the already-parsed parameter
    /// expression (which must be a bare <see cref="VariableRefNode"/>) or
    /// <c>null</c> for the implicit-<c>it</c> form.
    ///
    /// <para>
    /// Accepts <c>where</c> alone, <c>via</c> alone, the chained
    /// <c>where ... via ...</c> filter-then-transform, and the fold shape
    /// <c>_item making _acc via ...</c> (item parameter first, accumulator
    /// after <c>making</c>, body required). <c>when</c> remains unsupported.
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
                 && TryParseHookOrdinal(cursor.Peek().Value, out int ordIndex, out bool ordFromEnd))
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
          return new MacroCallNode { Name = name, Arguments = ParseArgumentList(cursor, IsAssignmentMacro(name)) };

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
    /// Parses an ordinal accessor name (<c>last</c>, <c>1st</c>, <c>2nd</c>,
    /// <c>2ndlast</c>, …) into a 1-based index and a from-the-end flag, for
    /// folding a <c>?name's …</c> chain into <see cref="HookRefStep"/>s. Returns
    /// false for any non-ordinal name so the caller leaves the <c>'s</c> for the
    /// binary-operator path. Mirrors the permissive ordinal handling in
    /// <c>ExpressionEvaluator</c> — the <c>st</c>/<c>nd</c>/<c>rd</c>/<c>th</c>
    /// suffix is decorative.
    /// </summary>
    private static bool TryParseHookOrdinal(string name, out int index, out bool fromEnd)
    {
      index = 0;
      fromEnd = false;
      if (string.IsNullOrEmpty(name)) return false;
      if (name == "last") { index = 1; fromEnd = true; return true; }
      int p = 0;
      while (p < name.Length && char.IsDigit(name[p])) p++;
      if (p == 0) return false;
      if (p + 2 > name.Length) return false;
      string suffix = name.Substring(p, 2);
      if (suffix != "st" && suffix != "nd" && suffix != "rd" && suffix != "th") return false;
      int after = p + 2;
      if (!int.TryParse(name.Substring(0, p), out int n)) return false;
      if (after == name.Length) { index = n; fromEnd = false; return true; }
      if (after + 4 == name.Length && name.Substring(after, 4) == "last")
      { index = n; fromEnd = true; return true; }
      return false;
    }
  }
}
