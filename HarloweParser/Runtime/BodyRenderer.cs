using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Walks a <see cref="PassageBody"/>, executes command macros, and pushes
  /// the visible output through an <see cref="IRenderOutput"/>. Implements
  /// <see cref="IBodyVisitor"/>; the public entry point is <see cref="Render"/>.
  ///
  /// <para>
  /// <b>Conditional rendering.</b> <c>(if:)</c>, <c>(unless:)</c>,
  /// <c>(else-if:)</c>, and <c>(else:)</c> return a Boolean that decides whether
  /// the macro's <see cref="MacroNode.AttachedHook"/> renders. Non-conditional
  /// macros also reset <see cref="MacroContext.LastConditional"/> so an
  /// <c>(else:)</c> only pairs with the <em>immediately preceding</em>
  /// conditional macro and not across an intervening <c>(set:)</c> or similar.
  /// </para>
  ///
  /// <para>
  /// <b>Changer rendering.</b> When a macro returns a
  /// <see cref="HarloweValueKind.Changer"/> and has an
  /// <see cref="MacroNode.AttachedHook"/>, the changer's open HTML is emitted,
  /// the hook contents are rendered, and the close HTML follows — see
  /// <see cref="Changer.Apply"/>. Without an attached hook the changer is a
  /// pure value (no visible output), which lets authors store changers in
  /// variables for later application via composition.
  /// </para>
  ///
  /// <para>
  /// <b>Goto handling.</b> A successful <c>(goto:)</c> sets
  /// <see cref="MacroContext.PendingGoto"/>; the renderer checks that flag
  /// before each sibling node and inside hook recursion, aborting render the
  /// moment it appears. The session reads the flag after render to navigate.
  /// </para>
  ///
  /// <para>
  /// <b>Errors.</b> Argument-evaluation errors and macro-returned errors are
  /// pushed through <see cref="IRenderOutput.Error"/>; they never throw. An
  /// unset variable in body position emits an error and the surrounding
  /// passage continues to render.
  /// </para>
  /// </summary>
  public class BodyRenderer : IBodyVisitor
  {
    private readonly IRenderOutput _output;
    private readonly MacroRegistry _registry;
    private readonly MacroContext _context;
    private readonly ExpressionEvaluator _evaluator;

    public BodyRenderer(IRenderOutput output, MacroRegistry registry, MacroContext context)
    {
      _output = output;
      _registry = registry;
      _context = context;
      _evaluator = new ExpressionEvaluator(context.Store, context.EvaluationContext, registry);
    }

    /// <summary>
    /// Render <paramref name="body"/> into the output sink. Aborts early on
    /// <see cref="MacroContext.PendingGoto"/>. Safe with a null body or null
    /// children list.
    /// </summary>
    public void Render(PassageBody body)
    {
      if (body == null || body.Children == null) return;
      RenderChildren(body.Children);
    }

    private void RenderChildren(List<IBodyNode> children)
    {
      for (int i = 0; i < children.Count; i++)
      {
        if (_context.NavigationHalt) return;
        children[i].Accept(this);
      }
    }

    public void Visit(TextNode node) => _output.Text(node.Content);

    public void Visit(NewlineNode node) => _output.Text("\n");

    public void Visit(VariableNode node)
    {
      var value = _context.Store.Get(node.Name, node.IsTemporary);
      if (value == null)
      {
        string sigil = node.IsTemporary ? "_" : "$";
        _output.Error($"{sigil}{node.Name} is not set");
        return;
      }
      if (value.IsError) { _output.Error(value.ErrorMessage); return; }
      _output.Text(value.ToHarloweString());
    }

    public void Visit(HtmlNode node) => _output.Html(node.RawHtml);

    public void Visit(LinkNode node) => _output.Link(node.Text, node.Target);

    public void Visit(ParseErrorNode node) => _output.Error(node.Message);

    /// <summary>
    /// Render a hook's contents. When the output is a
    /// <see cref="Rendering.RenderTreeBuilder"/>, the contents are bracketed as
    /// a <see cref="Rendering.RenderHookNode"/> — the addressable unit revision
    /// and enchantment macros target. Anonymous hooks still produce a node
    /// (with a null name) so position/string targeting can find them. When the
    /// output is a plain sink (a unit-test buffer, an expression-position
    /// <c>(display:)</c> capture) there is no tree to build into, so the hook
    /// renders flat — exactly the pre-render-tree behaviour.
    ///
    /// <para>Pushes a fresh temp-variable scope for the hook body so authors
    /// who write <c>(set: _x to ...)</c> inside a hook see Harlowe's
    /// documented hook-scoped semantics: a freshly-declared temp variable
    /// dies on hook exit, but <c>(set:)</c> of an outer-scoped temp variable
    /// still updates the outer binding. Matches reference Harlowe
    /// (ts/internaltypes/varscope.ts).</para>
    /// </summary>
    public void Visit(HookNode node)
    {
      var builder = _output as Rendering.RenderTreeBuilder;
      builder?.BeginHook(node.Name, node.Anchor);
      // A hook is its own conditional scope. Reference Harlowe renders each
      // hook in a fresh stack frame whose lastHookShown starts unset, so a
      // conditional inside a hook can neither pair with an (else:) outside it
      // nor leak its show/hide decision back out. Start the hook body with a
      // cleared pairing and restore the outer value on exit.
      var priorConditional = _context.LastConditional;
      _context.LastConditional = null;
      using (_context.Store.PushTempScope())
      {
        if (node.Children != null) RenderChildren(node.Children);
      }
      _context.LastConditional = priorConditional;
      builder?.EndHook();
    }

    /// <summary>
    /// Render an inline-formatted span (<c>''bold''</c> / <c>//italic//</c>) by
    /// bracketing its content in the matching <see cref="StyleSpec"/> flag —
    /// the same style channel <c>(text-style: "bold"/"italic")</c> uses, so the
    /// render tree wraps it in a <see cref="Rendering.RenderStyleNode"/> that
    /// enchant/revision macros can target. Unlike a hook, an inline format span
    /// is not its own temp-variable or conditional scope and never touches
    /// <see cref="MacroContext.LastConditional"/>, matching reference Harlowe
    /// (style markup does not affect <c>lastHookShown</c>).
    /// </summary>
    public void Visit(FormatNode node)
    {
      _output.PushStyle(StyleFor(node.Format));
      if (node.Children != null) RenderChildren(node.Children);
      _output.PopStyle();
    }

    /// <summary>
    /// The <see cref="Ast.Body.InlineFormat"/> → <see cref="StyleSpec"/> mapping
    /// (the runtime half of the centralized format mapping; the delimiter half
    /// lives in <see cref="Ast.Body.InlineFormats"/>). The throwing default is a
    /// developer invariant guard — a <c>FormatNode</c> only ever carries a value
    /// the tokenizer + parser produced, so an unmapped format means a new markup
    /// type was wired up without a style here, caught loudly by tests rather than
    /// rendering silently unstyled. Not reachable from author input, so it does
    /// not breach the in-prose-error policy.
    /// </summary>
    private static StyleSpec StyleFor(Ast.Body.InlineFormat format)
    {
      var spec = new StyleSpec();
      switch (format)
      {
        case Ast.Body.InlineFormat.Bold: spec.Bold = true; break;
        case Ast.Body.InlineFormat.Italic: spec.Italic = true; break;
        case Ast.Body.InlineFormat.Strike: spec.Strikethrough = true; break;
        // Superscript markup is a semantic primitive (renders <sup>), kept
        // distinct from the (text-style:"superscript") macro's CSS effect —
        // matching reference, which renders ^^ as <sup> but the macro as a span.
        case Ast.Body.InlineFormat.Superscript: spec.Superscript = true; break;
        default:
          throw new System.ArgumentOutOfRangeException(
            nameof(format), format, "no StyleSpec mapping for this InlineFormat");
      }
      return spec;
    }

    /// <summary>
    /// Render a changer-chain node: evaluate the expression, apply if it
    /// resolves to a <see cref="HarloweValueKind.Changer"/>, and fall back to
    /// "emit value text + render hook" for any other value so non-Changer
    /// variables still render predictably (the body parser builds these for
    /// any <c>$var[hook]</c> shape regardless of what the runtime value
    /// turns out to be).
    /// </summary>
    public void Visit(ChangerChainNode node)
    {
      var result = _evaluator.Evaluate(node.Expression);
      if (result == null) return;
      if (result.IsError) { _output.Error(result.ErrorMessage); return; }

      if (result.Kind == HarloweValueKind.Changer)
      {
        if (node.AttachedHook != null)
        {
          result.AsChanger.Apply(_output, target => RenderHookInto(node.AttachedHook, target), _context);
          // Symmetric with the changer branch in Visit(MacroNode): a shown
          // changer hook records that a hook was shown so a following (else:)
          // pairs against it rather than the conditional before the changer.
          _context.LastConditional = true;
        }
        return;
      }

      // Non-Changer: emit the value's text form (matching the existing
      // VariableNode behaviour), then render the hook contents — preserves
      // backward compat for stories using `$var[content]` to mean "value
      // followed by anonymous hook".
      EmitMacroResult(result);
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    public void Visit(MacroNode node)
    {
      // Pre-check the macro name so an unknown call surfaces as a clean
      // in-prose error rather than the arity / type errors a no-op registry
      // dispatch would otherwise produce. `to`/`into` mutation leakage is
      // no longer a concern here: the parser only emits assignment binary
      // ops at the top of (set:)/(put:) argument positions, so arg evaluation
      // for any other macro can't trigger a hidden assignment.
      if (!_registry.Contains(node.Name))
      {
        _output.Error($"unknown macro '{node.Name}'");
        return;
      }

      var args = new List<HarloweValue>(node.Arguments != null ? node.Arguments.Count : 0);
      if (node.Arguments != null)
      {
        for (int i = 0; i < node.Arguments.Count; i++)
        {
          var v = _evaluator.Evaluate(node.Arguments[i]);
          if (v.IsError) { _output.Error(v.ErrorMessage); return; }
          args.Add(v);
        }
      }

      // Normalize so case/dash variants ((If:), (Else-If:)) pair correctly;
      // "else-if" normalizes to "elseif".
      string macroName = MacroNames.Normalize(node.Name);
      bool isConditional = macroName == "if" || macroName == "unless"
        || macroName == "else" || macroName == "elseif";

      // Expose the active sink for command macros (e.g. (display:)) that want
      // to render structured output directly into the parent output rather
      // than capture it as a string. Cleared on the way out so a subsequent
      // arg-eval pass (which goes through ExpressionEvaluator with no
      // surrounding BodyRenderer) sees a null sink and routes through the
      // buffered-snapshot path instead.
      var priorOutput = _context.Output;
      _context.Output = _output;
      HarloweValue result;
      try { result = _registry.Invoke(node.Name, args, _context); }
      finally { _context.Output = priorOutput; }

      // The conditional pairing (LastConditional, reference's lastHookShown)
      // is only touched when a hook is shown or hidden by an attached
      // expression — never reset by a plain command macro or by intervening
      // prose. So an intervening (set:)/(print:) leaves the pairing intact and
      // a following (else:) still pairs with the original (if:), matching
      // reference Harlowe. A non-conditional changer that shows its hook sets
      // the pairing below (after the hook renders).
      if (result != null && result.IsError) { _output.Error(result.ErrorMessage); return; }

      // (goto:) and any macro that triggered a navigation aborts now.
      if (_context.NavigationHalt) return;

      if (isConditional)
      {
        bool render = result != null && result.Kind == HarloweValueKind.Bool && result.AsBool;
        if (render && node.AttachedHook != null) node.AttachedHook.Accept(this);
        return;
      }

      // Changer + attached hook: open / render contents / close. Without an
      // attached hook the changer is a pure value — we drop it (storing it in
      // a variable would have happened during evaluation, not here).
      if (result != null && result.Kind == HarloweValueKind.Changer)
      {
        if (node.AttachedHook != null)
        {
          result.AsChanger.Apply(_output, target => RenderHookInto(node.AttachedHook, target), _context);
          // A shown changer hook counts as "a hook was shown" for a following
          // (else:) — reference sets lastHookShown=true for any enabled
          // attached-expression hook, not just conditionals.
          _context.LastConditional = true;
        }
        return;
      }

      // Non-changer non-conditional: emit any visible value, then render the
      // hook (if any) unconditionally.
      if (result != null && !result.IsError)
      {
        EmitMacroResult(result);
      }
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    /// <summary>
    /// Render <paramref name="hook"/> into <paramref name="target"/>. When the
    /// target is this renderer's own output, reuse this renderer; otherwise
    /// spin up a sub-renderer sharing the registry and context. The latter is
    /// how a revision changer renders its hook into a detached tree — see
    /// <see cref="Changer.Apply"/> — without touching the live output.
    /// </summary>
    private void RenderHookInto(IBodyNode hook, IRenderOutput target)
    {
      if (hook == null) return;
      if (ReferenceEquals(target, _output)) { hook.Accept(this); return; }
      hook.Accept(new BodyRenderer(target, _registry, _context));
    }

    private void EmitMacroResult(HarloweValue result)
    {
      switch (result.Kind)
      {
        case HarloweValueKind.String:
          if (result.AsString.Length > 0) _output.Text(result.AsString);
          return;
        case HarloweValueKind.Number:
        case HarloweValueKind.Bool:
        case HarloweValueKind.Array:
        case HarloweValueKind.Datamap:
          _output.Text(result.ToHarloweString());
          return;
      }
    }
  }
}
