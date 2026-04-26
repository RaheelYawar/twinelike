using System.Collections.Generic;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// Where a hook's name is anchored relative to its bracket block.
  /// <see cref="None"/> means the hook is anonymous.
  /// </summary>
  public enum HookAnchor
  {
    /// <summary>Anonymous hook with no name attached.</summary>
    None,
    /// <summary>Name appears after the brackets: <c>[content]&lt;name|</c>.</summary>
    Left,
    /// <summary>Name appears before the brackets: <c>|name&gt;[content]</c>.</summary>
    Right
  }

  /// <summary>
  /// A hook — a bracketed region of passage content (<c>[ ... ]</c>) that can
  /// be addressed and operated on by macros. Hooks are Harlowe's unit of
  /// "addressable content"; a macro can render, suppress, repeat, replace,
  /// style, or otherwise transform a hook.
  ///
  /// <para>Hooks come in four shapes the parser must accept:</para>
  /// <list type="number">
  /// <item><c>(if: $x)[content]</c> — anonymous, attached to a macro via <see cref="MacroNode.AttachedHook"/>.</item>
  /// <item><c>|greeting&gt;[content]</c> — named with a right anchor.</item>
  /// <item><c>[content]&lt;greeting|</c> — named with a left anchor.</item>
  /// <item><c>[content]</c> — bare anonymous hook, targetable later by position or selector.</item>
  /// </list>
  ///
  /// Hooks can contain any body content, including more hooks and macros, so
  /// <see cref="Children"/> is a list of <see cref="IBodyNode"/>.
  /// </summary>
  public class HookNode : IBodyNode
  {
    /// <summary>The hook's name without the <c>|</c> delimiters; <c>null</c> for anonymous hooks.</summary>
    public string Name;

    /// <summary>Where the name is anchored relative to the brackets.</summary>
    public HookAnchor Anchor;

    /// <summary>The body nodes contained inside the brackets.</summary>
    public List<IBodyNode> Children;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
