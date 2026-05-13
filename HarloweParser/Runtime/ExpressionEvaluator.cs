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
      // Dispatch on operator first so `its` can hold onto the operand's AST
      // shape — `its name` needs to see IdentifierNode("name") as a key, not
      // evaluate it into an "unknown identifier" error.
      switch (node.Operator)
      {
        case "its":
        {
          var it = _store.It;
          if (it == null) { _result = HarloweValue.OfError("'it' is not yet set"); return; }
          // Property access does not rebind `it`; `'s`/`of`/`its` are pure reads.
          _result = ResolveProperty(it, node.Operand);
          return;
        }
        case "not":
        {
          var operand = Evaluate(node.Operand);
          if (operand.IsError) { _result = operand; return; }
          if (operand.Kind != HarloweValueKind.Bool) { _result = TypeError("not", operand); return; }
          _result = HarloweValue.OfBool(!operand.AsBool);
          return;
        }
        case "-":
        {
          var operand = Evaluate(node.Operand);
          if (operand.IsError) { _result = operand; return; }
          if (operand.Kind != HarloweValueKind.Number) { _result = TypeError("unary -", operand); return; }
          _result = HarloweValue.OfNumber(-operand.AsNumber);
          return;
        }
        case "+":
        {
          var operand = Evaluate(node.Operand);
          if (operand.IsError) { _result = operand; return; }
          if (operand.Kind != HarloweValueKind.Number) { _result = TypeError("unary +", operand); return; }
          _result = operand;
          return;
        }
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

      // Property access keeps the accessor side as a node so an IdentifierNode
      // is treated as a key/property name rather than evaluated as an unknown
      // identifier. Dispatched before the universal evaluation below.
      if (node.Operator == "'s")
      {
        var container = Evaluate(node.Left);
        _result = ResolveProperty(container, node.Right);
        return;
      }
      if (node.Operator == "of")
      {
        var container = Evaluate(node.Right);
        _result = ResolveProperty(container, node.Left);
        return;
      }

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
        case "does not contain": _result = OpNegateContains(OpContains(left, right)); return;
        case "is not in": _result = OpNegateContains(OpContains(right, left)); return;

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
      // Pre-check the macro name before evaluating arguments, so a `to`/`into`
      // assignment inside an unknown macro's arg list can't leak its
      // side-effect before the unknown-macro error is reported. Matches
      // BodyRenderer.Visit(MacroNode). TODO: replace with full restriction of
      // `to`/`into` to (set:)/(put:) argument position.
      if (!_macros.Contains(node.Name))
      {
        _result = HarloweValue.OfError($"unknown macro '{node.Name}'");
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

    public void Visit(ArrayNode node) => _result = HarloweValue.OfError("array literal not supported");
    public void Visit(DatamapNode node) => _result = HarloweValue.OfError("datamap literal not supported");
    public void Visit(DatasetNode node) => _result = HarloweValue.OfError("dataset literal not supported");

    /// <summary>
    /// Lambdas are opaque at construction time — no scope work happens here.
    /// The runtime wrapper just pins the AST so <see cref="LambdaInvoker"/> can
    /// bind parameters and re-enter the evaluator when a consuming macro fires.
    /// </summary>
    public void Visit(LambdaNode node) => _result = HarloweValue.OfLambda(new LambdaValue { Node = node });

    private static HarloweValue OpAdd(HarloweValue left, HarloweValue right)
    {
      if (left.Kind == HarloweValueKind.Number && right.Kind == HarloweValueKind.Number)
        return HarloweValue.OfNumber(left.AsNumber + right.AsNumber);
      if (left.Kind == HarloweValueKind.String && right.Kind == HarloweValueKind.String)
        return HarloweValue.OfString(left.AsString + right.AsString);
      if (left.Kind == HarloweValueKind.Changer && right.Kind == HarloweValueKind.Changer)
        return HarloweValue.OfChanger(left.AsChanger.Compose(right.AsChanger));
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

    // Negation wrapper for `does not contain` / `is not in`: preserves the
    // error from OpContains (e.g. "a String cannot contain a Number") rather
    // than flipping it to true.
    private static HarloweValue OpNegateContains(HarloweValue v)
      => v.IsError ? v : HarloweValue.OfBool(!v.AsBool);

    private static HarloweValue TypeError(string op, HarloweValue offender)
      => HarloweValue.OfError($"{op} does not apply to a {offender.Kind}");

    /// <summary>
    /// Resolve a property/key/index off <paramref name="container"/>. The
    /// accessor's AST shape matters: an <see cref="IdentifierNode"/> is treated
    /// as a name (datamap key, or reserved property like <c>length</c> on
    /// arrays/strings); anything else is evaluated as a value and dispatched by
    /// container kind. Errors on the container short-circuit unchanged.
    /// </summary>
    private HarloweValue ResolveProperty(HarloweValue container, IExpressionNode accessor)
    {
      if (container.IsError) return container;

      if (accessor is IdentifierNode id)
        return ResolveIdentifierAccessor(container, id.Name);

      var key = Evaluate(accessor);
      if (key.IsError) return key;
      return ResolveValueAccessor(container, key);
    }

    private static HarloweValue ResolveIdentifierAccessor(HarloweValue container, string name)
    {
      switch (container.Kind)
      {
        case HarloweValueKind.Datamap:
          if (container.AsDatamap.TryGetValue(name, out var v)) return v;
          return HarloweValue.OfError($"datamap has no key '{name}'");
        case HarloweValueKind.Array:
          if (name == "length") return HarloweValue.OfNumber(container.AsArray.Count);
          if (TryParseOrdinal(name, out int aIdx, out bool aFromEnd))
          {
            int target = aFromEnd ? container.AsArray.Count - aIdx + 1 : aIdx;
            return IndexArray(container.AsArray, target);
          }
          return HarloweValue.OfError($"array has no property '{name}'");
        case HarloweValueKind.String:
          if (name == "length") return HarloweValue.OfNumber(container.AsString.Length);
          if (TryParseOrdinal(name, out int sIdx, out bool sFromEnd))
          {
            int target = sFromEnd ? container.AsString.Length - sIdx + 1 : sIdx;
            return IndexString(container.AsString, target);
          }
          return HarloweValue.OfError($"string has no property '{name}'");
        default:
          return HarloweValue.OfError($"a {container.Kind} has no properties");
      }
    }

    /// <summary>
    /// Parses an ordinal accessor name into a 1-based index and a direction
    /// flag. Recognised forms: <c>last</c> (idx=1, fromEnd=true), <c>Nth</c>
    /// where the suffix is <c>st</c>/<c>nd</c>/<c>rd</c>/<c>th</c> (forward
    /// indexing), and <c>Nthlast</c> (back-anchored). Returns false for any
    /// other identifier so the caller can report an unknown property. The
    /// suffix is decorative — <c>2st</c> and <c>1nd</c> still parse — matching
    /// Harlowe's permissive author-facing behaviour.
    /// </summary>
    private static bool TryParseOrdinal(string name, out int index, out bool fromEnd)
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

    private static HarloweValue ResolveValueAccessor(HarloweValue container, HarloweValue key)
    {
      switch (container.Kind)
      {
        case HarloweValueKind.Datamap:
          if (key.Kind != HarloweValueKind.String)
            return HarloweValue.OfError($"datamap key must be a String, not a {key.Kind}");
          if (container.AsDatamap.TryGetValue(key.AsString, out var v)) return v;
          return HarloweValue.OfError($"datamap has no key '{key.AsString}'");
        case HarloweValueKind.Array:
          if (key.Kind != HarloweValueKind.Number)
            return HarloweValue.OfError($"array index must be a Number, not a {key.Kind}");
          return IndexArray(container.AsArray, key.AsNumber);
        case HarloweValueKind.String:
          if (key.Kind != HarloweValueKind.Number)
            return HarloweValue.OfError($"string index must be a Number, not a {key.Kind}");
          return IndexString(container.AsString, key.AsNumber);
        default:
          return HarloweValue.OfError($"a {container.Kind} has no properties");
      }
    }

    // Harlowe arrays/strings are 1-based; non-integer or out-of-range indices error.
    private static HarloweValue IndexArray(List<HarloweValue> arr, double idx)
    {
      if (idx != System.Math.Floor(idx))
        return HarloweValue.OfError($"array index must be a whole number; got {idx}");
      int i = (int)idx;
      if (i < 1 || i > arr.Count)
        return HarloweValue.OfError($"array index {i} is out of range (1..{arr.Count})");
      return arr[i - 1];
    }

    private static HarloweValue IndexString(string s, double idx)
    {
      if (idx != System.Math.Floor(idx))
        return HarloweValue.OfError($"string index must be a whole number; got {idx}");
      int i = (int)idx;
      if (i < 1 || i > s.Length)
        return HarloweValue.OfError($"string index {i} is out of range (1..{s.Length})");
      return HarloweValue.OfString(s[i - 1].ToString());
    }
  }
}
