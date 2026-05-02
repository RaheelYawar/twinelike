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
      // Order 15 (lambda construction)
      { "where", 15 }, { "when", 15 }, { "via", 15 }, { "making", 15 }, { "each", 15 },
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
      { "*", 7 }, { "/", 7 },
      // Order 4 (belonging — data on the right)
      { "of", 4 },
      // Order 3 (possessive — data on the left)
      { "'s", 3 },
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
    /// </summary>
    public IExpressionNode ParseExpression(TokenCursor cursor)
    {
      return ParseBinary(cursor, 16);
    }

    /// <summary>
    /// Parses a comma-separated argument list terminated by <see cref="TokenType.MacroClose"/>.
    /// Tolerates an empty list (<c>(foo:)</c>) and a single trailing comma
    /// (per the Harlowe manual, "trailing commas in macro calls are valid").
    /// Consumes the terminating <c>MacroClose</c> so the caller is left just
    /// past the macro.
    /// </summary>
    public List<IExpressionNode> ParseArgumentList(TokenCursor cursor)
    {
      var args = new List<IExpressionNode>();

      if (cursor.Current.Type == TokenType.MacroClose)
      {
        cursor.Advance();
        return args;
      }

      while (true)
      {
        args.Add(ParseExpression(cursor));

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

        // Malformed input — bail to avoid an infinite loop. The body parser
        // will surface this as a diagnostic at a higher level.
        break;
      }

      return args;
    }

    /// <summary>
    /// Precedence-climbing core. Parses an operand via <see cref="ParseUnary"/>,
    /// then loops while the next token is a binary operator with order
    /// &lt;= <paramref name="maxOrder"/> (i.e. loose enough for this level).
    /// Recurses on the right-hand side at <c>order - 1</c> so binary operators
    /// stay left-associative.
    /// </summary>
    private IExpressionNode ParseBinary(TokenCursor cursor, int maxOrder)
    {
      var left = ParseUnary(cursor);

      while (true)
      {
        var t = cursor.Current;
        if (t.Type != TokenType.Operator) break;
        if (!BinaryOps.TryGetValue(t.Value, out int order)) break;
        if (order > maxOrder) break;

        cursor.Advance();
        var right = ParseBinary(cursor, order - 1);
        left = new BinaryOpNode { Operator = t.Value, Left = left, Right = right };
      }

      return left;
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
        var operand = ParseBinary(cursor, order - 1);
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

        case TokenType.MacroOpen:
          string name = t.Value;
          cursor.Advance();
          return new MacroCallNode { Name = name, Arguments = ParseArgumentList(cursor) };

        case TokenType.ParenOpen:
          cursor.Advance();
          var inner = ParseExpression(cursor);
          if (cursor.Current.Type == TokenType.ParenClose) cursor.Advance();
          return inner;
      }

      throw new Exception($"Unexpected token in expression: {t.Type}({t.Value}) at line {t.Line}, col {t.Column}");
    }
  }
}
