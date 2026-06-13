using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Runtime;

namespace Harlowe.Twee
{
  /// <summary>
  /// Walks a body or expression AST and emits Harlowe markup text. Used by
  /// <see cref="TweeWriter"/> to canonicalize dirty passages on save;
  /// also useful as a debugging aid for inspecting an AST in source form.
  ///
  /// <para><b>Round-trip property.</b> For any AST produced by
  /// <see cref="HarloweParser.Parsing.HarloweBodyParser"/> /
  /// <see cref="HarloweParser.Parsing.HarloweExpressionParser"/>, the output
  /// of this printer parses back to an equivalent AST. The output is
  /// canonical (no insignificant whitespace, no original formatting), not
  /// byte-identical to the source — that's what
  /// <see cref="HarlowePassage.RawBody"/> is for.</para>
  ///
  /// <para><b>Operator parenthesization.</b> Driven by the same precedence
  /// tables the parser uses (lower order = tighter binding; left-associative).
  /// Parens go around a child <see cref="BinaryOpNode"/> when its order is
  /// looser than the parent — strictly looser on the left, equal-or-looser on
  /// the right (because left-associativity already groups equal-precedence
  /// runs leftward). Around a <see cref="BinaryOpNode"/> operand of a
  /// <see cref="UnaryOpNode"/>, parens go on equal-or-looser order.</para>
  ///
  /// <para><b>String literals.</b> Always double-quoted; the tokenizer
  /// decodes the JS-spec escape set (<c>\n</c>/<c>\r</c>/<c>\t</c>/<c>\\</c>/
  /// <c>\"</c>/etc.) so the printer can re-encode any value round-trippably
  /// without picking a delimiter per string. <see cref="AppendStringLiteral"/>
  /// escapes <c>\</c>, <c>"</c>, and the control characters that can't sit
  /// raw inside a double-quoted string.</para>
  /// </summary>
  public class MarkupPrinter : IBodyVisitor, IExpressionVisitor
  {
    /// <summary>
    /// Binary operator → precedence order. Mirrors
    /// <c>HarloweExpressionParser.BinaryOps</c>; kept here as a private copy
    /// so the parser doesn't need to expose its internals. Lower order =
    /// tighter binding.
    /// </summary>
    private static readonly Dictionary<string, int> BinaryOps = new Dictionary<string, int>
    {
      { "to", 16 }, { "into", 16 },
      { "where", 15 }, { "when", 15 }, { "via", 15 }, { "making", 15 }, { "each", 15 },
      { "-type", 14 },
      { "and", 13 }, { "or", 13 },
      { "is", 12 }, { "is not", 12 },
      { "contains", 11 }, { "does not contain", 11 }, { "is in", 11 }, { "is not in", 11 },
      { "is a", 10 }, { "is not a", 10 }, { "matches", 10 }, { "does not match", 10 },
      { "<", 9 }, { "<=", 9 }, { ">=", 9 }, { ">", 9 },
      { "+", 8 }, { "-", 8 },
      { "*", 7 }, { "/", 7 }, { "%", 7 },
      { "of", 4 },
      { "'s", 3 },
    };

    private static readonly Dictionary<string, int> UnaryPrefixOps = new Dictionary<string, int>
    {
      { "...", 6 }, { "bind", 6 }, { "2bind", 6 },
      { "not", 5 }, { "+", 5 }, { "-", 5 },
      { "its", 3 },
    };

    private StringBuilder _sb;

    /// <summary>
    /// Print a full passage body. Walks every child of
    /// <paramref name="body"/> in order; a <c>null</c> input yields an empty
    /// string for symmetry with the renderer.
    /// </summary>
    public string Print(PassageBody body)
    {
      _sb = new StringBuilder();
      if (body?.Children != null)
        foreach (var child in body.Children) child.Accept(this);
      return _sb.ToString();
    }

    /// <summary>Print a single body node (e.g. a hook or macro in isolation).</summary>
    public string Print(IBodyNode node)
    {
      _sb = new StringBuilder();
      node?.Accept(this);
      return _sb.ToString();
    }

    /// <summary>Print a single expression node — useful for diagnostics and tests.</summary>
    public string Print(IExpressionNode node)
    {
      _sb = new StringBuilder();
      node?.Accept(this);
      return _sb.ToString();
    }

    // ----- IBodyVisitor -----

    public void Visit(TextNode node) => _sb.Append(node.Content);

    public void Visit(NewlineNode node) => _sb.Append('\n');

