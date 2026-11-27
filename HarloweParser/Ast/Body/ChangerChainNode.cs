using Harlowe.Ast.Expression;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A variable followed by a hook, <c>$var[hook]</c> or <c>$var [hook]</c>.
  /// What the hook does depends on the variable's value at render time:
  ///
  /// <list type="bullet">
  /// <item>A <see cref="Runtime.HarloweValueKind.Changer"/> applies to the hook, and the whitespace between them goes.</item>
  /// <item>A boolean shows or hides the hook.</item>
  /// <item>Any other value prints in place, followed by the whitespace and the hook.</item>
  /// <item>An error replaces the variable, and the whitespace and hook still render.</item>
  /// </list>
  /// </summary>
  public class ChangerChainNode : IBodyNode
  {
    public IExpressionNode Expression;
    public HookNode AttachedHook;

    /// <summary>The whitespace written between the variable and <see cref="AttachedHook"/>, or null when there's none.</summary>
    public string HookGap;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
