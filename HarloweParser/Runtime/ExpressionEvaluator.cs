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
    private readonly MacroRegistry _registry;
    private readonly IRng _rng;
    private readonly HarloweProfile _profile;
    private HarloweValue _result;

    public ExpressionEvaluator(IVariableStore store, IEvaluationContext context, IMacroInvoker macros, IRng rng = null, HarloweProfile profile = null)
    {
      _store = store;
      _context = context;
      _macros = macros;
      _registry = macros as MacroRegistry;
      _profile = profile ?? HarloweProfile.V3;
      // The `random` data name draws from here. Callers with a session pass
      // MacroContext.Rng so draws share the session stream (and so undo/redo/
      // load replay reproduces them via the Moment's recorded RNG state);
      // standalone evaluation gets a fresh time-seeded stream, like
      // MacroContext's own default.
      _rng = rng ?? new MulberryRng();
    }

    // 1-based random position, reference's `State.random() * length | 0` draw
    // (compilePropertyIndex in ts/internaltypes/varref.ts). Callers guarantee
    // count >= 1.
    private int RandomIndex(int count) => (int)(_rng.NextDouble() * count) + 1;

    // The empty-container `random` error, shared by the read, write and
    // (move:)-source paths. Reference interpolates `objectName(obj)` into this
    // sentence, and objectName renders a zero-length container as "an empty
    // array"/"an empty string" (see the `an empty array` early return in
    // ts/utils/operationutils.ts), so the finished message says "empty" twice.
    private static HarloweValue EmptyRandomError(string containerNoun)
      => HarloweValue.OfError($"I can't get a random value from an empty {containerNoun}, because it's empty.");

    /// <summary>
    /// Evaluate <paramref name="node"/> and return the produced value. Always
    /// returns a non-null <see cref="HarloweValue"/>; failures come back as
    /// <see cref="HarloweValueKind.Error"/> rather than thrown exceptions.
    /// </summary>
    public HarloweValue Evaluate(IExpressionNode node)
    {
      if (node == null) return HarloweValue.OfError("missing expression");
      if (_registry != null) _registry.EvaluationDepth++;
      node.Accept(this);
      if (_registry != null) _registry.EvaluationDepth--;
      return _result ?? HarloweValue.OfError("expression produced no value");
    }

    public void Visit(LiteralNode node)
    {
      switch (node.Kind)
      {
        case LiteralKind.Number: _result = HarloweValue.OfNumber((double)node.Value); break;
        case LiteralKind.String: _result = HarloweValue.OfString((string)node.Value); break;
        case LiteralKind.Bool: _result = HarloweValue.OfBool((bool)node.Value); break;
        case LiteralKind.Colour:
        {
          // Value is the raw lexeme; the tokenizer only emits valid forms, so
          // a null here is a parser-construction bug surfaced in-prose.
          var colour = ColourValue.FromLexeme((string)node.Value);
          _result = colour != null
            ? HarloweValue.OfColour(colour)
            : HarloweValue.OfError($"malformed colour literal '{node.Value}'");
          break;
        }
        case LiteralKind.Datatype:
        {
          // Value is the raw lexeme; FromLexeme canonicalises it, and the
          // tokenizer only emits names it accepts, so null is a construction bug.
          var datatype = DatatypeValue.FromLexeme((string)node.Value);
          _result = datatype != null
            ? HarloweValue.OfDatatype(datatype)
            : HarloweValue.OfError($"unknown datatype '{node.Value}'");
          break;
        }
        default: _result = HarloweValue.OfError("unknown literal kind"); break;
      }
    }

    public void Visit(IdentifierNode node)
    {
      switch (node.Name)
      {
        case "it":
          _result = ReadIt();
          break;
        case "pos":
          // The 1-indexed lambda iteration position. Bound by LambdaInvoker
          // (and by (for:) iteration in Changer) for the duration of each
          // iteration; null outside any lambda. Documented reference idiom:
          // `(altered: via it + (str: pos), "A","B","C")` → "A1","B2","C3".
          _result = _store.Pos ?? HarloweValue.OfError("'pos' is only available inside a lambda iteration");
          break;
        case "time":
          _result = _context != null ? _context.Time : HarloweValue.OfError("'time' is unavailable outside a story session");
          break;
        case "visit":
        case "visits":
          _result = _context != null ? _context.Visits : HarloweValue.OfError("'visits' is unavailable outside a story session");
          break;
        case "turn":
        case "turns":
          _result = _context != null ? _context.Turns : HarloweValue.OfError("'turns' is unavailable outside a story session");
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
      => _result = ReadVariable(node.Name, node.IsTemporary) ?? UnsetError(node);

    /// <summary>
    /// A variable's value as story code reads it: what's stored, or 0 for a
    /// story variable that was never set when the profile has that default.
    /// Null means there's no value to read. The read never creates the variable.
    /// </summary>
    internal HarloweValue ReadVariable(string name, bool isTemporary)
    {
      var value = _store.Get(name, isTemporary);
      if (value == null && !isTemporary && _profile.UnsetReadsAsZero) return HarloweValue.OfNumber(0);
      return value;
    }

    /// <summary>The value of <c>it</c>: what's bound, the profile's default when nothing is, or an error.</summary>
    private HarloweValue ReadIt()
    {
      if (_store.It != null) return _store.It;
      return _profile.UnsetReadsAsZero ? HarloweValue.OfNumber(0) : ItNotSetError();
    }

    private static HarloweValue ItNotSetError() => HarloweValue.OfError("'it' is not yet set");

    private static HarloweValue UnsetError(VariableRefNode v)
      => HarloweValue.OfError($"{(v.IsTemporary ? "_" : "$")}{v.Name} is not set");

    public void Visit(UnaryOpNode node)
    {
      // Dispatch on operator first so `its` can hold onto the operand's AST
      // shape — `its name` needs to see IdentifierNode("name") as a key, not
      // evaluate it into an "unknown identifier" error.
      switch (node.Operator)
      {
        case "its":
        {
          var it = ReadIt();
          if (it.IsError) { _result = it; return; }
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
        case "...":
          // A spread at the top of a macro argument is expanded by
          // EvaluateMacroArguments and never reaches here. Reference's Spreader
          // is likewise unstorable and meaningless anywhere else.
          _result = HarloweValue.OfError("a spread '...' value can only be given directly to a macro");
          return;
        default:
          _result = HarloweValue.OfError($"unsupported unary operator '{node.Operator}'");
          return;
      }
    }

    public void Visit(BinaryOpNode node)
    {
      // 'to' and 'into' are assignment forms — the target side must be a
      // variable reference or a property chain rooted in one, not an evaluated
      // value. Handle them before either side is evaluated. Only 'to' rebinds
      // `it` to the target before the value runs (reference runner.ts: the
      // `to` arm calls `setIt(run(before))` on the destination, the `into`
      // arm does not).
      if (node.Operator == "to") { AssignTo(node.Left, node.Right, bindItToTarget: true); return; }
      if (node.Operator == "into") { AssignTo(node.Right, node.Left, bindItToTarget: false); return; }

      // The variable side of `-type` is a name, not a value to read.
      if (node.Operator == "-type") { _result = EvaluateTypedVar(node); return; }

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
        case "-": _result = OpSubtract(left, right); return;
        case "*": _result = OpNumeric("*", left, right, (a, b) => a * b); return;
        case "/":
          if (left.Kind == HarloweValueKind.Number && right.Kind == HarloweValueKind.Number && right.AsNumber == 0.0)
          { _result = HarloweValue.OfError("division by zero"); return; }
          _result = OpNumeric("/", left, right, (a, b) => a / b); return;

        case "%":
          // Matches reference Harlowe (the "%" entry in
          // ts/twinescript/operations.ts): numbers only (no coercion), error
          // on divisor zero, otherwise delegate to the native modulo. C# `%`
          // semantics match JS `%` semantics: truncated, sign follows the
          // dividend.
          if (left.Kind == HarloweValueKind.Number && right.Kind == HarloweValueKind.Number && right.AsNumber == 0.0)
          { _result = HarloweValue.OfError("modulo by zero"); return; }
          _result = OpNumeric("%", left, right, (a, b) => a % b); return;

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
        case "does not contain": _result = OpNegate(OpContains(left, right)); return;
        case "is not in": _result = OpNegate(OpContains(right, left)); return;

        case "is a": _result = OpIsA(left, right); return;
        case "is not a": _result = OpNegate(OpIsA(left, right)); return;
        case "matches": _result = OpMatchesValue(left, right, negate: false); return;
        case "does not match": _result = OpMatchesValue(left, right, negate: true); return;

        default:
          _result = HarloweValue.OfError($"unsupported binary operator '{node.Operator}'"); return;
      }
    }

    /// <summary>
    /// Builds the value of <c>type-type variable</c>, reference's
    /// <c>TypedVar.create</c>. A leading <c>...</c> on the type makes it a spread
    /// variable. The variable side has to be a plain variable.
    /// </summary>
    private HarloweValue EvaluateTypedVar(BinaryOpNode node)
    {
      bool isRest = node.Left is UnaryOpNode spread && spread.Operator == "...";
      var datatype = Evaluate(isRest ? ((UnaryOpNode)node.Left).Operand : node.Left);
      if (datatype.IsError) return datatype;
      if (isRest && datatype.Kind != HarloweValueKind.Datatype)
        return HarloweValue.OfError("A spread typed variable needs a plain datatype, like ...num-type _a.");
      if (node.Right is VariableRefNode variable)
        return HarloweValue.OfTypedVar(new TypedVarValue(datatype, variable.Name, variable.IsTemporary, isRest));
      return HarloweValue.OfError(node.Right is BinaryOpNode chain && (chain.Operator == "'s" || chain.Operator == "of")
        ? "I can only restrict the datatypes of variables, not data names or anything else."
        : "The -type syntax must have a variable to its right.");
    }

    /// <summary>
    /// A <c>(set:)</c> or <c>(put:)</c> with a typed destination. The value is
    /// checked against the type, the restriction is recorded, and then the value
    /// is stored, reference's order in <c>AssignmentRequest.set</c>.
    /// </summary>
    private void AssignTyped(BinaryOpNode typedNode, IExpressionNode valueNode, bool bindItToTarget)
    {
      var typedValue = EvaluateTypedVar(typedNode);
      if (typedValue.IsError) { _result = typedValue; return; }
      var typed = typedValue.AsTypedVar;

      HarloweValue value;
      if (bindItToTarget)
      {
        var current = ReadVariable(typed.Name, typed.IsTemporary)
          ?? UnsetError(new VariableRefNode { Name = typed.Name, IsTemporary = typed.IsTemporary });
        using (_store.PushItBinding(current))
          value = Evaluate(valueNode);
      }
      else
      {
        value = Evaluate(valueNode);
      }
      if (value.IsError) { _result = value; return; }
      _result = StoreTyped(typed, value) ?? value;
    }

    /// <summary>
    /// Checks <paramref name="value"/> against a typed destination, records the
    /// restriction and stores the value. <c>any</c> restricts nothing. A
    /// <c>const</c> variable may take this one value even if it had another,
    /// because defining the type clears it, and only in the current scope, as
    /// 3.3.9's <c>defineType</c> does. Returns the error, or null.
    /// </summary>
    private HarloweValue StoreTyped(TypedVarValue typed, HarloweValue value)
    {
      if (!Matches(value, typed.Datatype))
        return HarloweValue.OfError($"I can't put {value.ObjectName} into {typed.VariableSource} because it doesn't match {typed.VariableSource}'s datatype, {typed.Datatype.ObjectName}.");

      var type = typed.Datatype;
      bool isAny = type.Kind == HarloweValueKind.Datatype && type.AsDatatype.Name == "any";
      bool isConst = type.Kind == HarloweValueKind.Datatype && type.AsDatatype.Name == "const";
      if (!isAny)
      {
        var existing = _store.GetTypeRestriction(typed.Name, typed.IsTemporary);
        if (existing != null && !existing.Equals(type))
        {
          string noun = typed.IsTemporary ? "temp variable" : "variable";
          return HarloweValue.OfError($"I can't redefine the type of the {noun} {typed.VariableSource} to {type.ObjectName}, as it is already {existing.ObjectName}.");
        }
        _store.DefineType(typed.Name, typed.IsTemporary, type);
      }
      if (isConst)
      {
        var storable = UnstorableError(value);
        if (storable != null) return storable;
        var named = NamedForStorage(value, typed.VariableSource);
        _store.SetInCurrentScope(typed.Name, typed.IsTemporary, named);
        _store.SetIt(named);
        return null;
      }
      return WriteVariable(typed.Name, typed.IsTemporary, value);
    }

    /// <summary>
    /// Stores a whole variable, applying reference's <c>canSet</c> first: nothing
    /// unstorable, and nothing its type restriction refuses. Returns the error,
    /// or null.
    /// </summary>
    private HarloweValue WriteVariable(string name, bool isTemporary, HarloweValue value)
    {
      var storable = UnstorableError(value);
      if (storable != null) return storable;

      var restriction = _store.GetTypeRestriction(name, isTemporary);
      if (restriction != null)
      {
        string variable = (isTemporary ? "_" : "$") + name;
        if (restriction.Kind == HarloweValueKind.Datatype && restriction.AsDatatype.Name == "const")
        {
          if (_store.Get(name, isTemporary) != null)
            return HarloweValue.OfError($"I can't alter {variable} because it's been restricted to a constant value.");
        }
        else if (!Matches(value, restriction))
        {
          return HarloweValue.OfError($"I can't set {variable} to {value.ObjectName} because it's been restricted to {restriction.ToSource()}-type data.");
        }
      }
      _store.Set(name, isTemporary, NamedForStorage(value, (isTemporary ? "_" : "$") + name));
      return null;
    }

    /// <summary>
    /// A custom macro takes the name of the variable it's stored in, for error
    /// messages. One that's already named is copied first, as reference's
    /// <c>VarRef.set()</c> does, so the variables hold separate macros.
    /// </summary>
    private static HarloweValue NamedForStorage(HarloweValue value, string variable)
    {
      if (value.Kind != HarloweValueKind.CustomMacro) return value;
      var macro = value.AsCustomMacro;
      if (macro.KnownName != null)
      {
        macro = macro.Copy();
        value = HarloweValue.OfCustomMacro(macro);
      }
      macro.KnownName = variable;
      return value;
    }

    /// <summary>A typed variable name can't be kept in a variable, even inside a collection. Returns reference's error, or null.</summary>
    private static HarloweValue UnstorableError(HarloweValue value)
    {
      var unstorable = FindUnstorable(value, 0);
      if (unstorable == null) return null;
      return HarloweValue.OfError(ReferenceEquals(unstorable, value)
        ? $"{value.ObjectName} can't be stored."
        : $"{value.ObjectName} can't be stored because it holds {unstorable.ObjectName}.");
    }

    private static HarloweValue FindUnstorable(HarloweValue value, int depth)
    {
      if (depth >= MaxMatchDepth) return null;
      switch (value.Kind)
      {
        case HarloweValueKind.TypedVar:
          return value;
        case HarloweValueKind.Array:
          var items = value.AsArray;
          for (int i = 0; i < items.Count; i++)
          {
            var found = FindUnstorable(items[i], depth + 1);
            if (found != null) return found;
          }
          return null;
        case HarloweValueKind.Datamap:
          foreach (var kv in value.AsDatamap)
          {
            var found = FindUnstorable(kv.Value, depth + 1);
            if (found != null) return found;
          }
          return null;
      }
      return null;
    }

    private void AssignTo(IExpressionNode targetNode, IExpressionNode valueNode, bool bindItToTarget)
    {
      if (targetNode is BinaryOpNode typedTarget && typedTarget.Operator == "-type")
      {
        AssignTyped(typedTarget, valueNode, bindItToTarget);
        return;
      }
      if (!(targetNode is VariableRefNode target))
      {
        AssignToProperty(targetNode, valueNode, bindItToTarget);
        return;
      }
      HarloweValue value;
      if (bindItToTarget)
      {
        // Reference binds `it` to the assignment target's CURRENT value before
        // the right-hand side runs, so `(set: $x to it + 1)` reads $x rather
        // than the globally last-set variable. A target with no value binds the
        // read's error, which only surfaces if the RHS actually reads `it`. The
        // binding is restored on dispose, then Set rebinds `it` to the new value
        // as usual.
        var current = ReadVariable(target.Name, target.IsTemporary) ?? UnsetError(target);
        using (_store.PushItBinding(current))
          value = Evaluate(valueNode);
      }
      else
      {
        value = Evaluate(valueNode);
      }
      if (value.IsError) { _result = value; return; }
      _result = WriteVariable(target.Name, target.IsTemporary, value) ?? value;
    }

    /// <summary>
    /// Assignment into a property-chain target — <c>(set: $person's name to "Bob")</c>,
    /// <c>(put: 5 into $dm's key)</c>, <c>(set: 1st of $arr to 5)</c>. Mirrors
    /// reference Harlowe's <c>set()</c> in <c>ts/internaltypes/varref.ts</c>:
    /// the chain is resolved against current values first, then the write
    /// rebuilds it copy-on-write — every container level is shallow-cloned
    /// before mutation (the per-level clone-and-reassign walk in varref.ts's
    /// <c>#mutateRight</c>), and the rebuilt root goes through the normal
    /// <see cref="IVariableStore.Set"/> so delta capture for save/undo sees
    /// it. Untouched siblings keep their references, as in reference. The
    /// target path is resolved and validated BEFORE the value expression
    /// runs, so an error — anywhere — means zero mutation.
    /// </summary>
    private void AssignToProperty(IExpressionNode targetNode, IExpressionNode valueNode, bool bindItToTarget)
    {
      // `to` is (set:)'s operator and `into` is (put:)'s; name the macro the
      // author wrote in the not-a-variable error, as reference does.
      string macroName = bindItToTarget ? "(set:)" : "(put:)";
      var pathError = ResolveAssignmentTarget(targetNode, macroName, out var root, out var steps, out var oldValue);
      if (pathError != null) { _result = pathError; return; }

      HarloweValue value;
      if (bindItToTarget)
      {
        // As with bare variables, `it` is the destination's CURRENT value
        // while the right-hand side runs (reference's setIt on the `to` arm),
        // so `(set: $arr's 1st to it + 1)` increments the element. A new
        // datamap key / append slot binds an error that only surfaces if the
        // RHS actually reads it: unlike a variable, a missing slot has no
        // default under any profile.
        using (_store.PushItBinding(oldValue ?? ItNotSetError()))
          value = Evaluate(valueNode);
      }
      else
      {
        value = Evaluate(valueNode);
      }
      if (value.IsError) { _result = value; return; }
      FinishPropertyWrite(root, steps, value);
    }

    /// <summary>
    /// The front half of a property write, shared by RHS-evaluating
    /// assignment ((set:)/(put:)) and pre-evaluated assignment ((move:)'s
    /// destination): decompose the chain, read the root, resolve the write
    /// path. Returns the error to surface, or null with the chain captured.
    /// </summary>
    private HarloweValue ResolveAssignmentTarget(IExpressionNode targetNode, string macroName,
        out VariableRefNode root, out List<WriteStep> steps, out HarloweValue oldValue)
    {
      steps = null;
      oldValue = null;
      if (!TryDecomposeAssignmentTarget(targetNode, out root, out var accessors))
        return HarloweValue.OfError($"I can't {macroName} that value, if it isn't stored in a variable.");
      var rootValue = ReadVariable(root.Name, root.IsTemporary);
      if (rootValue == null) return UnsetError(root);
      steps = new List<WriteStep>(accessors.Count);
      return ResolveWritePath(rootValue, accessors, steps, out oldValue);
    }

    /// <summary>
    /// The back half of a property write: the value-dependent string-slot
    /// checks (one string position takes exactly one code point — the string
    /// branch of reference varref.ts's set()), the copy-on-write rebuild, the
    /// root store write, and the `it` correction.
    /// </summary>
    private void FinishPropertyWrite(VariableRefNode root, List<WriteStep> steps, HarloweValue value)
    {
      if (steps[steps.Count - 1].Kind == WriteKind.StringIndex)
      {
        if (value.Kind != HarloweValueKind.String)
        {
          _result = HarloweValue.OfError($"I can't put this non-string value, {value.ToSource()}, in a string.");
          return;
        }
        if (CodePointCount(value.AsString) != 1)
        {
          _result = HarloweValue.OfError($"{value.ToSource()} is not the right length to fit into this string location.");
          return;
        }
      }
      var writeError = WriteVariable(root.Name, root.IsTemporary, ApplyWrite(steps, value));
      if (writeError != null) { _result = writeError; return; }
      _store.SetIt(value);
      _result = value;
    }

    /// <summary>
    /// One level of a property-assignment write chain: the container value the
    /// level was resolved against plus the slot to rebuild — a datamap key, a
    /// 1-based array index (<see cref="IsAppend"/> for the Count+1 growth
    /// slot), or a 1-based string code-point index, from-end forms already
    /// converted. Captured by <see cref="ResolveWritePath"/>, consumed
    /// deepest-first by <see cref="ApplyWrite"/>.
    /// </summary>
    private struct WriteStep
    {
      public HarloweValue Container;
      public WriteKind Kind;
      public string Key;
      public int Index;
      public bool IsAppend;

      /// <summary>For a (move:) source written from the end (<c>last</c> is 1, <c>2ndlast</c> is 2), which reference recounts against the container at delete time. Zero otherwise.</summary>
      public int FromEndOrdinal;
    }

    private enum WriteKind { DatamapKey, ArrayIndex, StringIndex }

    /// <summary>
    /// Walks a property-access chain down its container side to a variable
    /// base: <c>'s</c> keeps its container on the left, <c>of</c> is the
    /// mirrored form with the container on the right. Accessors come back in
    /// application order (the root's accessor first); each stays a whole node
    /// — the accessor side of a <c>'s</c>/<c>of</c> is one computed accessor
    /// even when it is itself an expression, evaluated exactly once later.
    /// False when the base isn't a variable or the node isn't a chain at all.
    /// </summary>
    private static bool TryDecomposeAssignmentTarget(IExpressionNode node, out VariableRefNode root, out List<IExpressionNode> accessors)
    {
      root = null;
      accessors = null;
      var list = new List<IExpressionNode>();
      var cur = node;
      while (cur is BinaryOpNode bin && (bin.Operator == "'s" || bin.Operator == "of"))
      {
        if (bin.Operator == "'s") { list.Add(bin.Right); cur = bin.Left; }
        else { list.Add(bin.Left); cur = bin.Right; }
      }
      if (list.Count == 0 || !(cur is VariableRefNode v)) return false;
      list.Reverse();
      root = v;
      accessors = list;
      return true;
    }

    /// <summary>
    /// Resolves each accessor of an assignment target against current values,
    /// capturing one <see cref="WriteStep"/> per level and the final slot's
    /// current value in <paramref name="oldValue"/> (null for a new datamap
    /// key or an array append). Pure reads — nothing is mutated here, and a
    /// computed accessor is evaluated exactly once. Intermediate levels use
    /// the read path's rules and wordings (they are reads, as in reference);
    /// the final level applies the <c>canSet</c> rules from reference
    /// varref.ts. One deliberate divergence: an array write allows 1..Count
    /// (replace) plus Count+1 (append — reference's no-hole growth case) and
    /// errors beyond, where reference grows a sparse JS array whose holes
    /// error on read and break its own serialisation.
    /// Returns the error to surface, or null on success.
    /// </summary>
    private HarloweValue ResolveWritePath(HarloweValue rootValue, List<IExpressionNode> accessors, List<WriteStep> steps, out HarloweValue oldValue)
    {
      oldValue = null;
      var container = rootValue;
      for (int i = 0; i < accessors.Count; i++)
      {
        if (container.IsError) return container;
        bool isLast = i == accessors.Count - 1;
        var accessor = accessors[i];
        var step = new WriteStep { Container = container };
        HarloweValue child = null;

        switch (container.Kind)
        {
          case HarloweValueKind.Datamap:
          {
            var map = container.AsDatamap;
            string key;
            if (accessor is IdentifierNode id) key = id.Name;
            else
            {
              var keyValue = Evaluate(accessor);
              if (keyValue.IsError) return keyValue;
              if (keyValue.Kind != HarloweValueKind.String)
                return isLast
                  ? HarloweValue.OfError($"the datamap can only have string data names, not a {keyValue.Kind}.")
                  : HarloweValue.OfError($"datamap key must be a String, not a {keyValue.Kind}");
              key = keyValue.AsString;
            }
            bool exists = map.TryGetValue(key, out var existing);
            // A missing key errors mid-chain but is created by the final
            // write (reference: plain Map.set).
            if (!exists && !isLast) return HarloweValue.OfError($"datamap has no key '{key}'");
            step.Kind = WriteKind.DatamapKey;
            step.Key = key;
            if (exists) child = existing;
            break;
          }
          case HarloweValueKind.Array:
          {
            var arr = container.AsArray;
            var indexError = ResolveWriteIndex(accessor, isLast, "array", arr.Count, out int index);
            if (indexError != null) return indexError;
            step.Kind = WriteKind.ArrayIndex;
            step.Index = index;
            if (isLast && index == arr.Count + 1)
            {
              step.IsAppend = true;
            }
            else if (index >= 1 && index <= arr.Count)
            {
              child = arr[index - 1];
            }
            else
            {
              int upper = isLast ? arr.Count + 1 : arr.Count;
              return HarloweValue.OfError($"array index {index} is out of range (1..{upper})");
            }
            break;
          }
          case HarloweValueKind.String:
          {
            string s = container.AsString;
            int count = CodePointCount(s);
            var indexError = ResolveWriteIndex(accessor, isLast, "string", count, out int index);
            if (indexError != null) return indexError;
            if (index < 1 || index > count)
              return HarloweValue.OfError($"string index {index} is out of range (1..{count})");
            step.Kind = WriteKind.StringIndex;
            step.Index = index;
            child = IndexString(s, index);
            break;
          }
          case HarloweValueKind.Colour:
            // Colours are immutable; reference's canSet catch-all.
            return HarloweValue.OfError("I can't modify the colour");
          default:
          {
            string desc;
            if (accessor is IdentifierNode id) desc = $"'{id.Name}'";
            else
            {
              var keyValue = Evaluate(accessor);
              if (keyValue.IsError) return keyValue;
              desc = DescribeKey(keyValue);
            }
            return HarloweValue.OfError($"a {container.Kind} doesn't have data names, let alone {desc}");
          }
        }

        steps.Add(step);
        if (isLast) { oldValue = child; return null; }
        container = child;
      }
      return HarloweValue.OfError("missing assignment target accessor");
    }

    /// <summary>
    /// Resolves one array/string write accessor to a 1-based index (from-end
    /// forms converted against <paramref name="count"/>; bounds NOT checked —
    /// the caller applies its own replace/append rule). Returns the error to
    /// surface, or null. <c>length</c> is unassignable at any level of a
    /// write chain; <c>random</c> draws a fresh in-range position (reference
    /// compiles it to a concrete index before canSet ever sees it, which is
    /// why reference also allows assigning through it); a non-position name
    /// gets reference's canSet wording when it is the slot being assigned,
    /// the read path's unknown-property wording mid-chain.
    /// </summary>
    private HarloweValue ResolveWriteIndex(IExpressionNode accessor, bool isLast, string containerNoun, int count, out int index)
    {
      index = 0;
      if (accessor is IdentifierNode id)
      {
        if (id.Name == "length")
          return HarloweValue.OfError($"I can't forcibly alter the 'length' of the {containerNoun}.");
        if (id.Name == "random")
        {
          if (count == 0)
            return EmptyRandomError(containerNoun);
          index = RandomIndex(count);
          return null;
        }
        if (!Ordinals.TryParse(id.Name, out int ordIdx, out bool fromEnd))
          return isLast
            ? HarloweValue.OfError($"the {containerNoun} can only have position data names ('3rd', '1st', (5), etc.), not '{id.Name}'.")
            : HarloweValue.OfError($"{containerNoun} has no property '{id.Name}'");
        index = fromEnd ? count - ordIdx + 1 : ordIdx;
        return null;
      }
      var keyValue = Evaluate(accessor);
      if (keyValue.IsError) return keyValue;
      if (keyValue.Kind != HarloweValueKind.Number)
        return isLast
          ? HarloweValue.OfError($"the {containerNoun} can only have position data names ('3rd', '1st', (5), etc.), not {DescribeKey(keyValue)}.")
          : HarloweValue.OfError($"{containerNoun} index must be a Number, not a {keyValue.Kind}");
      double n = keyValue.AsNumber;
      if (n != System.Math.Floor(n))
        return HarloweValue.OfError($"{containerNoun} index must be a whole number; got {HarloweValue.FormatNumber(n)}");
      index = (int)n;
      return null;
    }

    // Describes a computed accessor key for a write-path error: string keys
    // read as names, everything else by kind.
    private static string DescribeKey(HarloweValue key)
      => key.Kind == HarloweValueKind.String ? $"'{key.AsString}'" : $"a {key.Kind}";

    /// <summary>
    /// Rebuilds the resolved chain around <paramref name="value"/>, deepest
    /// level first: each container is shallow-cloned, the new child installed,
    /// and the clone becomes the next level up's child — the copy-on-write
    /// walk of reference varref.ts's <c>#mutateRight</c>. Returns the rebuilt
    /// root value for the store write. Untouched siblings keep their
    /// references, which is what keeps the clones cheap.
    /// </summary>
    private static HarloweValue ApplyWrite(List<WriteStep> steps, HarloweValue value)
    {
      var child = value;
      for (int i = steps.Count - 1; i >= 0; i--)
      {
        var step = steps[i];
        switch (step.Kind)
        {
          case WriteKind.DatamapKey:
          {
            var map = new Dictionary<string, HarloweValue>(step.Container.AsDatamap);
            map[step.Key] = child;
            child = HarloweValue.OfDatamap(map);
            break;
          }
          case WriteKind.ArrayIndex:
          {
            var arr = new List<HarloweValue>(step.Container.AsArray);
            if (step.IsAppend) arr.Add(child);
            else arr[step.Index - 1] = child;
            child = HarloweValue.OfArray(arr);
            break;
          }
          default:
            // StringIndex: child is always a String here — a string's element
            // is a one-code-point string, and the deepest level's value was
            // gated by the caller's string-slot checks.
            child = HarloweValue.OfString(CodePoints.ReplaceAt(step.Container.AsString, step.Index, child.AsString));
            break;
        }
      }
      return child;
    }

    /// <summary>
    /// Reference's assignment-discipline pre-pass: the typeChecker halves of
    /// (set:)/(put:)/(move:)/(unpack:) in <c>ts/macrolib/commands.ts</c>
    /// check every argument BEFORE any argument runs, so
    /// <c>(set: $a to 1, 5 into $b)</c> assigns nothing at all. Two checks
    /// per argument: the assignment operator (a non-assignment argument is
    /// rejected too, reference's AssignmentRequest type signature), and the
    /// destination's shape — reference's XOR gate: (set:)/(put:) reject a
    /// pattern-shaped destination, (unpack:) requires one. Returns the error,
    /// or null when the macro isn't one of the four or every argument passes.
    /// </summary>
    public HarloweValue ValidateAssignmentOperators(string macroName, List<IExpressionNode> args)
    {
      if (args == null || args.Count == 0) return null;
      string name = MacroNames.Normalize(macroName);
      if (name != "set" && name != "put" && name != "move" && name != "unpack") return null;
      for (int i = 0; i < args.Count; i++)
      {
        var bin = args[i] as BinaryOpNode;
        string op = bin != null && (bin.Operator == "to" || bin.Operator == "into") ? bin.Operator : null;
        // `to` keeps its destination on the left, `into` on the right.
        var dest = op == "to" ? bin.Left : op == "into" ? bin.Right : null;
        switch (name)
        {
          case "set":
            if (op == "into") return HarloweValue.OfError("Please say 'to' when using the (set:) macro.");
            if (op == null) return HarloweValue.OfError("(set:) requires a 'to' assignment for each of its arguments.");
            if (IsPatternShapedDest(dest))
              return HarloweValue.OfError("Please use the (set:) macro with just single variables to the left of 'to'.");
            break;
          case "put":
            if (op == "to") return HarloweValue.OfError("Please say 'into' when using the (put:) or (unpack:) macro.");
            if (op == null) return HarloweValue.OfError("(put:) requires an 'into' assignment for each of its arguments.");
            if (IsPatternShapedDest(dest))
              return HarloweValue.OfError("Please use the (put:) macro with just single variables to the right of 'into'.");
            break;
          case "unpack":
            if (op == "to") return HarloweValue.OfError("Please say 'into' when using the (put:) or (unpack:) macro.");
            if (op == null) return HarloweValue.OfError("(unpack:) requires an 'into' assignment for each of its arguments.");
            if (!IsPatternShapedDest(dest))
              return HarloweValue.OfError("Please use the (unpack:) macro with arrays or datamaps containing variables to the right of 'into'.");
            break;
          default:
            if (op == "to") return HarloweValue.OfError("Please say 'into' when using the (move:) macro.");
            if (op == null) return HarloweValue.OfError("(move:) requires an 'into' assignment for each of its arguments.");
            break;
        }
      }
      return null;
    }

    /// <summary>
    /// A destructuring pattern in the AST: a literal <c>(a:)</c>/<c>(dm:)</c>
    /// call in destination position. Reference's shared set/put/unpack
    /// registration keys its XOR dest-shape gate off this — (set:)/(put:)
    /// reject it, (unpack:) requires it; every other destination shape keeps
    /// its own downstream error.
    /// </summary>
    private static bool IsPatternShapedDest(IExpressionNode node)
    {
      if (!(node is MacroCallNode call)) return false;
      string n = MacroNames.Normalize(call.Name);
      return n == "a" || n == "array" || n == "dm" || n == "datamap";
    }

    /// <summary>
    /// Evaluates one argument of a macro call. A (move:) <c>into</c> argument
    /// executes move semantics — copy the source, then delete a
    /// data-structure source slot — and an (unpack:) <c>into</c> argument
    /// destructures the source into its pattern, instead of the plain
    /// (put:)-style assignment the generic <c>into</c> operator performs;
    /// every other macro's arguments evaluate normally. Callers pre-scan the
    /// whole list with <see cref="ValidateAssignmentOperators"/> first.
    /// </summary>
    public HarloweValue EvaluateMacroArgument(string macroName, IExpressionNode arg)
    {
      if (arg is BinaryOpNode assign && assign.Operator == "into")
      {
        string name = MacroNames.Normalize(macroName);
        if (name == "move" || name == "unpack")
        {
          if (name == "move") MoveAssign(assign);
          else UnpackAssign(assign);
          return _result ?? HarloweValue.OfError("expression produced no value");
        }
      }
      return Evaluate(arg);
    }

    /// <summary>
    /// Evaluates a macro's whole argument list into <paramref name="into"/>,
    /// expanding each top-level <c>...</c> spread in place — reference's
    /// <c>spreadArguments</c> in <c>ts/macros.ts</c>, which runs before arity
    /// and type checks, so a spread can supply any number of arguments.
    /// Returns the first error, or null.
    /// </summary>
    public HarloweValue EvaluateMacroArguments(string macroName, List<IExpressionNode> arguments, List<HarloweValue> into)
    {
      for (int i = 0; i < arguments.Count; i++)
      {
        if (arguments[i] is UnaryOpNode spread && spread.Operator == "...")
        {
          var spreadError = SpreadInto(spread.Operand, into);
          if (spreadError != null) return spreadError;
          continue;
        }
        var value = EvaluateMacroArgument(macroName, arguments[i]);
        if (value.IsError) return value;
        into.Add(value);
      }
      return null;
    }

    /// <summary>
    /// Calls the custom macro held in the variable <paramref name="name"/> names
    /// (<c>$m</c> or <c>_m</c>), reference's <c>variableCall</c> branch in
    /// <c>ts/twinescript/runner.ts</c>: an unreadable variable is the error, and
    /// the arguments then evaluate and spread like any macro's. Reads the
    /// variable through the profile's unset rule, so an unset <c>$m</c> under
    /// Harlowe 3 is the number 0 and fails as "isn't a custom macro".
    /// </summary>
    internal HarloweValue CallCustomMacro(string name, List<IExpressionNode> arguments)
    {
      bool isTemporary = name[0] == '_';
      string variable = name.Substring(1);
      var target = ReadVariable(variable, isTemporary);
      if (target == null)
        return HarloweValue.OfError(isTemporary
          ? $"There isn't a temp variable named _{variable} in this place."
          : $"${variable} is not set");

      var args = new List<HarloweValue>(arguments.Count);
      var argumentError = EvaluateMacroArguments(null, arguments, args);
      if (argumentError != null) return argumentError;

      var registry = _macros as MacroRegistry;
      if (registry == null || registry.Context == null)
        return HarloweValue.OfError("custom macros can't run without a macro registry");
      return CustomMacroRunner.Call(target, args, registry, registry.Context);
    }

    /// <summary>
    /// Appends the items of a spread operand: an array's elements or a string's
    /// code points (JS spreads strings by code point). Datasets don't exist
    /// here yet, and spread datatypes (<c>...num</c>) are a separate feature.
    /// Returns the error, or null.
    /// </summary>
    private HarloweValue SpreadInto(IExpressionNode operand, List<HarloweValue> into)
    {
      var value = Evaluate(operand);
      if (value.IsError) return value;
      switch (value.Kind)
      {
        case HarloweValueKind.Array:
          into.AddRange(value.AsArray);
          return null;
        case HarloweValueKind.String:
          var characters = CodePoints.Split(value.AsString);
          for (int i = 0; i < characters.Count; i++) into.Add(HarloweValue.OfString(characters[i]));
          return null;
        case HarloweValueKind.Datatype:
          into.Add(HarloweValue.OfDatatype(value.AsDatatype.AsSpread()));
          return null;
        default:
          return HarloweValue.OfError($"I can't spread out a {value.Kind}, because it is not a string, dataset, or array.");
      }
    }

    /// <summary>
    /// Executes one (move:) argument — <c>source into dest</c> — per
    /// reference's <c>AssignmentRequest.set(true)</c> in
    /// <c>ts/datatypes/assignmentrequest.ts</c>: read the source, assign the
    /// destination, then delete the source. A data-structure slot is spliced
    /// out (array position, datamap key, string character) and a whole variable
    /// is removed, as real 3.3.9 and 4.0 do, whatever the (move:) article says.
    /// The deletion re-fetches the containers from the store after the
    /// assignment (reference's delete() re-walks its compiled chain), with keys
    /// and indices frozen at the initial resolve so computed accessors run
    /// exactly once. A destination error aborts before any deletion. A deletion
    /// that can't happen is skipped silently, because reference ignores what
    /// <c>src.delete()</c> returns, so moving <c>length</c> or a colour data
    /// name just copies it.
    /// </summary>
    private void MoveAssign(BinaryOpNode node)
    {
      VariableRefNode srcRoot;
      HarloweValue value;
      bool deletable = true;
      List<WriteStep> frozen;
      if (node.Left is VariableRefNode srcVar)
      {
        srcRoot = srcVar;
        value = ReadVariable(srcVar.Name, srcVar.IsTemporary);
        if (value == null) { _result = UnsetError(srcVar); return; }
        frozen = new List<WriteStep>();
      }
      else if (TryDecomposeAssignmentTarget(node.Left, out srcRoot, out var srcAccessors))
      {
        var srcRootValue = ReadVariable(srcRoot.Name, srcRoot.IsTemporary);
        if (srcRootValue == null) { _result = UnsetError(srcRoot); return; }
        frozen = new List<WriteStep>(srcAccessors.Count);
        var readError = ResolveSourcePath(srcRootValue, srcAccessors, frozen, out value, out deletable);
        if (readError != null) { _result = readError; return; }
      }
      else
      {
        _result = HarloweValue.OfError("I can't (move:) that value, if it isn't stored in a variable.");
        return;
      }

      if (IsPatternShapedDest(node.Right))
      {
        // A pattern deletes one level deeper, from inside the collection, so
        // the variable itself stays. An undeletable source reads as a scalar,
        // which fails the pattern match before anything could be deleted.
        ExecutePattern(node.Right, value, deletable ? srcRoot : null, deletable ? frozen : null);
        return;
      }

      AssignEvaluatedTo(node.Right, value);
      if (_result.IsError) return;
      if (deletable)
      {
        var deleteError = DeleteSource(srcRoot, frozen);
        if (deleteError != null) { _result = deleteError; return; }
      }
      _result = value;
    }

    /// <summary>
    /// The delete tail of a (move:). An empty <paramref name="frozen"/> path
    /// means the whole variable. Otherwise the root is re-fetched, the path is
    /// re-resolved against the post-assignment store, and the slot is spliced
    /// out via <see cref="ApplyDelete"/>, with the rebuilt root written to the
    /// current scope as reference's delete() does. If the assignment reshaped
    /// the source so the slot can't be reached, nothing is deleted. A path
    /// through a string is an error, because reference's writeback into the
    /// immutable string throws. Returns that error, or null.
    ///
    /// <para>A variable with a type restriction keeps its value. 3.3.9's
    /// <c>delete()</c> calls <c>canSet</c> with no value, which a restriction
    /// refuses at the variable level, and <c>(move:)</c> ignores the refusal.
    /// 4.0 made this delete, so it's recorded as a version difference.</para>
    /// </summary>
    private HarloweValue DeleteSource(VariableRefNode srcRoot, List<WriteStep> frozen)
    {
      bool restricted = _store.GetTypeRestriction(srcRoot.Name, srcRoot.IsTemporary) != null;
      if (frozen.Count == 0)
      {
        if (!restricted) _store.Delete(srcRoot.Name, srcRoot.IsTemporary);
        return null;
      }
      var freshRoot = _store.Get(srcRoot.Name, srcRoot.IsTemporary);
      if (freshRoot == null) return null;
      var steps = new List<WriteStep>(frozen.Count);
      if (!TryReresolveForDelete(freshRoot, frozen, steps, out bool throughString)) return null;
      if (throughString)
        return HarloweValue.OfError("I can't move a value out of a character of a string, because a string's characters can't be changed individually.");
      if (!restricted)
        _store.SetInCurrentScope(srcRoot.Name, srcRoot.IsTemporary, ApplyDelete(steps));
      return null;
    }

    /// <summary>
    /// Executes one (unpack:) argument — <c>source into pattern</c>. The
    /// source is a plain read (unpack never deletes); the pattern walk and
    /// reversed execution live in <see cref="Destructure"/> and
    /// <see cref="ExecutePattern"/>. The pre-scan guarantees the destination
    /// is pattern-shaped.
    /// </summary>
    private void UnpackAssign(BinaryOpNode node)
    {
      var source = Evaluate(node.Left);
      if (source.IsError) { _result = source; return; }
      ExecutePattern(node.Right, source, null, null);
    }

    /// <summary>
    /// One variable slot matched by a destructuring pattern: the destination
    /// AST (a variable or property chain), the source value bound to it, and
    /// — when the source lives in a variable — the frozen accessor path to
    /// its slot, for (move:)'s per-binding deletion.
    /// </summary>
    private struct PatternBinding
    {
      public IExpressionNode DestNode;
      public HarloweValue Value;
      public List<WriteStep> SrcSteps;

      /// <summary>For a spread position, one source path per gathered value, in ascending order.</summary>
      public List<List<WriteStep>> RestSteps;

      /// <summary>Set when the destination is a typed variable, which then also records its type.</summary>
      public TypedVarValue Typed;
    }

    /// <summary>
    /// Destructures <paramref name="source"/> against a pattern destination
    /// and executes the bindings in reverse — reference reverses so (move:)'s
    /// successive deletes can't shift array positions (it also means the
    /// first occurrence wins when one variable appears twice in a pattern).
    /// Each binding assigns via <see cref="AssignEvaluatedTo"/>, then — when
    /// the source lives in <paramref name="srcRoot"/> and the binding carries
    /// a slot path — deletes the source slot. A pattern that bound nothing
    /// (all positions were literals) is reference's "can't store" error.
    /// <c>it</c> ends as the whole source value (reference's runner binds
    /// <c>it</c> to the src of every <c>into</c> operation).
    /// </summary>
    private void ExecutePattern(IExpressionNode pattern, HarloweValue source, VariableRefNode srcRoot, List<WriteStep> srcSteps)
    {
      var bindings = new List<PatternBinding>();
      var err = Destructure(pattern, source, srcSteps, bindings);
      if (err != null) { _result = err; return; }
      if (bindings.Count == 0)
      {
        string noun = source.Kind == HarloweValueKind.Datamap ? "datamap" : "array";
        _result = HarloweValue.OfError($"I can't store a new value inside the {noun} that isn't in a variable.");
        return;
      }
      for (int i = bindings.Count - 1; i >= 0; i--)
      {
        var binding = bindings[i];
        if (binding.Typed != null)
        {
          var typedError = StoreTyped(binding.Typed, binding.Value);
          if (typedError != null) { _result = typedError; return; }
        }
        else
        {
          AssignEvaluatedTo(binding.DestNode, binding.Value);
          if (_result.IsError) return;
        }
        if (srcRoot == null) continue;
        if (binding.SrcSteps != null && binding.SrcSteps.Count > 0)
        {
          var deleteError = DeleteSource(srcRoot, binding.SrcSteps);
          if (deleteError != null) { _result = deleteError; return; }
        }
        // A spread position's slots go highest first, so one delete can't shift the next.
        for (int r = (binding.RestSteps?.Count ?? 0) - 1; r >= 0; r--)
        {
          var deleteError = DeleteSource(srcRoot, binding.RestSteps[r]);
          if (deleteError != null) { _result = deleteError; return; }
        }
      }
      _store.SetIt(source);
      _result = source;
    }

    /// <summary>
    /// The recursive pattern walk of reference's <c>destructure()</c> in
    /// <c>ts/datatypes/assignmentrequest.ts</c>, over AST shapes instead of
    /// VarRef-bearing values: <c>(a:)</c>/<c>(dm:)</c> calls recurse
    /// positionally / by key (datamap-pattern keys evaluate normally, so a
    /// <c>$var</c> key uses its value — saner than reference's degenerate
    /// VarRef-as-Map-key there), variables and property chains become
    /// bindings, and any other position evaluates and must <c>matches</c> the
    /// source value — so a bare datatype there is a check that binds nothing
    /// (<c>(a: num, $x)</c>), as in reference. A typed variable binds after its
    /// type is checked, and a spread one (<c>...num-type _x</c>) gathers a run of
    /// matching values. Still unimplemented: <c>(p:)</c> string patterns.
    /// Extra source values beyond the pattern are ignored, as in reference.
    /// <paramref name="srcSteps"/> is the frozen accessor path from the
    /// source root to <paramref name="source"/> — null when the source isn't
    /// in a variable — extended per level so (move:) knows each binding's
    /// delete path (reference's "VarRef of one level deeper"). Returns the
    /// error, or null with bindings appended in pattern order.
    /// </summary>
    private HarloweValue Destructure(IExpressionNode pattern, HarloweValue source,
        List<WriteStep> srcSteps, List<PatternBinding> bindings)
    {
      if (source.IsError) return source;

      if (pattern is BinaryOpNode typedPattern && typedPattern.Operator == "-type")
      {
        var typedValue = EvaluateTypedVar(typedPattern);
        if (typedValue.IsError) return typedValue;
        var typed = typedValue.AsTypedVar;
        if (!Matches(source, typed.Datatype)) return TypedMismatchError(typed, source);
        bindings.Add(new PatternBinding { DestNode = typedPattern.Right, Value = source, SrcSteps = srcSteps, Typed = typed });
        return null;
      }

      if (pattern is VariableRefNode
          || (pattern is BinaryOpNode chain && (chain.Operator == "'s" || chain.Operator == "of"))
          || (pattern is UnaryOpNode itsRef && itsRef.Operator == "its"))
      {
        bindings.Add(new PatternBinding { DestNode = pattern, Value = source, SrcSteps = srcSteps });
        return null;
      }

      if (pattern is UnaryOpNode spread && spread.Operator == "...")
        return HarloweValue.OfError("spread values in (unpack:) patterns need a datatype or a typed variable, like ...num or ...num-type _a");

      if (pattern is MacroCallNode call)
      {
        string name = MacroNames.Normalize(call.Name);
        if (name == "a" || name == "array")
        {
          if (source.Kind != HarloweValueKind.Array) return MismatchError(call, source);
          var arr = source.AsArray;
          var args = call.Arguments;
          int valueIndex = 0;
          for (int i = 0; i < args.Count; i++)
          {
            if (valueIndex >= arr.Count)
            {
              // Reference's template always pluralises; ours is singular for 1.
              int missing = args.Count - i;
              return HarloweValue.OfError(
                $"I can't unpack this array because it needs {missing} more value{(missing == 1 ? "" : "s")}.");
            }
            if (args[i] is BinaryOpNode restNode && restNode.Operator == "-type"
                && restNode.Left is UnaryOpNode restSpread && restSpread.Operator == "...")
            {
              var restError = DestructureRest(restNode, source, srcSteps, ref valueIndex, bindings);
              if (restError != null) return restError;
              continue;
            }
            // A bare ...num is a check that binds nothing, and unlike a typed
            // spread its run can be empty, as in 3.3.9's destructure.
            if (args[i] is UnaryOpNode bareSpread && bareSpread.Operator == "...")
            {
              var spreadType = Evaluate(bareSpread.Operand);
              if (spreadType.IsError) return spreadType;
              if (spreadType.Kind == HarloweValueKind.Datatype)
              {
                var runType = HarloweValue.OfDatatype(spreadType.AsDatatype.AsSpread());
                while (valueIndex < arr.Count && Matches(arr[valueIndex], runType)) valueIndex++;
                continue;
              }
            }
            var childSteps = ExtendSteps(srcSteps, new WriteStep { Container = source, Kind = WriteKind.ArrayIndex, Index = valueIndex + 1 });
            var err = Destructure(args[i], arr[valueIndex], childSteps, bindings);
            if (err != null) return err;
            valueIndex++;
          }
          return null;
        }
        if (name == "dm" || name == "datamap")
        {
          if (source.Kind != HarloweValueKind.Datamap) return MismatchError(call, source);
          var map = source.AsDatamap;
          var args = call.Arguments;
          if (args.Count % 2 != 0)
            return HarloweValue.OfError("(dm:) patterns require an even number of arguments (key, value pairs)");
          for (int i = 0; i < args.Count; i += 2)
          {
            var keyValue = Evaluate(args[i]);
            if (keyValue.IsError) return keyValue;
            if (keyValue.Kind != HarloweValueKind.String)
              return HarloweValue.OfError($"datamap key must be a String, not a {keyValue.Kind}");
            string key = keyValue.AsString;
            if (!map.TryGetValue(key, out var child))
              return HarloweValue.OfError($"I can't unpack this datamap because it needs a '{key}' data name.");
            var childSteps = ExtendSteps(srcSteps, new WriteStep { Container = source, Kind = WriteKind.DatamapKey, Key = key });
            var err = Destructure(args[i + 1], child, childSteps, bindings);
            if (err != null) return err;
          }
          return null;
        }
      }

      var expected = Evaluate(pattern);
      if (expected.IsError) return expected;
      bool tooDeep = false;
      bool matched = MatchesRecur(expected, source, 0, ref tooDeep);
      if (tooDeep) return HarloweValue.OfError(TooDeepMessage);
      if (!matched)
        return HarloweValue.OfError($"I tried to unpack, but {expected.ToSource()} in the pattern didn't match {source.ToSource()}.");
      return null;
    }

    /// <summary>
    /// A <c>...num-type _x</c> position: gathers the run of values from
    /// <paramref name="valueIndex"/> that match, as an array bound to the
    /// variable, whose type becomes an array of the spread type. An empty run
    /// doesn't match, which is 3.3.9's error for one.
    /// </summary>
    private HarloweValue DestructureRest(BinaryOpNode node, HarloweValue source, List<WriteStep> srcSteps,
        ref int valueIndex, List<PatternBinding> bindings)
    {
      var typedValue = EvaluateTypedVar(node);
      if (typedValue.IsError) return typedValue;
      var rest = typedValue.AsTypedVar;
      var arr = source.AsArray;

      int start = valueIndex;
      while (valueIndex < arr.Count && Matches(arr[valueIndex], rest.Datatype)) valueIndex++;

      var slice = arr.GetRange(start, valueIndex - start);
      var arrayType = HarloweValue.OfArray(new List<HarloweValue> { HarloweValue.OfDatatype(rest.Datatype.AsDatatype.AsSpread()) });
      var typed = new TypedVarValue(arrayType, rest.Name, rest.IsTemporary, false);
      var sliceValue = HarloweValue.OfArray(slice);
      if (slice.Count == 0) return TypedMismatchError(typed, sliceValue);

      List<List<WriteStep>> restSteps = null;
      if (srcSteps != null)
      {
        restSteps = new List<List<WriteStep>>(slice.Count);
        for (int i = start; i < valueIndex; i++)
          restSteps.Add(ExtendSteps(srcSteps, new WriteStep { Container = source, Kind = WriteKind.ArrayIndex, Index = i + 1 }));
      }
      bindings.Add(new PatternBinding { DestNode = node.Right, Value = sliceValue, RestSteps = restSteps, Typed = typed });
      return null;
    }

    private static HarloweValue TypedMismatchError(TypedVarValue typed, HarloweValue value)
      => HarloweValue.OfError($"I can't put {value.ObjectName} into {typed.VariableSource} because it doesn't match {typed.VariableSource}'s datatype, {typed.Datatype.ObjectName}.");

    private static HarloweValue MismatchError(MacroCallNode pattern, HarloweValue source)
      => HarloweValue.OfError($"I tried to unpack, but the ({MacroNames.Normalize(pattern.Name)}:) pattern didn't match {source.ToSource()}.");

    // Copy-extend a frozen source path; null stays null (the source isn't in
    // a variable, so bindings under it have nothing to delete).
    private static List<WriteStep> ExtendSteps(List<WriteStep> steps, WriteStep next)
    {
      if (steps == null) return null;
      var list = new List<WriteStep>(steps.Count + 1);
      list.AddRange(steps);
      list.Add(next);
      return list;
    }

    /// <summary>
    /// Assigns an already-evaluated value into a bare-variable or
    /// property-chain target — (move:)'s destination half, which has no RHS
    /// expression of its own to evaluate.
    /// </summary>
    private void AssignEvaluatedTo(IExpressionNode targetNode, HarloweValue value)
    {
      if (targetNode is BinaryOpNode typedTarget && typedTarget.Operator == "-type")
      {
        var typedValue = EvaluateTypedVar(typedTarget);
        _result = typedValue.IsError ? typedValue : StoreTyped(typedValue.AsTypedVar, value) ?? value;
        return;
      }
      if (targetNode is VariableRefNode target)
      {
        _result = WriteVariable(target.Name, target.IsTemporary, value) ?? value;
        return;
      }
      var pathError = ResolveAssignmentTarget(targetNode, "(move:)", out var root, out var steps, out _);
      if (pathError != null) { _result = pathError; return; }
      FinishPropertyWrite(root, steps, value);
    }

    /// <summary>
    /// Resolves a (move:) source chain with the READ path's semantics and
    /// wordings (reference's <c>src.get()</c>), capturing one frozen
    /// <see cref="WriteStep"/> per level for the deletion to replay. Computed
    /// accessors are evaluated exactly once, here; from-end ordinals convert
    /// against the pre-assignment counts. Two accessor families read fine but
    /// can never be deleted — a sequential's <c>length</c> and a colour's
    /// data names — those return successfully with
    /// <paramref name="deletable"/> false. Returns the read error, or null with
    /// <paramref name="value"/> holding the source slot's value.
    /// </summary>
    private HarloweValue ResolveSourcePath(HarloweValue rootValue, List<IExpressionNode> accessors,
        List<WriteStep> frozen, out HarloweValue value, out bool deletable)
    {
      value = null;
      deletable = true;
      var container = rootValue;
      for (int i = 0; i < accessors.Count; i++)
      {
        if (container.IsError) return container;
        bool isLast = i == accessors.Count - 1;
        var accessor = accessors[i];
        var step = new WriteStep { Container = container };
        HarloweValue child;

        switch (container.Kind)
        {
          case HarloweValueKind.Datamap:
          {
            var map = container.AsDatamap;
            string key;
            if (accessor is IdentifierNode id) key = id.Name;
            else
            {
              var keyValue = Evaluate(accessor);
              if (keyValue.IsError) return keyValue;
              if (keyValue.Kind != HarloweValueKind.String)
                return HarloweValue.OfError($"datamap key must be a String, not a {keyValue.Kind}");
              key = keyValue.AsString;
            }
            if (!map.TryGetValue(key, out child))
              return HarloweValue.OfError($"datamap has no key '{key}'");
            step.Kind = WriteKind.DatamapKey;
            step.Key = key;
            break;
          }
          case HarloweValueKind.Array:
          case HarloweValueKind.String:
          {
            bool isString = container.Kind == HarloweValueKind.String;
            string noun = isString ? "string" : "array";
            int count = isString ? CodePointCount(container.AsString) : container.AsArray.Count;
            int index;
            int fromEndOrdinal = 0;
            if (accessor is IdentifierNode id)
            {
              if (id.Name == "length")
              {
                // Reads fine; can never be deleted. That only matters when
                // this is the slot being moved — mid-chain, the next level
                // fails its own read on the Number.
                child = HarloweValue.OfNumber(count);
                if (isLast)
                {
                  deletable = false;
                  value = child;
                  return null;
                }
                container = child;
                continue;
              }
              if (id.Name == "random")
              {
                // Drawn once, here — the read and the later delete share the
                // frozen index, reference's compile-once model behind
                // `(move: $deck's random into $card)`.
                if (count == 0)
                  return EmptyRandomError(noun);
                index = RandomIndex(count);
              }
              else if (!Ordinals.TryParse(id.Name, out int ordIdx, out bool fromEnd))
                return HarloweValue.OfError($"{noun} has no property '{id.Name}'");
              else
              {
                index = fromEnd ? count - ordIdx + 1 : ordIdx;
                if (fromEnd) fromEndOrdinal = ordIdx;
              }
            }
            else
            {
              var keyValue = Evaluate(accessor);
              if (keyValue.IsError) return keyValue;
              if (keyValue.Kind != HarloweValueKind.Number)
                return HarloweValue.OfError($"{noun} index must be a Number, not a {keyValue.Kind}");
              double n = keyValue.AsNumber;
              if (n != System.Math.Floor(n))
                return HarloweValue.OfError($"{noun} index must be a whole number; got {HarloweValue.FormatNumber(n)}");
              index = (int)n;
            }
            if (index < 1 || index > count)
              return HarloweValue.OfError($"{noun} index {index} is out of range (1..{count})");
            step.Kind = isString ? WriteKind.StringIndex : WriteKind.ArrayIndex;
            step.Index = index;
            step.FromEndOrdinal = fromEndOrdinal;
            child = isString ? IndexString(container.AsString, index) : container.AsArray[index - 1];
            break;
          }
          case HarloweValueKind.Colour:
          {
            if (accessor is IdentifierNode id)
            {
              var prop = container.AsColour.GetProperty(id.Name);
              if (prop == null)
                return HarloweValue.OfError($"a colour has no property '{id.Name}'");
              // Readable, never deletable (reference's canSet catch-all).
              if (isLast)
              {
                deletable = false;
                value = prop;
                return null;
              }
              container = prop;
              continue;
            }
            return HarloweValue.OfError($"a {container.Kind} has no properties");
          }
          default:
            return HarloweValue.OfError($"a {container.Kind} has no properties");
        }

        frozen.Add(step);
        if (isLast) { value = child; return null; }
        container = child;
      }
      return HarloweValue.OfError("missing assignment source accessor");
    }

    /// <summary>
    /// Re-fetches the containers along a frozen (move:) source path against
    /// the post-assignment store state — reference's delete() re-walks its
    /// compiled chain, so an overlapping destination write is honoured — while
    /// keeping the initially-resolved keys, with from-end positions recounted
    /// as reference does. The deepest level follows reference's <c>canSet</c>:
    /// arrays and strings share position space, so either can lose the slot,
    /// and a slot that's no longer there is a no-op that still writes the
    /// container back. False when the path no longer reaches a container that
    /// can be deleted from; otherwise <paramref name="steps"/> is ready for
    /// <see cref="ApplyDelete"/>, unless <paramref name="throughString"/> says
    /// the writeback would have to land inside a string.
    /// </summary>
    private static bool TryReresolveForDelete(HarloweValue freshRoot, List<WriteStep> frozen, List<WriteStep> steps, out bool throughString)
    {
      throughString = false;
      var container = freshRoot;
      for (int i = 0; i < frozen.Count; i++)
      {
        bool deepest = i == frozen.Count - 1;
        var step = new WriteStep { Container = container, Kind = frozen[i].Kind, Key = frozen[i].Key, Index = frozen[i].Index };
        HarloweValue child = null;
        if (step.Kind == WriteKind.DatamapKey)
        {
          if (container.Kind != HarloweValueKind.Datamap) return false;
          if (!container.AsDatamap.TryGetValue(step.Key, out child) && !deepest) return false;
        }
        else
        {
          bool isString = container.Kind == HarloweValueKind.String;
          if (!isString && container.Kind != HarloweValueKind.Array) return false;
          step.Kind = isString ? WriteKind.StringIndex : WriteKind.ArrayIndex;
          int count = isString ? CodePointCount(container.AsString) : container.AsArray.Count;
          if (frozen[i].FromEndOrdinal > 0) step.Index = count - frozen[i].FromEndOrdinal + 1;
          bool inRange = step.Index >= 1 && step.Index <= count;
          if (!inRange && !deepest) return false;
          if (inRange) child = isString ? IndexString(container.AsString, step.Index) : container.AsArray[step.Index - 1];
          if (isString && !deepest) throughString = true;
        }
        steps.Add(step);
        container = child;
      }
      return true;
    }

    /// <summary>
    /// Rebuilds a resolved chain with the deepest slot REMOVED — the delete
    /// mirror of <see cref="ApplyWrite"/>, per reference varref.ts's
    /// delete(): the array position is spliced out (no hole), the datamap key
    /// removed, the string code point excised; every level is shallow-cloned
    /// on the way up. Returns the rebuilt root value.
    /// </summary>
    private static HarloweValue ApplyDelete(List<WriteStep> steps)
    {
      HarloweValue child = null;
      for (int i = steps.Count - 1; i >= 0; i--)
      {
        var step = steps[i];
        bool deepest = i == steps.Count - 1;
        switch (step.Kind)
        {
          case WriteKind.DatamapKey:
          {
            var map = new Dictionary<string, HarloweValue>(step.Container.AsDatamap);
            if (deepest) map.Remove(step.Key);
            else map[step.Key] = child;
            child = HarloweValue.OfDatamap(map);
            break;
          }
          case WriteKind.ArrayIndex:
          {
            var arr = new List<HarloweValue>(step.Container.AsArray);
            if (!deepest) arr[step.Index - 1] = child;
            else if (step.Index >= 1 && step.Index <= arr.Count) arr.RemoveAt(step.Index - 1);
            child = HarloweValue.OfArray(arr);
            break;
          }
          default:
          {
            // Only ever the deepest level: a path through a string is refused
            // before this runs.
            string text = step.Container.AsString;
            child = step.Index >= 1 && step.Index <= CodePointCount(text)
              ? HarloweValue.OfString(CodePoints.ReplaceAt(text, step.Index, ""))
              : step.Container;
            break;
          }
        }
      }
      return child;
    }

    public void Visit(MacroCallNode node)
    {
      if (_macros == null)
      {
        _result = HarloweValue.OfError($"macro '{node.Name}' cannot be called: no macro registry configured");
        return;
      }
      if (MacroNames.IsCustomCall(node.Name))
      {
        _result = CallCustomMacro(node.Name, node.Arguments);
        return;
      }
      // Pre-check the macro name so an unknown call surfaces as a clean
      // in-prose error before any arg evaluation work. The historical
      // motivation also included blocking `to`/`into` leakage through arg
      // evaluation; the parser now enforces that restriction syntactically
      // (only (set:)/(put:) accept assignment args), so this guard is just
      // for the clean error message now.
      if (!_macros.Contains(node.Name))
      {
        _result = HarloweValue.OfError($"unknown macro '{node.Name}'");
        return;
      }
      var operatorError = ValidateAssignmentOperators(node.Name, node.Arguments);
      if (operatorError != null) { _result = operatorError; return; }
      var args = new List<HarloweValue>(node.Arguments.Count);
      var argumentError = EvaluateMacroArguments(node.Name, node.Arguments, args);
      if (argumentError != null) { _result = argumentError; return; }

      // A command in an expression is a value, and only does something when it
      // reaches prose. An assignment has already happened by this point: 3.3.9
      // assigns while evaluating it, then refuses it as data.
      var role = _registry != null ? BuiltinCommands.RoleOf(node.Name) : CommandRole.None;
      if (role == CommandRole.Operation)
      {
        _result = HarloweValue.OfError($"A ({BuiltinCommands.CanonicalName(node.Name)}:) operation can't be used as a value.");
        return;
      }
      if (role != CommandRole.None)
      {
        _result = _registry.CheckCall(node.Name, args)
          ?? _registry.CheckCommand(node.Name, args, _registry.Context)
          ?? HarloweValue.OfCommand(CommandValue.ForBuiltin(node.Name, args));
        return;
      }

      _result = _macros.Invoke(node.Name, args) ?? HarloweValue.OfError($"macro '{node.Name}' returned no value");
      StampChangerSource(_result, node.Name, args);
    }

    /// <summary>
    /// Stamp a freshly-created Changer with the Harlowe source that built it —
    /// <c>(name:arg-source,…)</c> from the call node's name and each <em>evaluated</em>
    /// argument's <see cref="HarloweValue.ToSource"/> (reference's resolved
    /// <c>params</c>). Mirrors reference's changer <c>macroName</c>+<c>params</c> →
    /// <c>TwineScript_ToSource</c>, which stamps at <em>creation</em> only: a macro can
    /// also <em>return</em> a changer it was handed — <c>(either: $a, $b)</c> — and
    /// restamping that shared object would rewrite the stored value's source to the
    /// outer call, which re-evaluates on load (nondeterministically, for
    /// <c>(either:)</c>). An already-stamped changer is therefore left untouched.
    /// Best-effort and non-throwing: if any argument has no source form,
    /// <see cref="Changer.Source"/> stays null and the changer simply can't be saved.
    /// Lets a changer stored in a variable round-trip through save/load.
    /// </summary>
    internal static Changer ComposeChangers(Changer left, Changer right)
    {
      // Reference chains composed changers' source with `+`. If either operand
      // is unstamped the composite has no reliable source, so it can't be saved.
      var composed = left.Compose(right);
      string ls = left.Source, rs = right.Source;
      composed.Source = (ls != null && rs != null) ? ls + "+" + rs : null;
      return composed;
    }

    internal static void StampChangerSource(HarloweValue result, string macroName, List<HarloweValue> args)
    {
      if (result == null || result.Kind != HarloweValueKind.Changer) return;
      var changer = result.AsChanger;
      if (changer.Source != null) return;
      var sb = new System.Text.StringBuilder();
      sb.Append('(').Append(macroName).Append(':');
      for (int i = 0; i < args.Count; i++)
      {
        if (i > 0) sb.Append(',');
        string src = args[i].ToSource();
        if (src == null) return;
        sb.Append(src);
      }
      sb.Append(')');
      changer.Source = sb.ToString();
    }

    /// <summary>
    /// Lambdas are opaque at construction time — no scope work happens here.
    /// The runtime wrapper just pins the AST so <see cref="LambdaInvoker"/> can
    /// bind parameters and re-enter the evaluator when a consuming macro fires.
    /// </summary>
    public void Visit(LambdaNode node) => _result = HarloweValue.OfLambda(new LambdaValue { Node = node });

    /// <summary>
    /// A hook reference (<c>?name</c>) evaluates to an opaque
    /// <see cref="HarloweValueKind.HookName"/> value — a selector spec, not a
    /// resolved node. Resolution against the render tree happens lazily inside
    /// the consuming revision/enchantment macros. The AST's ordinal
    /// <see cref="HookRefStep"/> list is carried straight through.
    /// </summary>
    public void Visit(HookRefNode node)
      => _result = HarloweValue.OfHookName(new HookNameValue { Name = node.Name, Steps = node.Steps });

    /// <summary>
    /// True while a save is being read back. Code hooks then keep only their
    /// source and parse on first use, because they were just lexed under the
    /// save format's profile and their contents belong to the story's.
    /// </summary>
    internal bool ReparseCodeHooks;

    public void Visit(CodeHookNode node)
      => _result = HarloweValue.OfCodeHook(new CodeHookValue(node.Source, ReparseCodeHooks && node.Source != null ? null : node.Children));

    /// <summary>
    /// Type-dispatched <c>+</c>. Reference Harlowe (the <c>"+"</c> entry in
    /// <c>ts/twinescript/operations.ts</c>) overloads <c>+</c> across primitive
    /// and collection types using <c>doNotCoerce</c> — both operands must have
    /// the same kind, but each kind has its own meaning of <c>+</c>:
    /// arithmetic for numbers, concatenation for strings and arrays, RHS-wins
    /// merge for datamaps, logical OR for booleans, changer composition for
    /// changers, additive blending for colours. The combinations we don't
    /// support yet (Set, Gradient, HookName) require value types we don't have;
    /// they fall through to the type-mismatch error.
    /// </summary>
    private static HarloweValue OpAdd(HarloweValue left, HarloweValue right)
    {
      if (left.Kind != right.Kind)
        return HarloweValue.OfError($"+ does not apply to {left.Kind} and {right.Kind}");

      switch (left.Kind)
      {
        case HarloweValueKind.Number:
          return HarloweValue.OfNumber(left.AsNumber + right.AsNumber);
        case HarloweValueKind.String:
          return HarloweValue.OfString(left.AsString + right.AsString);
        case HarloweValueKind.Bool:
          // Reference uses JS `||`; for booleans this is logical OR.
          return HarloweValue.OfBool(left.AsBool || right.AsBool);
        case HarloweValueKind.Colour:
          // Additive blend, reference colour.ts's "+": channels
          // min(round((l+r)*0.6), 255), alpha averaged.
          return HarloweValue.OfColour(left.AsColour.Mix(right.AsColour));
        case HarloweValueKind.Array:
        {
          var combined = new List<HarloweValue>(left.AsArray.Count + right.AsArray.Count);
          combined.AddRange(left.AsArray);
          combined.AddRange(right.AsArray);
          return HarloweValue.OfArray(combined);
        }
        case HarloweValueKind.Datamap:
        {
          // Start with LHS entries; overwrite with RHS entries so RHS wins on
          // key collision (matches reference's Map(l) then r.forEach(set)).
          var merged = new Dictionary<string, HarloweValue>(left.AsDatamap);
          foreach (var kv in right.AsDatamap) merged[kv.Key] = kv.Value;
          return HarloweValue.OfDatamap(merged);
        }
        case HarloweValueKind.Changer:
        {
          return HarloweValue.OfChanger(ComposeChangers(left.AsChanger, right.AsChanger));
        }
        default:
          return HarloweValue.OfError($"+ does not apply to {left.Kind} and {right.Kind}");
      }
    }

    /// <summary>
    /// Type-dispatched <c>-</c>. Reference Harlowe (the <c>"-"</c> entry in
    /// <c>ts/twinescript/operations.ts</c>) overloads <c>-</c> with
    /// <c>doNotCoerce</c> — both operands must share kind — to mean "remove
    /// all instances of": <c>String - String</c> deletes every occurrence of
    /// the RHS substring, <c>Array - Array</c> filters out elements that
    /// appear anywhere in the RHS (by <c>is</c>-equality, our
    /// <see cref="HarloweValue.Equals"/>). <c>Set - Set</c> is the set
    /// difference; we don't have a Set value type yet, so that case falls
    /// through to the type-mismatch error.
    ///
    /// <para>Note the reference comment: "Subtracting 1 element from an array
    /// requires it be wrapped in an (a:) macro." Authors write
    /// <c>$list - (a:item)</c>, not <c>$list - item</c>.</para>
    /// </summary>
    private static HarloweValue OpSubtract(HarloweValue left, HarloweValue right)
    {
      if (left.Kind != right.Kind)
        return HarloweValue.OfError($"- does not apply to {left.Kind} and {right.Kind}");

      switch (left.Kind)
      {
        case HarloweValueKind.Number:
          return HarloweValue.OfNumber(left.AsNumber - right.AsNumber);
        case HarloweValueKind.String:
        {
          // Reference: `l.split(r).join('')` — remove every occurrence. An
          // empty RHS means "remove nothing" (matches JS split-on-empty).
          // C#'s string.Replace throws on an empty pattern in .NET 5+, so
          // short-circuit empty RHS rather than depending on platform.
          if (right.AsString.Length == 0) return HarloweValue.OfString(left.AsString);
          return HarloweValue.OfString(left.AsString.Replace(right.AsString, string.Empty));
        }
        case HarloweValueKind.Array:
        {
          var srcL = left.AsArray;
          var srcR = right.AsArray;
          var kept = new List<HarloweValue>(srcL.Count);
          for (int i = 0; i < srcL.Count; i++)
          {
            bool drop = false;
            for (int j = 0; j < srcR.Count; j++)
            {
              if (srcL[i] != null && srcL[i].Equals(srcR[j])) { drop = true; break; }
              if (srcL[i] == null && srcR[j] == null) { drop = true; break; }
            }
            if (!drop) kept.Add(srcL[i]);
          }
          return HarloweValue.OfArray(kept);
        }
        default:
          return HarloweValue.OfError($"- does not apply to {left.Kind} and {right.Kind}");
      }
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

    // Negation wrapper for the `not`-spelled operators (`does not contain`,
    // `is not in`, `is not a`): preserves the positive form's error (e.g.
    // "a String cannot contain a Number") rather than flipping it to true.
    private static HarloweValue OpNegate(HarloweValue v)
      => v.IsError ? v : HarloweValue.OfBool(!v.AsBool);

    /// <summary>
    /// <c>is a</c> — reference's <c>isA</c> in
    /// <c>ts/utils/operationutils.ts</c>: the right side must be a datatype
    /// (anything carrying <c>isTypeOf</c>), and the answer is that type's
    /// verdict on the left. Deliberately narrower than <c>matches</c>, which
    /// takes a whole pattern on either side.
    /// </summary>
    private static HarloweValue OpIsA(HarloweValue left, HarloweValue right)
    {
      if (right.Kind != HarloweValueKind.Datatype)
        return HarloweValue.OfError($"'is a' should only be used to compare type names, not a {right.Kind}");
      return HarloweValue.OfBool(right.AsDatatype.IsTypeOf(left));
    }

    /// <summary>
    /// Matches the recursion caps elsewhere (the parsers, <c>ToSource</c>,
    /// <c>DeepCopyValue</c>, <c>(datapattern:)</c>). A value nested past this
    /// depth can genuinely reach the store — <c>DeepCopyValue</c> stops copying
    /// at its own cap rather than erroring, so a structure grown a level per
    /// turn survives — and an uncapped walk over one would be an uncatchable
    /// <see cref="System.StackOverflowException"/>, which this runtime's
    /// in-prose-errors-never-exceptions contract does not allow.
    /// </summary>
    private const int MaxMatchDepth = 256;

    private const string TooDeepMessage = "I was given a value nested too deeply to compare.";

    /// <summary>
    /// The <c>matches</c>/<c>does not match</c> operator sites: runs the walk
    /// and turns a depth blow-out into an in-prose error rather than a bool.
    /// The <c>(unpack:)</c> pattern check does the same two steps inline,
    /// since its non-match is an error with its own wording.
    /// </summary>
    /// <summary><c>matches</c> as a plain test. A value too deep to compare doesn't match.</summary>
    internal static bool Matches(HarloweValue left, HarloweValue right)
    {
      bool tooDeep = false;
      return MatchesRecur(left, right, 0, ref tooDeep);
    }

    private static HarloweValue OpMatchesValue(HarloweValue left, HarloweValue right, bool negate)
    {
      bool tooDeep = false;
      bool matched = MatchesRecur(left, right, 0, ref tooDeep);
      if (tooDeep) return HarloweValue.OfError(TooDeepMessage);
      return HarloweValue.OfBool(negate ? !matched : matched);
    }

    /// <summary>
    /// <c>matches</c> — reference's <c>matches</c> in
    /// <c>ts/utils/operationutils.ts</c>: structural equality, except that a
    /// datatype on <em>either</em> side is satisfied by the other side being of
    /// that type. That is what lets a data structure with datatypes in it serve
    /// as a pattern: <c>(a: 2, 3) matches (a: num, num)</c>.
    ///
    /// <para>Two datatypes match if either types the other, so
    /// <c>datatype matches num</c> is true (the <c>datatype</c> type accepts
    /// <c>num</c>) — reference's <c>||=</c> over both directions.</para>
    ///
    /// <para>Unlike reference this returns a plain bool: its error case comes
    /// only from the <c>(p-opt:)</c> string-pattern family, which isn't
    /// implemented here. A typed variable in a pattern matches by its type, and
    /// a spread datatype (<c>...num</c>) spans a run of array positions.</para>
    /// </summary>
    private static bool MatchesRecur(HarloweValue left, HarloweValue right, int depth, ref bool tooDeep)
    {
      if (depth >= MaxMatchDepth) { tooDeep = true; return false; }

      if (left.Kind == HarloweValueKind.TypedVar) return MatchesRecur(left.AsTypedVar.Datatype, right, depth + 1, ref tooDeep);
      if (right.Kind == HarloweValueKind.TypedVar) return MatchesRecur(left, right.AsTypedVar.Datatype, depth + 1, ref tooDeep);

      if (left.Kind == HarloweValueKind.Datatype && left.AsDatatype.IsTypeOf(right)) return true;
      if (right.Kind == HarloweValueKind.Datatype && right.AsDatatype.IsTypeOf(left)) return true;

      if (left.Kind == HarloweValueKind.Array && right.Kind == HarloweValueKind.Array)
      {
        return MatchesArrays(left.AsArray, right.AsArray, depth, ref tooDeep);
      }

      if (left.Kind == HarloweValueKind.Datamap && right.Kind == HarloweValueKind.Datamap)
      {
        // Reference sorts both entry lists and matches them pairwise, which for
        // string keys is exactly "same keys, values match" — a datatype can
        // never stand in for a key here, because our datamap keys are strings
        // rather than values.
        var lm = left.AsDatamap;
        var rm = right.AsDatamap;
        if (lm.Count != rm.Count) return false;
        foreach (var kv in lm)
        {
          if (!rm.TryGetValue(kv.Key, out var rv)) return false;
          if (!MatchesRecur(kv.Value, rv, depth + 1, ref tooDeep)) return false;
        }
        return true;
      }

      return left.Equals(right);
    }

    private static bool IsSpreadDatatype(HarloweValue value)
      => value.Kind == HarloweValueKind.Datatype && value.AsDatatype.IsSpread;

    /// <summary>
    /// The array walk in reference's <c>matches</c>. A spread datatype on either
    /// side takes as long a run from the other side as it can, which may be no
    /// run at all. Both sides have to be used up, so a spread left over when the
    /// other side runs out fails the match.
    /// </summary>
    private static bool MatchesArrays(List<HarloweValue> left, List<HarloweValue> right, int depth, ref bool tooDeep)
    {
      int l = 0, r = 0;
      while (l < left.Count && r < right.Count)
      {
        if (IsSpreadDatatype(left[l]))
        {
          while (r < right.Count && MatchesRecur(left[l], right[r], depth + 1, ref tooDeep)) r++;
          l++;
        }
        else if (IsSpreadDatatype(right[r]))
        {
          while (l < left.Count && MatchesRecur(left[l], right[r], depth + 1, ref tooDeep)) l++;
          r++;
        }
        else
        {
          if (!MatchesRecur(left[l], right[r], depth + 1, ref tooDeep)) return false;
          l++;
          r++;
        }
      }
      return l >= left.Count && r >= right.Count;
    }

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
      if (key.Kind == HarloweValueKind.String
          && (container.Kind == HarloweValueKind.CustomMacro || container.Kind == HarloweValueKind.TypedVar))
        return ResolveIdentifierAccessor(container, key.AsString);
      return ResolveValueAccessor(container, key);
    }

    private HarloweValue ResolveIdentifierAccessor(HarloweValue container, string name)
    {
      switch (container.Kind)
      {
        case HarloweValueKind.CustomMacro:
        {
          if (name != "params") return HarloweValue.OfError($"a custom macro has no property '{name}'");
          var parameters = container.AsCustomMacro.Parameters;
          var list = new List<HarloweValue>(parameters.Count);
          for (int i = 0; i < parameters.Count; i++) list.Add(HarloweValue.OfTypedVar(parameters[i]));
          return HarloweValue.OfArray(list);
        }
        case HarloweValueKind.TypedVar:
          if (name == "name") return HarloweValue.OfString(container.AsTypedVar.Name);
          if (name == "datatype") return container.AsTypedVar.DatatypeProperty;
          return HarloweValue.OfError($"a typed variable name has no property '{name}'");
        case HarloweValueKind.Datamap:
          if (container.AsDatamap.TryGetValue(name, out var v)) return v;
          return HarloweValue.OfError($"datamap has no key '{name}'");
        case HarloweValueKind.Array:
          if (name == "length") return HarloweValue.OfNumber(container.AsArray.Count);
          if (name == "random")
          {
            if (container.AsArray.Count == 0)
              return EmptyRandomError("array");
            return IndexArray(container.AsArray, RandomIndex(container.AsArray.Count));
          }
          if (Ordinals.TryParse(name, out int aIdx, out bool aFromEnd))
          {
            int target = aFromEnd ? container.AsArray.Count - aIdx + 1 : aIdx;
            return IndexArray(container.AsArray, target);
          }
          return HarloweValue.OfError($"array has no property '{name}'");
        case HarloweValueKind.String:
          // Strings count and index by Unicode code point, not UTF-16 code
          // unit — reference Harlowe spreads strings into code points (JS
          // `[...str]`), so an astral character (surrogate pair) is one
          // character, not two.
          if (name == "length") return HarloweValue.OfNumber(CodePointCount(container.AsString));
          if (name == "random")
          {
            int chars = CodePointCount(container.AsString);
            if (chars == 0)
              return EmptyRandomError("string");
            return IndexString(container.AsString, RandomIndex(chars));
          }
          if (Ordinals.TryParse(name, out int sIdx, out bool sFromEnd))
          {
            int target = sFromEnd ? CodePointCount(container.AsString) - sIdx + 1 : sIdx;
            return IndexString(container.AsString, target);
          }
          return HarloweValue.OfError($"string has no property '{name}'");
        case HarloweValueKind.Colour:
        {
          // r/g/b/a plus derived h/s/l — reference colour.ts's getProperty.
          // (lch/oklch data names are deferred with the LCH colour models.)
          var prop = container.AsColour.GetProperty(name);
          if (prop != null) return prop;
          return HarloweValue.OfError($"a colour has no property '{name}'");
        }
        default:
          return HarloweValue.OfError($"a {container.Kind} has no properties");
      }
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
        return HarloweValue.OfError($"array index must be a whole number; got {HarloweValue.FormatNumber(idx)}");
      int i = (int)idx;
      if (i < 1 || i > arr.Count)
        return HarloweValue.OfError($"array index {i} is out of range (1..{arr.Count})");
      return arr[i - 1];
    }

    private static HarloweValue IndexString(string s, double idx)
    {
      if (idx != System.Math.Floor(idx))
        return HarloweValue.OfError($"string index must be a whole number; got {HarloweValue.FormatNumber(idx)}");
      int i = (int)idx;
      int count = CodePointCount(s);
      if (i < 1 || i > count)
        return HarloweValue.OfError($"string index {i} is out of range (1..{count})");
      // Walk to the i-th code point (1-based), returning the whole code point
      // — both halves of a surrogate pair — rather than a lone surrogate.
      int seen = 0;
      for (int p = 0; p < s.Length; p++)
      {
        bool pair = char.IsHighSurrogate(s[p]) && p + 1 < s.Length && char.IsLowSurrogate(s[p + 1]);
        if (++seen == i)
          return HarloweValue.OfString(pair ? s.Substring(p, 2) : s[p].ToString());
        if (pair) p++;
      }
      return HarloweValue.OfError($"string index {i} is out of range (1..{count})");
    }

    /// <summary>
    /// Counts Unicode code points in <paramref name="s"/> (surrogate pairs as
    /// one), matching reference Harlowe's code-point string model rather than
    /// .NET's UTF-16 code-unit <see cref="string.Length"/>. Delegates to the
    /// shared <see cref="CodePoints"/> helper.
    /// </summary>
    private static int CodePointCount(string s) => CodePoints.Count(s);
  }
}