    public void Visit(VariableNode node)
    {
      _sb.Append(node.IsTemporary ? '_' : '$');
      _sb.Append(node.Name);
    }

    public void Visit(Ast.Body.HtmlNode node) => _sb.Append(node.RawHtml);

    /// <summary>
    /// Emits the captured <see cref="ParseErrorNode.OriginalSource"/> if the
    /// loader populated it — that lets a programmatically constructed
    /// parse-error passage round-trip even when its carrying
    /// <see cref="HarlowePassage.RawBody"/> is null. Otherwise emits nothing
    /// (per-node body-parser recovery has no source text to fall back on) so
    /// the writer doesn't smuggle a synthetic error message back into the
    /// document.
    /// </summary>
    public void Visit(ParseErrorNode node)
    {
      if (!string.IsNullOrEmpty(node.OriginalSource))
        _sb.Append(node.OriginalSource);
    }

    /// <summary>
    /// Emits the canonical link form. <c>[[name]]</c> when <c>Text == Target</c>
    /// (or the parser populated <c>Text</c> from <c>Target</c>); otherwise
    /// <c>[[text-&gt;target]]</c>. The <c>&lt;-</c> form is never emitted —
    /// the parser folds both arrow directions into the same AST shape so the
    /// canonical output picks one direction.
    /// </summary>
    public void Visit(LinkNode node)
    {
      _sb.Append("[[");
      if (node.Text == node.Target || string.IsNullOrEmpty(node.Text))
      {
        _sb.Append(node.Target ?? string.Empty);
      }
      else
      {
        _sb.Append(node.Text);
        _sb.Append("->");
        _sb.Append(node.Target);
      }
      _sb.Append("]]");
    }

    /// <summary>
    /// Emits a hook with its anchor in the canonical position:
    /// <c>|name&gt;[content]</c> for right-anchored, <c>[content]&lt;name|</c>
    /// for left-anchored, <c>[content]</c> for anonymous.
    /// </summary>
    public void Visit(HookNode node)
    {
      if (node.Anchor == HookAnchor.Right)
      {
        _sb.Append('|').Append(node.Name).Append('>');
      }
      _sb.Append('[');
      if (node.Children != null)
        foreach (var child in node.Children) child.Accept(this);
      _sb.Append(']');
      if (node.Anchor == HookAnchor.Left)
      {
        _sb.Append('<').Append(node.Name).Append('|');
      }
    }

