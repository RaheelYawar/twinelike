using System.Collections.Generic;
using Harlowe.Ast.Expression;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Walks an <see cref="IExpressionNode"/> tree and produces a single
  /// <see cref="HarloweValue"/>. Implements <see cref="IExpressionVisitor"/>
  /// using a "current value" slot (<see cref="_result"/>) because the visitor
  /// pattern returns void; the public entry point is <see cref="Evaluate"/>.
  ///
  /// <para>
  /// <b>Error propagation discipline.</b> Every operator handler short-circuits
  /// on a <see cref="HarloweValueKind.Error"/> operand and returns that error
  /// unchanged. This keeps a single bad sub-expression from compounding into
  /// cascading "real" type errors and matches Harlowe's authoring model: a
  /// broken expression renders an inline error at the spot it happened, the
  /// rest of the passage continues.
  /// </para>
  ///
  /// <para>v1 operator coverage:
  /// <list type="bullet">
  /// <item>Binary: <c>+ - * /</c>, <c>&lt; &lt;= &gt; &gt;=</c>, <c>is</c>, <c>is not</c>, <c>and</c>, <c>or</c>, <c>to</c>, <c>into</c>, <c>contains</c>, <c>is in</c>.</item>
  /// <item>Unary: <c>not</c>, unary <c>-</c>, unary <c>+</c>.</item>
  /// <item>Identifiers: <c>it</c>, <c>time</c>, <c>visit</c>, <c>visits</c>, <c>passage</c>.</item>
  /// </list>
  /// Lambda operators (<c>where/when/via/making/each</c>), <c>'s</c>/<c>of</c>/<c>its</c>,
  /// <c>...</c>/<c>bind</c>/<c>2bind</c>, and pattern operators (<c>matches</c>, <c>is a</c>, etc.)
  /// are deferred to v2.
  /// </para>
  /// </summary>
  public class ExpressionEvaluator : IExpressionVisitor
  {
    private readonly IVariableStore _store;
    private readonly IEvaluationContext _context;
    private readonly IMacroInvoker _macros;
    private HarloweValue _result;

    public ExpressionEvaluator(IVariableStore store, IEvaluationContext context, IMacroInvoker macros)
    {
      _store = store;
      _context = context;
      _macros = macros;
    }

    /// <summary>
    /// Evaluate <paramref name="node"/> and return the produced value. Always
    /// returns a non-null <see cref="HarloweValue"/>; failures come back as
    /// <see cref="HarloweValueKind.Error"/> rather than thrown exceptions.
    /// </summary>
    public HarloweValue Evaluate(IExpressionNode node)
    {
      if (node == null) return HarloweValue.OfError("missing expression");
      node.Accept(this);
      return _result ?? HarloweValue.OfError("expression produced no value");
    }

    public void Visit(LiteralNode node)
    {
      switch (node.Kind)
      {
        case LiteralKind.Number: _result = HarloweValue.OfNumber((double)node.Value); break;
        case LiteralKind.String: _result = HarloweValue.OfString((string)node.Value); break;
        case LiteralKind.Bool: _result = HarloweValue.OfBool((bool)node.Value); break;
        default: _result = HarloweValue.OfError("unknown literal kind"); break;
      }
    }

    public void Visit(IdentifierNode node)
    {
      switch (node.Name)
      {
        case "it":
          _result = _store.It ?? HarloweValue.OfError("'it' is not yet set");
          break;
        case "time":
          _result = _context != null ? _context.Time : HarloweValue.OfError("'time' is unavailable outside a story session");
          break;
        case "visit":
        case "visits":
          _result = _context != null ? _context.Visits : HarloweValue.OfError("'visits' is unavailable outside a story session");
          break;
        case "passage":
          _result = _context != null ? _context.Passage : HarloweValue.OfError("'passage' is unavailable outside a story session");
          break;
        default:
          _result = HarloweValue.OfError($"unknown identifier '{node.Name}'");
          break;
      }
    }

    public void Visit(VariableRefNode node)
    {
      var value = _store.Get(node.Name, node.IsTemporary);
      if (value != null) { _result = value; return; }
      string sigil = node.IsTemporary ? "_" : "$";
      _result = HarloweValue.OfError($"{sigil}{node.Name} is not set");
    }

    public void Visit(UnaryOpNode node)
    {
      // 'to'/'into' are not unary; only assignment forms are. Everything here
      // is a value-returning prefix.
      var operand = Evaluate(node.Operand);
      if (operand.IsError) { _result = operand; return; }

      switch (node.Operator)
      {
        case "not":
          if (operand.Kind != HarloweValueKind.Bool) { _result = TypeError("not", operand); return; }
          _result = HarloweValue.OfBool(!operand.AsBool);
          return;
        case "-":
          if (operand.Kind != HarloweValueKind.Number) { _result = TypeError("unary -", operand); return; }
          _result = HarloweValue.OfNumber(-operand.AsNumber);
          return;
        case "+":
          if (operand.Kind != HarloweValueKind.Number) { _result = TypeError("unary +", operand); return; }
          _result = operand;
          return;
        default:
          _result = HarloweValue.OfError($"unsupported unary operator '{node.Operator}'");
          return;
      }
    }

    public void Visit(BinaryOpNode node)
    {
      // 'to' and 'into' are assignment forms — the target side must be a
      // variable reference, not an evaluated value. Handle them before either
      // side is evaluated.
      if (node.Operator == "to") { AssignTo(node.Left, node.Right); return; }
      if (node.Operator == "into") { AssignTo(node.Right, node.Left); return; }

      var left = Evaluate(node.Left);
      if (left.IsError) { _result = left; return; }
      var right = Evaluate(node.Right);
      if (right.IsError) { _result = right; return; }

      switch (node.Operator)
      {
        case "+": _result = OpAdd(left, right); return;
        case "-": _result = OpNumeric("-", left, right, (a, b) => a - b); return;
        case "*": _result = OpNumeric("*", left, right, (a, b) => a * b); return;
        case "/":
          if (left.Kind == HarloweValueKind.Number && right.Kind == HarloweValueKind.Number && right.AsNumber == 0.0)
          { _result = HarloweValue.OfError("division by zero"); return; }
          _result = OpNumeric("/", left, right, (a, b) => a / b); return;

        case "<": _result = OpCompare("<", left, right, (a, b) => a < b); return;
        case "<=": _result = OpCompare("<=", left, right, (a, b) => a <= b); return;
        case ">": _result = OpCompare(">", left, right, (a, b) => a > b); return;
        case ">=": _result = OpCompare(">=", left, right, (a, b) => a >= b); return;

        case "is": _result = HarloweValue.OfBool(left.Equals(right)); return;
        case "is not": _result = HarloweValue.OfBool(!left.Equals(right)); return;

        case "and":
          if (left.Kind != HarloweValueKind.Bool) { _result = TypeError("and", left); return; }
          if (right.Kind != HarloweValueKind.Bool) { _result = TypeError("and", right); return; }
          _result = HarloweValue.OfBool(left.AsBool && right.AsBool); return;
        case "or":
          if (left.Kind != HarloweValueKind.Bool) { _result = TypeError("or", left); return; }
          if (right.Kind != HarloweValueKind.Bool) { _result = TypeError("or", right); return; }
          _result = HarloweValue.OfBool(left.AsBool || right.AsBool); return;

        case "contains": _result = OpContains(left, right); return;
        case "is in": _result = OpContains(right, left); return;

        default:
          _result = HarloweValue.OfError($"unsupported binary operator '{node.Operator}'"); return;
      }
    }

    private void AssignTo(IExpressionNode targetNode, IExpressionNode valueNode)
    {
      if (!(targetNode is VariableRefNode target))
      {
        _result = HarloweValue.OfError("assignment target must be a variable");
        return;
      }
      var value = Evaluate(valueNode);
      if (value.IsError) { _result = value; return; }
      _store.Set(target.Name, target.IsTemporary, value);
      _result = value;
    }

    public void Visit(MacroCallNode node)
    {
      if (_macros == null)
      {
        _result = HarloweValue.OfError($"macro '{node.Name}' cannot be called: no macro registry configured");
        return;
      }
      var args = new List<HarloweValue>(node.Arguments.Count);
      for (int i = 0; i < node.Arguments.Count; i++)
      {
        var v = Evaluate(node.Arguments[i]);
        if (v.IsError) { _result = v; return; }
        args.Add(v);
      }
      _result = _macros.Invoke(node.Name, args) ?? HarloweValue.OfError($"macro '{node.Name}' returned no value");
    }

    public void Visit(ArrayNode node) => _result = HarloweValue.OfError("array literal not supported in v1");
    public void Visit(DatamapNode node) => _result = HarloweValue.OfError("datamap literal not supported in v1");
    public void Visit(DatasetNode node) => _result = HarloweValue.OfError("dataset literal not supported in v1");

    private static HarloweValue OpAdd(HarloweValue left, HarloweValue right)
    {
      if (left.Kind == HarloweValueKind.Number && right.Kind == HarloweValueKind.Number)
        return HarloweValue.OfNumber(left.AsNumber + right.AsNumber);
      if (left.Kind == HarloweValueKind.String && right.Kind == HarloweValueKind.String)
        return HarloweValue.OfString(left.AsString + right.AsString);
      return HarloweValue.OfError($"+ does not apply to {left.Kind} and {right.Kind}");
    }

    private static HarloweValue OpNumeric(string op, HarloweValue left, HarloweValue right, System.Func<double, double, double> fn)
    {
      if (left.Kind != HarloweValueKind.Number) return TypeError(op, left);
      if (right.Kind != HarloweValueKind.Number) return TypeError(op, right);
      return HarloweValue.OfNumber(fn(left.AsNumber, right.AsNumber));
    }

    private static HarloweValue OpCompare(string op, HarloweValue left, HarloweValue right, System.Func<double, double, bool> fn)
    {
      if (left.Kind != HarloweValueKind.Number) return TypeError(op, left);
      if (right.Kind != HarloweValueKind.Number) return TypeError(op, right);
      return HarloweValue.OfBool(fn(left.AsNumber, right.AsNumber));
    }

    private static HarloweValue OpContains(HarloweValue container, HarloweValue item)
    {
      switch (container.Kind)
      {
        case HarloweValueKind.Array:
          var arr = container.AsArray;
          for (int i = 0; i < arr.Count; i++) if (arr[i].Equals(item)) return HarloweValue.OfBool(true);
          return HarloweValue.OfBool(false);
        case HarloweValueKind.String:
          if (item.Kind != HarloweValueKind.String) return HarloweValue.OfError($"a String cannot contain a {item.Kind}");
          return HarloweValue.OfBool(container.AsString.Contains(item.AsString));
        case HarloweValueKind.Datamap:
          if (item.Kind != HarloweValueKind.String) return HarloweValue.OfError($"a Datamap key must be a String, not a {item.Kind}");
          return HarloweValue.OfBool(container.AsDatamap.ContainsKey(item.AsString));
        default:
          return HarloweValue.OfError($"contains does not apply to {container.Kind}");
      }
    }

    private static HarloweValue TypeError(string op, HarloweValue offender)
      => HarloweValue.OfError($"{op} does not apply to a {offender.Kind}");
  }
}
