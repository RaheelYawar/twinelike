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
  /// <b>Conditional rendering.</b> <c>(if:)</c>, <c>(unless:)</c>, and
  /// <c>(else:)</c> return a Boolean that decides whether the macro's
  /// <see cref="MacroNode.AttachedHook"/> renders. Non-conditional macros
  /// also reset <see cref="MacroContext.LastConditional"/> so an
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
        if (_context.PendingGoto != null) return;
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

    public void Visit(HookNode node)
    {
      if (node.Children == null) return;
      RenderChildren(node.Children);
    }

    public void Visit(MacroNode node)
    {
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

      bool isConditional = node.Name == "if" || node.Name == "unless" || node.Name == "else";

      var result = _registry.Invoke(node.Name, args, _context);

      // Reset the conditional pairing only after non-conditional macros, so
      // intervening prose between (if:) and (else:) does not break the pair
      // but an intervening (set:) does.
      if (!isConditional) _context.LastConditional = null;

      if (result != null && result.IsError) { _output.Error(result.ErrorMessage); return; }

      // (goto:) and any macro that triggered a navigation aborts now.
      if (_context.PendingGoto != null) return;

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
          result.AsChanger.Apply(_output, () => node.AttachedHook.Accept(this));
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