    /// <summary>
    /// Emits a body-position macro: <c>(name: args)</c>, immediately followed
    /// by an attached hook (no whitespace) when present. The body parser's
    /// whitespace-skipping lookahead means the spacing here is purely
    /// canonical — the original may have had any amount of whitespace before
    /// the hook.
    /// </summary>
    public void Visit(MacroNode node)
    {
      EmitMacroCall(node.Name, node.Arguments);
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    /// <summary>
    /// Emits a changer-chain node: the expression in canonical form (which
    /// already produces <c>(m1)+(m2)</c> or <c>$var</c> via the existing
    /// expression visitors), immediately followed by the hook. Re-parses to
    /// the same shape — the body parser detects the chain pattern from the
    /// expression's leading token.
    /// </summary>
    public void Visit(ChangerChainNode node)
    {
      node.Expression.Accept((IExpressionVisitor)this);
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    // ----- IExpressionVisitor -----

    public void Visit(LiteralNode node)
    {
      switch (node.Kind)
      {
        case LiteralKind.Number:
          // Round-trip precision (shared with value display) so a dirty
          // passage's numeric literals reserialize without truncation; the
          // tokenizer accepts the exponent forms this can emit.
          _sb.Append(HarloweValue.FormatNumber((double)node.Value));
          break;
        case LiteralKind.String:
          AppendStringLiteral((string)node.Value);
          break;
        case LiteralKind.Bool:
          _sb.Append((bool)node.Value ? "true" : "false");
          break;
      }
    }

    public void Visit(IdentifierNode node) => _sb.Append(node.Name);

    public void Visit(VariableRefNode node)
    {
      _sb.Append(node.IsTemporary ? '_' : '$');
      _sb.Append(node.Name);
    }

    /// <summary>
    /// Emits <c>Left OP Right</c> with precedence-driven parens around the
    /// children when needed. <c>'s</c> is the only operator that is
    /// close-bound on the left (no leading space); every other operator —
    /// including the word-operators <c>of</c>, <c>is</c>, etc. — gets a
    /// single space on each side.
    /// </summary>
    public void Visit(BinaryOpNode node)
    {
      int ourOrder = BinaryOps[node.Operator];
      EmitBinaryChild(node.Left, node.Operator, ourOrder, isRight: false);
      if (node.Operator == "'s")
      {
        _sb.Append("'s ");
      }
      else
      {
        _sb.Append(' ').Append(node.Operator).Append(' ');
      }
      EmitBinaryChild(node.Right, node.Operator, ourOrder, isRight: true);
    }

    /// <summary>
    /// Emits <c>OP operand</c>. Sign and spread operators (<c>+</c>,
    /// <c>-</c>, <c>...</c>) are close-bound to the operand; word operators
    /// (<c>not</c>, <c>its</c>, <c>bind</c>, <c>2bind</c>) get a trailing
    /// space.
    /// </summary>
    public void Visit(UnaryOpNode node)
    {
      int ourOrder = UnaryPrefixOps[node.Operator];
      string op = node.Operator;
      if (op == "+" || op == "-" || op == "...")
        _sb.Append(op);
      else
        _sb.Append(op).Append(' ');
      EmitUnaryChild(node.Operand, ourOrder);
    }

    public void Visit(MacroCallNode node) => EmitMacroCall(node.Name, node.Arguments);

    /// <summary>
    /// Emits a hook reference: <c>?name</c>, with each ordinal step appended as
    /// a <c>'s</c> accessor (<c>?cake's 1st</c>, <c>?cake's last</c>,
    /// <c>?cake's 2ndlast</c>). The parser folds exactly these forms back into
    /// <see cref="HookRefNode.Steps"/>, so the output round-trips.
    /// </summary>
    public void Visit(HookRefNode node)
    {
      _sb.Append('?').Append(node.Name);
      if (node.Steps == null) return;
      for (int i = 0; i < node.Steps.Count; i++)
      {
        _sb.Append("'s ").Append(OrdinalText(node.Steps[i]));
      }
    }

    /// <summary>
    /// Emits canonical lambda shapes: <c>_x where _x &gt; 5</c>,
    /// <c>where it &gt; 5</c> (implicit-it), <c>_x via _x * 2</c>,
    /// <c>_running making _x via _running + _x</c>, and <c>each _x</c>. Lambda
    /// children are written at the loosest binary level — Harlowe doesn't have
    /// a lambda-vs-binary ambiguity inside a clause body.
    /// </summary>
    public void Visit(LambdaNode node)
    {
      if (node.IsEach)
      {
        _sb.Append("each ").Append(node.ParameterIsTemporary ? '_' : '$').Append(node.ParameterName);
        return;
      }

      if (node.ParameterName != null)
      {
        _sb.Append(node.ParameterIsTemporary ? '_' : '$').Append(node.ParameterName);
        if (node.MakingName != null)
        {
          _sb.Append(" making ").Append(node.MakingIsTemporary ? '_' : '$').Append(node.MakingName);
        }
        _sb.Append(' ');
      }

      if (node.WhereClause != null)
      {
        _sb.Append("where ");
        node.WhereClause.Accept(this);
      }
      if (node.ViaClause != null)
      {
        if (node.WhereClause != null) _sb.Append(' ');
        _sb.Append("via ");
        node.ViaClause.Accept(this);
      }
    }

    // ----- helpers -----

    /// <summary>
    /// Renders one hook-reference ordinal step in canonical Harlowe form:
    /// <c>last</c> for the back-anchored first, <c>Nthlast</c> for other
    /// back-anchored indices, <c>Nst</c>/<c>Nnd</c>/<c>Nrd</c>/<c>Nth</c>
    /// forwards. The suffix is decorative to the tokenizer but kept correct so
    /// the printed form reads naturally.
    /// </summary>
    private static string OrdinalText(Ast.Expression.HookRefStep step)
    {
      if (step.FromEnd && step.Index == 1) return "last";
      string suffix = OrdinalSuffix(step.Index);
      return step.Index + suffix + (step.FromEnd ? "last" : string.Empty);
    }

    private static string OrdinalSuffix(int n)
    {
      int mod100 = n % 100;
      if (mod100 >= 11 && mod100 <= 13) return "th";
      switch (n % 10)
      {
        case 1: return "st";
        case 2: return "nd";
        case 3: return "rd";
        default: return "th";
      }
    }

    private void EmitMacroCall(string name, List<IExpressionNode> args)
    {
      _sb.Append('(').Append(name).Append(':');
      EmitArgList(args);
      _sb.Append(')');
    }

    private void EmitArgList(List<IExpressionNode> args)
    {
      if (args == null || args.Count == 0) return;
      _sb.Append(' ');
      for (int i = 0; i < args.Count; i++)
      {
        if (i > 0) _sb.Append(", ");
        args[i].Accept(this);
      }
    }

    /// <summary>
    /// Wraps a binary child in parens if dropping them would change how the
    /// printed form re-parses. The paren rule depends on the PARENT's
    /// associativity:
    ///
    /// <list type="bullet">
    /// <item>Left-associative parent (the common case): left-side child can
    /// drop parens at equal precedence (left-grouping is implicit), right-
    /// side child needs parens at equal precedence so it doesn't get
    /// re-grouped into the left.</item>
    /// <item>Right-associative parent (currently <c>of</c>): right-side
    /// child can drop parens at equal precedence (right-grouping is
    /// implicit), left-side child needs parens at equal precedence to
    /// preserve its explicit grouping.</item>
    /// </list>
    ///
    /// In all cases, a strictly-looser child (greater order) always needs
    /// parens — the parent must visibly bind tighter.
    /// </summary>
    private void EmitBinaryChild(IExpressionNode child, string parentOp, int ourOrder, bool isRight)
    {
      // A `not` unary on the right of `is` would re-tokenize as the single
      // fused operator `is not` (the tokenizer fuses `is` + `not`), silently
      // inverting the expression. `3 is (not false)` must print with parens so
      // it does not become `3 is not false`. `not` is the only word-unary, and
      // `is` is the only binary operator that fuses with a following `not`.
      if (isRight && child is UnaryOpNode notUnary && notUnary.Operator == "not" && parentOp == "is")
      {
        _sb.Append('(');
        child.Accept(this);
        _sb.Append(')');
        return;
      }

      if (child is BinaryOpNode bin && BinaryOps.TryGetValue(bin.Operator, out int childOrder))
      {
        bool parentIsRightAssoc = HarloweExpressionParser.RightAssociativeOps.Contains(parentOp);
        // Equal-precedence side that does NOT match the parent's
        // associativity needs explicit parens.
        bool equalSideNeedsParens = parentIsRightAssoc ? !isRight : isRight;
        bool needsParens = childOrder > ourOrder
                           || (childOrder == ourOrder && equalSideNeedsParens);
        if (needsParens)
        {
          _sb.Append('(');
          child.Accept(this);
          _sb.Append(')');
          return;
        }
      }
      child.Accept(this);
    }

    /// <summary>
    /// Wraps a unary's operand in parens when the operand is a binary whose
    /// order is equal-or-looser than the unary's. The parser parses a unary's
    /// operand at <c>order - 1</c>, so equal-order would re-bind to the outer
    /// context without parens.
    /// </summary>
    private void EmitUnaryChild(IExpressionNode operand, int ourOrder)
    {
      if (operand is BinaryOpNode bin
          && BinaryOps.TryGetValue(bin.Operator, out int childOrder)
          && childOrder >= ourOrder)
      {
        _sb.Append('(');
        operand.Accept(this);
        _sb.Append(')');
        return;
      }
      operand.Accept(this);
    }

    /// <summary>
    /// Emit a string literal as a double-quoted, backslash-escaped Harlowe
    /// string. Round-trips through <see cref="HarloweTokenizer"/> because the
    /// tokenizer's escape decoder reverses every sequence we produce here.
    /// <c>\</c> and <c>"</c> get backslash escapes; <c>\n</c>/<c>\r</c>/
    /// <c>\t</c> become their named escapes so the emitted source stays on a
    /// single line; other control characters are <c>\xHH</c>-escaped so the
    /// output remains printable ASCII. Other characters (including <c>'</c>,
    /// which no longer needs special handling) pass through verbatim.
    /// </summary>
    private void AppendStringLiteral(string s)
    {
      _sb.Append('"');
      if (s != null)
      {
        for (int i = 0; i < s.Length; i++)
        {
          char c = s[i];
          switch (c)
          {
            case '\\': _sb.Append("\\\\"); break;
            case '"':  _sb.Append("\\\""); break;
            case '\n': _sb.Append("\\n"); break;
            case '\r': _sb.Append("\\r"); break;
            case '\t': _sb.Append("\\t"); break;
            default:
              if (c < 0x20 || c == 0x7f)
                _sb.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
              else
                _sb.Append(c);
              break;
          }
        }
      }
      _sb.Append('"');
    }
  }
}
