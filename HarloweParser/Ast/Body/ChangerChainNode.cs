using Harlowe.Ast.Expression;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A body-position expression that evaluates to a
  /// <see cref="Runtime.HarloweValueKind.Changer"/> and applies to an attached
  /// <see cref="HookNode"/>. Built by the body parser for two shapes:
  ///
  /// <list type="bullet">
  /// <item><c>(macro1)+(macro2)+...[hook]</c> — the canonical Harlowe inline composition syntax. <see cref="Expression"/> is a tree of <see cref="BinaryOpNode"/> over <see cref="MacroCallNode"/>s.</item>
  /// <item><c>$var[hook]</c> or <c>$var [hook]</c> — stored-changer-then-hook. <see cref="Expression"/> is a <see cref="VariableRefNode"/>.</item>
  /// </list>
  ///
  /// <para>The renderer evaluates <see cref="Expression"/> and:
  /// <list type="bullet">
  /// <item>If the result is a Changer, calls <see cref="Runtime.Changer.Apply"/> with <see cref="AttachedHook"/>.</item>
  /// <item>If the result is any other value, emits the value's text form and then renders the hook's children — preserves backward-compat for the unusual but legal "interpolate variable, then anonymous hook" pattern.</item>
  /// <item>If the result is an Error, routes through <see cref="Runtime.IRenderOutput.Error"/>.</item>
  /// </list></para>
  ///
  /// <para><see cref="AttachedHook"/> may be <c>null</c> for chains that are
  /// authored without a trailing hook (rare, equivalent to evaluating the
  /// composition for its side effects only — usually a no-op for changer
  /// chains since changers don't have side effects).</para>
  /// </summary>
  public class ChangerChainNode : IBodyNode
  {
    public IExpressionNode Expression;
    public HookNode AttachedHook;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
