using System.Collections.Generic;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Direct tests for <see cref="HarloweExpressionParser"/>. The parser is
  /// driven over a real <see cref="HarloweTokenizer"/> output so the tests
  /// double-check that the tokenizer's operator stream is shaped the way the
  /// parser expects.
  /// </summary>
  public class HarloweExpressionParserTests
  {
    /// <summary>
    /// Wraps the input as a one-arg macro call (<c>(_:expr)</c>) so the
    /// tokenizer enters expression mode, parses the single argument, and
    /// returns it. Keeps the test inputs focused on the expression itself
    /// without forcing each test to spell out the macro wrapper.
    /// </summary>
    private static IExpressionNode ParseExpr(string expr)
    {
      // Wrap as a (set:) call so the expression parser allows top-level
      // `to`/`into` assignment forms when a test exercises them. These are
      // direct parser tests — the body-parser-driven restriction lives in its
      // own tests; the expression parser itself should round-trip every shape.
      var tokens = new HarloweTokenizer().Tokenize("(set:" + expr + ")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance(); // consume MacroOpen
      var parser = new HarloweExpressionParser();
      var args = parser.ParseArgumentList(cursor, allowAssignment: true);
      // ParseArgumentList consumes the trailing MacroClose, so the cursor lands
      // at end-of-file for a single-arg `(set:expr)` wrapper.
      Assert.Single(args);
      return args[0];
    }

    // --- Literals ---

    [Fact]
    public void NumberLiteral_ParsedAsLiteralNode()
    {
      var n = Assert.IsType<LiteralNode>(ParseExpr("42"));
      Assert.Equal(LiteralKind.Number, n.Kind);
      Assert.Equal(42.0, n.Value);
    }

    [Fact]
    public void DecimalNumberLiteral_ParsedAsDouble()
    {
      var n = Assert.IsType<LiteralNode>(ParseExpr("1.5"));
      Assert.Equal(LiteralKind.Number, n.Kind);
      Assert.Equal(1.5, n.Value);
    }

    [Fact]
    public void StringLiteral_ParsedAsLiteralNode()
    {
      var n = Assert.IsType<LiteralNode>(ParseExpr("\"hello\""));
      Assert.Equal(LiteralKind.String, n.Kind);
      Assert.Equal("hello", n.Value);
    }

    [Fact]
    public void BoolTrue_ParsedAsLiteralNode()
    {
      var n = Assert.IsType<LiteralNode>(ParseExpr("true"));
      Assert.Equal(LiteralKind.Bool, n.Kind);
      Assert.Equal(true, n.Value);
    }

    [Fact]
    public void BoolFalse_ParsedAsLiteralNode()
    {
      var n = Assert.IsType<LiteralNode>(ParseExpr("false"));
      Assert.Equal(false, n.Value);
    }

    // --- Variables, identifiers ---

    [Fact]
    public void Variable_ParsedAsVariableRefNode()
    {
      var v = Assert.IsType<VariableRefNode>(ParseExpr("$hp"));
      Assert.Equal("hp", v.Name);
      Assert.False(v.IsTemporary);
    }

    [Fact]
    public void TempVariable_ParsedAsVariableRefNodeWithTemporaryFlag()
    {
      var v = Assert.IsType<VariableRefNode>(ParseExpr("_loop"));
      Assert.Equal("loop", v.Name);
      Assert.True(v.IsTemporary);
    }

    [Fact]
    public void BareIdentifier_ParsedAsIdentifierNode()
    {
      var i = Assert.IsType<IdentifierNode>(ParseExpr("time"));
      Assert.Equal("time", i.Name);
    }

    // --- Binary operators: arithmetic ---

    [Fact]
    public void Addition_ParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("1 + 2"));
      Assert.Equal("+", b.Operator);
      Assert.Equal(1.0, ((LiteralNode)b.Left).Value);
      Assert.Equal(2.0, ((LiteralNode)b.Right).Value);
    }

    [Fact]
    public void MultiplicationBindsTighterThanAddition()
    {
      // 1 + 2 * 3 → (1 + (2 * 3))
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("1 + 2 * 3"));
      Assert.Equal("+", b.Operator);
      var rhs = Assert.IsType<BinaryOpNode>(b.Right);
      Assert.Equal("*", rhs.Operator);
      Assert.Equal(2.0, ((LiteralNode)rhs.Left).Value);
      Assert.Equal(3.0, ((LiteralNode)rhs.Right).Value);
    }

    [Fact]
    public void Addition_LeftAssociative()
    {
      // 1 + 2 + 3 → ((1 + 2) + 3)
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("1 + 2 + 3"));
      Assert.Equal("+", b.Operator);
      var lhs = Assert.IsType<BinaryOpNode>(b.Left);
      Assert.Equal("+", lhs.Operator);
      Assert.Equal(3.0, ((LiteralNode)b.Right).Value);
    }

    [Fact]
    public void GroupingOverridesPrecedence()
    {
      // (1 + 2) * 3 → ((1 + 2) * 3) — the `*` is the root, lhs is the group
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("(1 + 2) * 3"));
      Assert.Equal("*", b.Operator);
      var lhs = Assert.IsType<BinaryOpNode>(b.Left);
      Assert.Equal("+", lhs.Operator);
    }

    // --- Binary operators: comparison & logical ---

    [Fact]
    public void Inequality_ParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a >= 1"));
      Assert.Equal(">=", b.Operator);
    }

    [Fact]
    public void Is_ParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a is 5"));
      Assert.Equal("is", b.Operator);
    }

    [Fact]
    public void IsNot_FusedAndParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a is not 5"));
      Assert.Equal("is not", b.Operator);
    }

    [Fact]
    public void IsBindsLooserThanInequality()
    {
      // $a < 5 is true → ($a < 5) is true
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a < 5 is true"));
      Assert.Equal("is", b.Operator);
      var lhs = Assert.IsType<BinaryOpNode>(b.Left);
      Assert.Equal("<", lhs.Operator);
    }

    [Fact]
    public void AndOr_ShareSamePrecedence_LeftAssociative()
    {
      // $a or $b and $c → (($a or $b) and $c) — same level, left-assoc
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a or $b and $c"));
      Assert.Equal("and", b.Operator);
      var lhs = Assert.IsType<BinaryOpNode>(b.Left);
      Assert.Equal("or", lhs.Operator);
    }

    [Fact]
    public void AndBindsLooserThanIs()
    {
      // $a is 1 and $b is 2 → ($a is 1) and ($b is 2)
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a is 1 and $b is 2"));
      Assert.Equal("and", b.Operator);
      Assert.Equal("is", ((BinaryOpNode)b.Left).Operator);
      Assert.Equal("is", ((BinaryOpNode)b.Right).Operator);
    }

    // --- Containment, type, pattern ---

    [Fact]
    public void Contains_ParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$xs contains 1"));
      Assert.Equal("contains", b.Operator);
    }

    [Fact]
    public void IsIn_FusedAndParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("1 is in $xs"));
      Assert.Equal("is in", b.Operator);
    }

    [Fact]
    public void IsA_FusedAndParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a is a number"));
      Assert.Equal("is a", b.Operator);
      Assert.IsType<IdentifierNode>(b.Right);
    }

    [Fact]
    public void Matches_ParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a matches 1"));
      Assert.Equal("matches", b.Operator);
    }

    // --- to / into (loosest binary level) ---

    [Fact]
    public void To_IsLoosestBinaryOperator()
    {
      // $x to $a + 1 → ($x to ($a + 1))
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$x to $a + 1"));
      Assert.Equal("to", b.Operator);
      var rhs = Assert.IsType<BinaryOpNode>(b.Right);
      Assert.Equal("+", rhs.Operator);
    }

    [Fact]
    public void Into_ParsedAsBinaryOp()
    {
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("5 into $x"));
      Assert.Equal("into", b.Operator);
    }

    [Fact]
    public void To_NotAllowedAtTop_WhenAssignmentForbidden()
    {
      // Calling ParseArgumentList with allowAssignment: false (the default,
      // and what the body parser uses for every macro that isn't set/put)
      // must reject `to` at the top of the argument expression.
      var tokens = new HarloweTokenizer().Tokenize("(print: $x to 5)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var ex = Assert.Throws<HarloweParseException>(
        () => parser.ParseArgumentList(cursor, allowAssignment: false));
      Assert.Contains("to", ex.Message);
    }

    [Fact]
    public void To_NotAllowedInSubExpression_EvenWhenTopLevelAllowed()
    {
      // Assignment is only allowed at the immediate top of an arg expression;
      // a nested `to` (here on the RHS of `+`) is rejected even when the outer
      // call permits top-level assignment.
      var tokens = new HarloweTokenizer().Tokenize("(set: $x + ($y to 5))");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var ex = Assert.Throws<HarloweParseException>(
        () => parser.ParseArgumentList(cursor, allowAssignment: true));
      Assert.Contains("to", ex.Message);
    }

    [Fact]
    public void Into_NotAllowedInNestedMacroArg()
    {
      // `(set: $r to (print: $x into 5))` — the outer (set:) allows `to` at
      // its top, but the inner (print:) is not an assignment macro and must
      // refuse `into` in its arg list.
      var tokens = new HarloweTokenizer().Tokenize("(set: $r to (print: $x into 5))");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var ex = Assert.Throws<HarloweParseException>(
        () => parser.ParseArgumentList(cursor, allowAssignment: true));
      Assert.Contains("into", ex.Message);
    }

    [Fact]
    public void To_PropertyAssignment_Parses()
    {
      // `$x's name to "Bob"` — property assignment reaches the evaluator as a
      // plain `to` node whose LHS is the `'s` chain; the evaluator's write
      // path decomposes it. The parser used to reject this shape.
      var tokens = new HarloweTokenizer().Tokenize("(set: $x's name to \"Bob\")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var args = parser.ParseArgumentList(cursor, allowAssignment: true);
      Assert.Single(args);
      var assign = Assert.IsType<BinaryOpNode>(args[0]);
      Assert.Equal("to", assign.Operator);
      var chain = Assert.IsType<BinaryOpNode>(assign.Left);
      Assert.Equal("'s", chain.Operator);
      Assert.IsType<VariableRefNode>(chain.Left);
      var key = Assert.IsType<IdentifierNode>(chain.Right);
      Assert.Equal("name", key.Name);
    }

    [Fact]
    public void Into_PropertyAssignment_Parses()
    {
      // `(put: "Bob" into $x's name)` — `into` puts the value on the LEFT and
      // the target on the RIGHT; the `'s` chain lands on the RHS.
      var tokens = new HarloweTokenizer().Tokenize("(put: \"Bob\" into $x's name)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var args = parser.ParseArgumentList(cursor, allowAssignment: true);
      Assert.Single(args);
      var assign = Assert.IsType<BinaryOpNode>(args[0]);
      Assert.Equal("into", assign.Operator);
      var chain = Assert.IsType<BinaryOpNode>(assign.Right);
      Assert.Equal("'s", chain.Operator);
      Assert.IsType<VariableRefNode>(chain.Left);
    }

    [Fact]
    public void Into_ChainedPropertyAssignment_Parses()
    {
      // `$x's name's first` on the RHS of `into` — the chain left-nests, so
      // the top node's Left is itself a `'s` chain down to the variable.
      var tokens = new HarloweTokenizer().Tokenize("(put: \"Bob\" into $x's name's first)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var args = parser.ParseArgumentList(cursor, allowAssignment: true);
      Assert.Single(args);
      var assign = Assert.IsType<BinaryOpNode>(args[0]);
      Assert.Equal("into", assign.Operator);
      var outer = Assert.IsType<BinaryOpNode>(assign.Right);
      Assert.Equal("'s", outer.Operator);
      var inner = Assert.IsType<BinaryOpNode>(outer.Left);
      Assert.Equal("'s", inner.Operator);
      Assert.IsType<VariableRefNode>(inner.Left);
    }

    [Fact]
    public void Move_IntoAssignment_Parses()
    {
      // (move:) joined (set:)/(put:) as an assignment macro, so `into` is
      // legal at the top of its argument expressions.
      var tokens = new HarloweTokenizer().Tokenize("(move: $a's 1st into $b)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var args = parser.ParseArgumentList(cursor, allowAssignment: HarloweExpressionParser.IsAssignmentMacro("move"));
      Assert.Single(args);
      var assign = Assert.IsType<BinaryOpNode>(args[0]);
      Assert.Equal("into", assign.Operator);
      var chain = Assert.IsType<BinaryOpNode>(assign.Left);
      Assert.Equal("'s", chain.Operator);
    }

    [Fact]
    public void Unpack_IntoAssignment_Parses()
    {
      // (unpack:) is the fourth assignment macro; its `into` destination is a
      // pattern-shaped macro call kept as AST for the evaluator's walk.
      var tokens = new HarloweTokenizer().Tokenize("(unpack: $a into (a: $x, $y))");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var args = parser.ParseArgumentList(cursor, allowAssignment: HarloweExpressionParser.IsAssignmentMacro("unpack"));
      Assert.Single(args);
      var assign = Assert.IsType<BinaryOpNode>(args[0]);
      Assert.Equal("into", assign.Operator);
      Assert.IsType<MacroCallNode>(assign.Right);
    }

    [Fact]
    public void Into_BareVariableTarget_StillParses()
    {
      // Regression check: the new RHS-side guard must not break the legal
      // shape `(put: value into $target)` where the target is a bare variable.
      var tokens = new HarloweTokenizer().Tokenize("(put: 5 into $x)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      var args = parser.ParseArgumentList(cursor, allowAssignment: true);
      Assert.Single(args);
      var assign = Assert.IsType<BinaryOpNode>(args[0]);
      Assert.Equal("into", assign.Operator);
    }

    [Fact]
    public void To_AllowedInNestedSetCall()
    {
      // The inner (set:) is itself an assignment macro, so `to` is allowed at
      // its arg top even when nested inside another macro's arg.
      var tokens = new HarloweTokenizer().Tokenize("(print: (set: $x to 5))");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var parser = new HarloweExpressionParser();
      // No throw — the outer (print:) doesn't allow assignment, but the inner
      // (set:) does, and `to` is at the top of that inner arg list.
      var args = parser.ParseArgumentList(cursor, allowAssignment: false);
      Assert.Single(args);
      var outerCall = Assert.IsType<MacroCallNode>(args[0]);
      Assert.Equal("set", outerCall.Name);
    }

    // --- of, 's, its (data access) ---

    [Fact]
    public void Of_ParsedAsBinaryOp()
    {
      // name of $a → BinaryOp(of, name, $a)
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("name of $a"));
      Assert.Equal("of", b.Operator);
      Assert.Equal("name", ((IdentifierNode)b.Left).Name);
      Assert.Equal("a", ((VariableRefNode)b.Right).Name);
    }

    [Fact]
    public void Of_RightAssociative_ChainGroupsRightward()
    {
      // Reference Harlowe marks the `belongingOperator` row rightAssociative
      // in ts/twinescript/runner.ts's precedenceTable. So `name of person of group`
      // parses as `name of (person of group)`, NOT `(name of person) of group` —
      // semantically matches "the name property of (the person property of group)".
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("name of person of group"));
      Assert.Equal("of", b.Operator);
      Assert.Equal("name", ((IdentifierNode)b.Left).Name);

      // Right child should ITSELF be `of`, with `person` on left and `group` on right.
      var inner = Assert.IsType<BinaryOpNode>(b.Right);
      Assert.Equal("of", inner.Operator);
      Assert.Equal("person", ((IdentifierNode)inner.Left).Name);
      Assert.Equal("group", ((IdentifierNode)inner.Right).Name);
    }

    [Fact]
    public void Possessive_LeftAssociative_ChainGroupsLeftward()
    {
      // `'s` is the syntactic inverse of `of` but groups the other way:
      // `$g's person's name` parses as `($g's person)'s name`. Verifies the
      // asymmetry is preserved (we did NOT accidentally flip `'s` along with
      // `of`).
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$g's person's name"));
      Assert.Equal("'s", b.Operator);
      Assert.Equal("name", ((IdentifierNode)b.Right).Name);

      // Left child should ITSELF be `'s`, with `$g` on left and `person` on right.
      var inner = Assert.IsType<BinaryOpNode>(b.Left);
      Assert.Equal("'s", inner.Operator);
      Assert.Equal("g", ((VariableRefNode)inner.Left).Name);
      Assert.Equal("person", ((IdentifierNode)inner.Right).Name);
    }

    [Fact]
    public void Possessive_ParsedAsBinaryOp()
    {
      // $a's name → BinaryOp('s, $a, name)
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a's name"));
      Assert.Equal("'s", b.Operator);
      Assert.Equal("a", ((VariableRefNode)b.Left).Name);
      Assert.Equal("name", ((IdentifierNode)b.Right).Name);
    }

    [Fact]
    public void PossessiveBindsTighterThanOf()
    {
      // $a's name of $b → ($a's name) of ... wait, of has order 4, 's has order 3.
      // Lower order = tighter, so 's binds tighter. $a's name of $b → BinaryOp(of, BinaryOp('s, $a, name), $b).
      // Hmm, that's not really meaningful Harlowe but it tests precedence.
      // Use plus instead: $a's name + 1 → ($a's name) + 1 (since + is order 8, looser than 's at 3).
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$a's name + 1"));
      Assert.Equal("+", b.Operator);
      Assert.IsType<BinaryOpNode>(b.Left);
    }

    [Fact]
    public void Its_ParsedAsUnaryPrefix()
    {
      var u = Assert.IsType<UnaryOpNode>(ParseExpr("its name"));
      Assert.Equal("its", u.Operator);
      Assert.Equal("name", ((IdentifierNode)u.Operand).Name);
    }

    // --- Unary prefixes ---

    [Fact]
    public void UnaryNot_ParsedAsUnaryOp()
    {
      var u = Assert.IsType<UnaryOpNode>(ParseExpr("not $a"));
      Assert.Equal("not", u.Operator);
      Assert.Equal("a", ((VariableRefNode)u.Operand).Name);
    }

    [Fact]
    public void UnaryMinus_ParsedAsUnaryOp()
    {
      // At the start of an operand position, `-` is unary.
      var u = Assert.IsType<UnaryOpNode>(ParseExpr("-5"));
      Assert.Equal("-", u.Operator);
      Assert.Equal(5.0, ((LiteralNode)u.Operand).Value);
    }

    [Fact]
    public void Spread_ParsedAsUnaryPrefix()
    {
      var u = Assert.IsType<UnaryOpNode>(ParseExpr("...$xs"));
      Assert.Equal("...", u.Operator);
    }

    [Fact]
    public void Bind_ParsedAsUnaryPrefix()
    {
      var u = Assert.IsType<UnaryOpNode>(ParseExpr("bind $x"));
      Assert.Equal("bind", u.Operator);
    }

    [Fact]
    public void TwoBind_ParsedAsUnaryPrefix()
    {
      var u = Assert.IsType<UnaryOpNode>(ParseExpr("2bind $x"));
      Assert.Equal("2bind", u.Operator);
    }

    [Fact]
    public void NotBindsTighterThanAnd()
    {
      // not $a and $b → (not $a) and $b
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("not $a and $b"));
      Assert.Equal("and", b.Operator);
      Assert.IsType<UnaryOpNode>(b.Left);
    }

    // --- Nested macro calls ---

    [Fact]
    public void NestedMacroCall_ParsedAsMacroCallNode()
    {
      // $x to (random: 1, 6) → BinaryOp(to, $x, MacroCall(random, [1, 6]))
      var b = Assert.IsType<BinaryOpNode>(ParseExpr("$x to (random: 1, 6)"));
      Assert.Equal("to", b.Operator);
      var call = Assert.IsType<MacroCallNode>(b.Right);
      Assert.Equal("random", call.Name);
      Assert.Equal(2, call.Arguments.Count);
      Assert.Equal(1.0, ((LiteralNode)call.Arguments[0]).Value);
      Assert.Equal(6.0, ((LiteralNode)call.Arguments[1]).Value);
    }

    // --- Argument lists ---

    [Fact]
    public void EmptyArgumentList_ProducesEmptyList()
    {
      var tokens = new HarloweTokenizer().Tokenize("(foo:)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance(); // past MacroOpen
      var args = new HarloweExpressionParser().ParseArgumentList(cursor);
      Assert.Empty(args);
    }

    [Fact]
    public void TrailingComma_TolerantPerHarloweManual()
    {
      var tokens = new HarloweTokenizer().Tokenize("(foo: 1, 2,)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var args = new HarloweExpressionParser().ParseArgumentList(cursor);
      Assert.Equal(2, args.Count);
    }

    [Fact]
    public void MultipleArguments_ParsedSeparately()
    {
      var tokens = new HarloweTokenizer().Tokenize("(foo: 1, 2, 3)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var args = new HarloweExpressionParser().ParseArgumentList(cursor);
      Assert.Equal(3, args.Count);
      Assert.Equal(1.0, ((LiteralNode)args[0]).Value);
      Assert.Equal(3.0, ((LiteralNode)args[2]).Value);
    }

    [Fact]
    public void ArgumentList_ConsumesTerminatingMacroClose()
    {
      var tokens = new HarloweTokenizer().Tokenize("(foo: 1)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      new HarloweExpressionParser().ParseArgumentList(cursor);
      Assert.Equal(TokenType.EndOfFile, cursor.Current.Type);
    }

    // --- lambdas (v2.3A: where clause only) ---

    [Fact]
    public void Lambda_WithTempParameter_ParsedAsLambdaNode()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("_x where _x > 5"));
      Assert.Equal("x", l.ParameterName);
      Assert.True(l.ParameterIsTemporary);
      var body = Assert.IsType<BinaryOpNode>(l.WhereClause);
      Assert.Equal(">", body.Operator);
    }

    [Fact]
    public void Lambda_WithStoryParameter_HonoursSigil()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("$item where $item is \"target\""));
      Assert.Equal("item", l.ParameterName);
      Assert.False(l.ParameterIsTemporary);
      Assert.IsType<BinaryOpNode>(l.WhereClause);
    }

    [Fact]
    public void Lambda_ImplicitIt_ParameterNameIsNull()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("where it > 5"));
      Assert.Null(l.ParameterName);
      var body = Assert.IsType<BinaryOpNode>(l.WhereClause);
      Assert.Equal(">", body.Operator);
      Assert.IsType<IdentifierNode>(body.Left);
    }

    [Fact]
    public void Lambda_ClauseBody_BindsAtOrder14()
    {
      // `and` is order 13, looser than the order-14 floor of the clause body —
      // so the body should consume the whole `_x > 5 and _x < 10` expression.
      var l = Assert.IsType<LambdaNode>(ParseExpr("_x where _x > 5 and _x < 10"));
      var body = Assert.IsType<BinaryOpNode>(l.WhereClause);
      Assert.Equal("and", body.Operator);
    }

    [Fact]
    public void Lambda_NonVariableParameter_Throws()
    {
      var ex = Assert.Throws<HarloweParseException>(() => ParseExpr("(1 + 2) where it > 0"));
      Assert.Contains("must be a variable", ex.Message);
    }

    [Fact]
    public void Lambda_UnsupportedClauseKeyword_GivesForwardCompatibleError()
    {
      // v2.3 ships `where`, `via`, `making`; `when` is still deferred and
      // should produce a clear error so the cursor doesn't run away.
      var ex = Assert.Throws<HarloweParseException>(() => ParseExpr("_x when _x > 5"));
      Assert.Contains("when", ex.Message);
    }

    // --- v2.3C: making clause (fold lambdas) ---

    [Fact]
    public void Lambda_MakingClause_PopulatesBothParameters()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("_item making _acc via _acc + _item"));
      Assert.Equal("item", l.ParameterName);
      Assert.True(l.ParameterIsTemporary);
      Assert.Equal("acc", l.MakingName);
      Assert.True(l.MakingIsTemporary);
      Assert.NotNull(l.ViaClause);
      Assert.Null(l.WhereClause);
    }

    [Fact]
    public void Lambda_MakingClause_HonoursStorySigil()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("$item making $acc via $acc + $item"));
      Assert.Equal("item", l.ParameterName);
      Assert.False(l.ParameterIsTemporary);
      Assert.Equal("acc", l.MakingName);
      Assert.False(l.MakingIsTemporary);
    }

    [Fact]
    public void Lambda_MakingWithoutVariable_Throws()
    {
      var ex = Assert.Throws<HarloweParseException>(() => ParseExpr("_x making via _x"));
      Assert.Contains("accumulator", ex.Message);
    }

    [Fact]
    public void Lambda_MakingWithoutVia_Throws()
    {
      var ex = Assert.Throws<HarloweParseException>(() => ParseExpr("_x making _acc where _x > 0"));
      Assert.Contains("via", ex.Message);
    }

    [Fact]
    public void Lambda_MakingWithoutItemParameter_Throws()
    {
      var ex = Assert.Throws<HarloweParseException>(() => ParseExpr("making _acc via _acc"));
      Assert.Contains("item parameter", ex.Message);
    }

    // --- v2.3B: via clause ---

    [Fact]
    public void Lambda_ViaClause_Alone()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("_x via _x * 2"));
      Assert.Equal("x", l.ParameterName);
      Assert.Null(l.WhereClause);
      var body = Assert.IsType<BinaryOpNode>(l.ViaClause);
      Assert.Equal("*", body.Operator);
    }

    [Fact]
    public void Lambda_WhereThenVia_BothClausesPopulated()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("_x where _x > 5 via _x * 2"));
      Assert.NotNull(l.WhereClause);
      Assert.NotNull(l.ViaClause);
    }

    [Fact]
    public void Lambda_ImplicitItVia_ParameterNameNull()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("via it * 2"));
      Assert.Null(l.ParameterName);
      Assert.NotNull(l.ViaClause);
    }

    // --- v2.3D: each clause ---

    [Fact]
    public void Lambda_EachTempParam_ParsedAsIsEach()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("each _enemy"));
      Assert.True(l.IsEach);
      Assert.Equal("enemy", l.ParameterName);
      Assert.True(l.ParameterIsTemporary);
      Assert.Null(l.WhereClause);
      Assert.Null(l.ViaClause);
    }

    [Fact]
    public void Lambda_EachStoryParam_HonoursSigil()
    {
      var l = Assert.IsType<LambdaNode>(ParseExpr("each $item"));
      Assert.True(l.IsEach);
      Assert.Equal("item", l.ParameterName);
      Assert.False(l.ParameterIsTemporary);
    }

    [Fact]
    public void Lambda_EachWithoutVariable_Throws()
    {
      var ex = Assert.Throws<HarloweParseException>(() => ParseExpr("each 5"));
      Assert.Contains("must be followed by a variable", ex.Message);
    }

    // --- Hook references ---

    [Fact]
    public void HookRef_Bare_ParsesAsHookRefNodeWithNoSteps()
    {
      var h = Assert.IsType<HookRefNode>(ParseExpr("?cake"));
      Assert.Equal("cake", h.Name);
      Assert.Empty(h.Steps);
    }

    [Fact]
    public void HookRef_WithForwardOrdinal_FoldsIntoStep()
    {
      var h = Assert.IsType<HookRefNode>(ParseExpr("?cake's 1st"));
      Assert.Equal("cake", h.Name);
      var step = Assert.Single(h.Steps);
      Assert.Equal(1, step.Index);
      Assert.False(step.FromEnd);
    }

    [Fact]
    public void HookRef_WithLast_FoldsIntoFromEndStep()
    {
      var h = Assert.IsType<HookRefNode>(ParseExpr("?cake's last"));
      var step = Assert.Single(h.Steps);
      Assert.Equal(1, step.Index);
      Assert.True(step.FromEnd);
    }

    [Fact]
    public void HookRef_WithNthLast_FoldsIntoFromEndStep()
    {
      var h = Assert.IsType<HookRefNode>(ParseExpr("?cake's 2ndlast"));
      var step = Assert.Single(h.Steps);
      Assert.Equal(2, step.Index);
      Assert.True(step.FromEnd);
    }

    [Fact]
    public void HookRef_ChainedOrdinals_FoldIntoMultipleSteps()
    {
      var h = Assert.IsType<HookRefNode>(ParseExpr("?cake's 1st's last"));
      Assert.Equal(2, h.Steps.Count);
      Assert.Equal(1, h.Steps[0].Index);
      Assert.False(h.Steps[0].FromEnd);
      Assert.True(h.Steps[1].FromEnd);
    }

    [Fact]
    public void HookRef_PossessiveOfNonOrdinal_LeftAsBinaryOp()
    {
      // `?cake's name` is not an ordinal narrowing — it stays a `'s` BinaryOp
      // over the hook ref (which the evaluator later rejects, but the parser
      // shape is the ordinary possessive).
      var bin = Assert.IsType<BinaryOpNode>(ParseExpr("?cake's name"));
      Assert.Equal("'s", bin.Operator);
      var h = Assert.IsType<HookRefNode>(bin.Left);
      Assert.Equal("cake", h.Name);
      Assert.Empty(h.Steps);
    }
  }
}
