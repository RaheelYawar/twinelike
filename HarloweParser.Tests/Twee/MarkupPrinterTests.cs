using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Twee
{
  public class MarkupPrinterTests
  {
    private static string Print(IBodyNode n) => new MarkupPrinter().Print(n);
    private static string Print(IExpressionNode n) => new MarkupPrinter().Print(n);
    private static string Print(PassageBody b) => new MarkupPrinter().Print(b);

    // ----- Body nodes -----

    [Fact]
    public void Body_Text() => Assert.Equal("hello", Print(new TextNode { Content = "hello" }));

    [Fact]
    public void Body_Newline() => Assert.Equal("\n", Print(new NewlineNode()));

    [Fact]
    public void Body_StoryVariable()
      => Assert.Equal("$hp", Print(new VariableNode { Name = "hp", IsTemporary = false }));

    [Fact]
    public void Body_TempVariable()
      => Assert.Equal("_loop", Print(new VariableNode { Name = "loop", IsTemporary = true }));

    [Fact]
    public void Body_Html()
      => Assert.Equal("<b>x</b>", Print(new Ast.Body.HtmlNode { RawHtml = "<b>x</b>" }));

    [Fact]
    public void Link_TargetOnly()
      => Assert.Equal("[[Start]]", Print(new LinkNode { Text = "Start", Target = "Start" }));

    [Fact]
    public void Link_NullText_PrintsTargetOnly()
      => Assert.Equal("[[Start]]", Print(new LinkNode { Text = null, Target = "Start" }));

    [Fact]
    public void Link_TextDifferentFromTarget()
      => Assert.Equal("[[Click here->Next]]", Print(new LinkNode { Text = "Click here", Target = "Next" }));

    [Fact]
    public void Hook_Anonymous()
    {
      var h = new HookNode { Anchor = HookAnchor.None, Children = new List<IBodyNode> { new TextNode { Content = "hi" } } };
      Assert.Equal("[hi]", Print(h));
    }

    [Fact]
    public void Hook_LeftAnchored()
    {
      var h = new HookNode
      {
        Anchor = HookAnchor.Left,
        Name = "greeting",
        Children = new List<IBodyNode> { new TextNode { Content = "hi" } }
      };
      Assert.Equal("[hi]<greeting|", Print(h));
    }

    [Fact]
    public void Hook_RightAnchored()
    {
      var h = new HookNode
      {
        Anchor = HookAnchor.Right,
        Name = "greeting",
        Children = new List<IBodyNode> { new TextNode { Content = "hi" } }
      };
      Assert.Equal("|greeting>[hi]", Print(h));
    }

    [Fact]
    public void Hook_Empty()
      => Assert.Equal("[]", Print(new HookNode { Anchor = HookAnchor.None, Children = new List<IBodyNode>() }));

    [Fact]
    public void Macro_Bare()
    {
      var m = new MacroNode
      {
        Name = "set",
        Arguments = new List<IExpressionNode> {
          new BinaryOpNode {
            Operator = "to",
            Left = new VariableRefNode { Name = "hp", IsTemporary = false },
            Right = new LiteralNode { Kind = LiteralKind.Number, Value = 10.0 },
          }
        }
      };
      Assert.Equal("(set: $hp to 10)", Print(m));
    }

    [Fact]
    public void Macro_WithAttachedHook_NoSpaceBeforeBracket()
    {
      var m = new MacroNode
      {
        Name = "if",
        Arguments = new List<IExpressionNode> { new VariableRefNode { Name = "x", IsTemporary = false } },
        AttachedHook = new HookNode
        {
          Anchor = HookAnchor.None,
          Children = new List<IBodyNode> { new TextNode { Content = "yes" } }
        }
      };
      Assert.Equal("(if: $x)[yes]", Print(m));
    }

    [Fact]
    public void Macro_NoArgs()
    {
      var m = new MacroNode { Name = "history", Arguments = new List<IExpressionNode>() };
      Assert.Equal("(history:)", Print(m));
    }

    // ----- Expression literals -----

    [Fact]
    public void Literal_Number_Integer()
      => Assert.Equal("10", Print(new LiteralNode { Kind = LiteralKind.Number, Value = 10.0 }));

    [Fact]
    public void Literal_Number_Decimal()
      => Assert.Equal("3.14", Print(new LiteralNode { Kind = LiteralKind.Number, Value = 3.14 }));

    [Fact]
    public void Literal_Number_Negative()
      => Assert.Equal("-5", Print(new LiteralNode { Kind = LiteralKind.Number, Value = -5.0 }));

    [Fact]
    public void Literal_Bool_True()
      => Assert.Equal("true", Print(new LiteralNode { Kind = LiteralKind.Bool, Value = true }));

    [Fact]
    public void Literal_Bool_False()
      => Assert.Equal("false", Print(new LiteralNode { Kind = LiteralKind.Bool, Value = false }));

    [Fact]
    public void Literal_String_Plain()
      => Assert.Equal("\"hello\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "hello" }));

    [Fact]
    public void Literal_String_Empty()
      => Assert.Equal("\"\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "" }));

    [Fact]
    public void Literal_String_ContainsDoubleQuote_EscapesIt()
      => Assert.Equal("\"a\\\"b\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "a\"b" }));

    [Fact]
    public void Literal_String_ContainsSingleQuote_PassesThrough()
      // Single quotes don't need escaping inside a double-quoted string.
      => Assert.Equal("\"a'b\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "a'b" }));

    [Fact]
    public void Literal_String_ContainsBothQuotes_EscapesDoubleAndDoubleQuotes()
      => Assert.Equal("\"a\\\"b'c\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "a\"b'c" }));

    [Fact]
    public void Literal_String_Backslash_IsEscaped()
      => Assert.Equal("\"a\\\\b\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "a\\b" }));

    [Fact]
    public void Literal_String_NewlineTabReturn_UseNamedEscapes()
      => Assert.Equal("\"a\\nb\\tc\\rd\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "a\nb\tc\rd" }));

    [Fact]
    public void Literal_String_OtherControlChar_UsesHexEscape()
      // U+0007 (BEL) is a control char with no named escape — falls back to \xHH.
      // Built via (char)0x07 concatenation rather than a string-literal escape
      // so the embedded control char is obvious from the source instead of
      // hiding as an invisible byte.
      => Assert.Equal("\"a\\x07b\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "a" + (char)0x07 + "b" }));

    [Fact]
    public void Literal_String_NullChar_UsesHexEscape()
      // U+0000 has no named escape in the printer (the tokenizer's `\0` is
      // a read-side convenience, not a printer choice). Falls through to \xHH.
      => Assert.Equal("\"a\\x00b\"", Print(new LiteralNode { Kind = LiteralKind.String, Value = "a" + (char)0x00 + "b" }));

    // ----- Identifiers and variable refs -----

    [Fact]
    public void Identifier_Plain() => Assert.Equal("name", Print(new IdentifierNode { Name = "name" }));

    [Fact]
    public void Identifier_Ordinal() => Assert.Equal("2ndlast", Print(new IdentifierNode { Name = "2ndlast" }));

    [Fact]
    public void VariableRef_Story()
      => Assert.Equal("$hp", Print(new VariableRefNode { Name = "hp", IsTemporary = false }));

    [Fact]
    public void VariableRef_Temp()
      => Assert.Equal("_loop", Print(new VariableRefNode { Name = "loop", IsTemporary = true }));

    // ----- Binary ops -----

    [Fact]
    public void Binary_Arithmetic()
      => Assert.Equal("$hp + 5", Print(new BinaryOpNode
      {
        Operator = "+",
        Left = new VariableRefNode { Name = "hp", IsTemporary = false },
        Right = new LiteralNode { Kind = LiteralKind.Number, Value = 5.0 }
      }));

    [Fact]
    public void Binary_Possessive_NoSpacesAroundApostropheS()
      => Assert.Equal("$arr's name", Print(new BinaryOpNode
      {
        Operator = "'s",
        Left = new VariableRefNode { Name = "arr", IsTemporary = false },
        Right = new IdentifierNode { Name = "name" }
      }));

    [Fact]
    public void Binary_Of_WithSpaces()
      => Assert.Equal("name of $arr", Print(new BinaryOpNode
      {
        Operator = "of",
        Left = new IdentifierNode { Name = "name" },
        Right = new VariableRefNode { Name = "arr", IsTemporary = false }
      }));

    [Fact]
    public void Binary_MultiWord_Is_Not_In()
      => Assert.Equal("$x is not in $arr", Print(new BinaryOpNode
      {
        Operator = "is not in",
        Left = new VariableRefNode { Name = "x", IsTemporary = false },
        Right = new VariableRefNode { Name = "arr", IsTemporary = false }
      }));

    // ----- Precedence parenthesization -----

    [Fact]
    public void Precedence_LeftAssociative_NoParensOnLeft()
    {
      // ($a + $b) + $c — left child of `+` is itself `+`. Same order on the
      // left stays paren-free because the parser is left-associative.
      var ast = new BinaryOpNode
      {
        Operator = "+",
        Left = new BinaryOpNode
        {
          Operator = "+",
          Left = new VariableRefNode { Name = "a" },
          Right = new VariableRefNode { Name = "b" },
        },
        Right = new VariableRefNode { Name = "c" },
      };
      Assert.Equal("$a + $b + $c", Print(ast));
    }

    [Fact]
    public void Precedence_RightSide_SameOrder_ParensRequired()
    {
      // $a + ($b + $c) — same-order on the right needs parens to survive
      // round-trip; without them the parser would re-group as ($a + $b) + $c.
      var ast = new BinaryOpNode
      {
        Operator = "+",
        Left = new VariableRefNode { Name = "a" },
        Right = new BinaryOpNode
        {
          Operator = "+",
          Left = new VariableRefNode { Name = "b" },
          Right = new VariableRefNode { Name = "c" },
        },
      };
      Assert.Equal("$a + ($b + $c)", Print(ast));
    }

    [Fact]
    public void Precedence_RightSide_TighterOrder_NoParens()
    {
      // $a + $b * $c — `*` (order 7) is tighter than `+` (order 8), so the
      // tighter-binding child can sit on the right without parens.
      var ast = new BinaryOpNode
      {
        Operator = "+",
        Left = new VariableRefNode { Name = "a" },
        Right = new BinaryOpNode
        {
          Operator = "*",
          Left = new VariableRefNode { Name = "b" },
          Right = new VariableRefNode { Name = "c" },
        },
      };
      Assert.Equal("$a + $b * $c", Print(ast));
    }

    [Fact]
    public void Precedence_LeftSide_LooserOrder_ParensRequired()
    {
      // ($a + $b) * $c — `+` is looser than `*`; without parens the parser
      // would build $a + ($b * $c).
      var ast = new BinaryOpNode
      {
        Operator = "*",
        Left = new BinaryOpNode
        {
          Operator = "+",
          Left = new VariableRefNode { Name = "a" },
          Right = new VariableRefNode { Name = "b" },
        },
        Right = new VariableRefNode { Name = "c" },
      };
      Assert.Equal("($a + $b) * $c", Print(ast));
    }

    [Fact]
    public void Precedence_PossessiveChain_RightSide_ParensRequired()
    {
      // $arr's ($other's name) — chained property access on the right needs
      // parens because `'s` is left-associative same-order.
      var ast = new BinaryOpNode
      {
        Operator = "'s",
        Left = new VariableRefNode { Name = "arr" },
        Right = new BinaryOpNode
        {
          Operator = "'s",
          Left = new VariableRefNode { Name = "other" },
          Right = new IdentifierNode { Name = "name" },
        },
      };
      Assert.Equal("$arr's ($other's name)", Print(ast));
    }

    // ----- Unary ops -----

    [Fact]
    public void Unary_Not_Word_HasTrailingSpace()
      => Assert.Equal("not $x", Print(new UnaryOpNode
      {
        Operator = "not",
        Operand = new VariableRefNode { Name = "x" }
      }));

    [Fact]
    public void Unary_Minus_NoSpace()
      => Assert.Equal("-$x", Print(new UnaryOpNode
      {
        Operator = "-",
        Operand = new VariableRefNode { Name = "x" }
      }));

    [Fact]
    public void Unary_Spread_NoSpace()
      => Assert.Equal("...$arr", Print(new UnaryOpNode
      {
        Operator = "...",
        Operand = new VariableRefNode { Name = "arr" }
      }));

    [Fact]
    public void Unary_Its()
      => Assert.Equal("its name", Print(new UnaryOpNode
      {
        Operator = "its",
        Operand = new IdentifierNode { Name = "name" }
      }));

    [Fact]
    public void Unary_Bind()
      => Assert.Equal("bind $x", Print(new UnaryOpNode
      {
        Operator = "bind",
        Operand = new VariableRefNode { Name = "x" }
      }));

    [Fact]
    public void Unary_OperandLooser_Parens()
    {
      // not ($a is true) — `is` (12) is looser than `not` (5), so the operand
      // needs parens; without them the parser binds `not $a is true` as
      // (not $a) is true, a different shape.
      var ast = new UnaryOpNode
      {
        Operator = "not",
        Operand = new BinaryOpNode
        {
          Operator = "is",
          Left = new VariableRefNode { Name = "a" },
          Right = new LiteralNode { Kind = LiteralKind.Bool, Value = true }
        }
      };
      Assert.Equal("not ($a is true)", Print(ast));
    }

    [Fact]
    public void Unary_OperandTighter_NoParens()
    {
      // not $a + $b — wait, `+` (8) is also looser than `not` (5).
      // Tighter operand example: there isn't one with current ops, since
      // unaries are at the tightest end of the table. Use a non-binary operand
      // to confirm the no-parens path: `not it` — operand is IdentifierNode.
      var ast = new UnaryOpNode
      {
        Operator = "not",
        Operand = new IdentifierNode { Name = "it" }
      };
      Assert.Equal("not it", Print(ast));
    }

    // ----- Macro calls / collection literals -----

    [Fact]
    public void MacroCall_Empty()
      => Assert.Equal("(history:)", Print(new MacroCallNode
      {
        Name = "history",
        Arguments = new List<IExpressionNode>()
      }));

    [Fact]
    public void MacroCall_MultiArg()
      => Assert.Equal("(random: 1, 6)", Print(new MacroCallNode
      {
        Name = "random",
        Arguments = new List<IExpressionNode>
        {
          new LiteralNode { Kind = LiteralKind.Number, Value = 1.0 },
          new LiteralNode { Kind = LiteralKind.Number, Value = 6.0 },
        }
      }));

    [Fact]
    public void ArrayNode_Empty() => Assert.Equal("(a:)", Print(new ArrayNode { Items = new List<IExpressionNode>() }));

    [Fact]
    public void ArrayNode_WithItems()
      => Assert.Equal("(a: 1, 2)", Print(new ArrayNode
      {
        Items = new List<IExpressionNode>
        {
          new LiteralNode { Kind = LiteralKind.Number, Value = 1.0 },
          new LiteralNode { Kind = LiteralKind.Number, Value = 2.0 },
        }
      }));

    [Fact]
    public void DatamapNode_PairsJoined()
      => Assert.Equal("(dm: \"name\", \"Bob\")", Print(new DatamapNode
      {
        Keys = new List<IExpressionNode> { new LiteralNode { Kind = LiteralKind.String, Value = "name" } },
        Values = new List<IExpressionNode> { new LiteralNode { Kind = LiteralKind.String, Value = "Bob" } },
      }));

    [Fact]
    public void DatasetNode_Items()
      => Assert.Equal("(ds: 1, 2)", Print(new DatasetNode
      {
        Items = new List<IExpressionNode>
        {
          new LiteralNode { Kind = LiteralKind.Number, Value = 1.0 },
          new LiteralNode { Kind = LiteralKind.Number, Value = 2.0 },
        }
      }));

    // ----- Hook references -----

    [Fact]
    public void HookRef_Bare()
      => Assert.Equal("?cake", Print(new HookRefNode { Name = "cake" }));

    [Fact]
    public void HookRef_ForwardOrdinalStep()
      => Assert.Equal("?cake's 1st", Print(new HookRefNode
      {
        Name = "cake",
        Steps = new List<HookRefStep> { new HookRefStep { Index = 1, FromEnd = false } }
      }));

    [Fact]
    public void HookRef_LastStep()
      => Assert.Equal("?cake's last", Print(new HookRefNode
      {
        Name = "cake",
        Steps = new List<HookRefStep> { new HookRefStep { Index = 1, FromEnd = true } }
      }));

    [Fact]
    public void HookRef_NthLastStep()
      => Assert.Equal("?cake's 2ndlast", Print(new HookRefNode
      {
        Name = "cake",
        Steps = new List<HookRefStep> { new HookRefStep { Index = 2, FromEnd = true } }
      }));

    // ----- Empty / null inputs -----

    [Fact]
    public void NullExpression_PrintsEmpty() => Assert.Equal("", Print((IExpressionNode)null));

    [Fact]
    public void NullBody_PrintsEmpty() => Assert.Equal("", Print((PassageBody)null));

    [Fact]
    public void EmptyPassageBody_PrintsEmpty()
      => Assert.Equal("", Print(new PassageBody { Children = new List<IBodyNode>() }));
  }
}
