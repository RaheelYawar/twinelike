using System.Collections.Generic;
using Harlowe.Ast.Expression;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A macro invocation that lives directly in passage prose — i.e. a *command*
  /// macro that performs an action when the passage is rendered. Examples:
  /// <c>(set: $hp to 10)</c>, <c>(if: $brave)[step inside]</c>, <c>(goto: "End")</c>.
  ///
  /// <para>
  /// Compare with <see cref="MacroCallNode"/>, which represents the same
  /// <c>(name: ...)</c> syntax when it appears *inside another macro's
  /// argument list* and is being treated as a value-returning expression.
  /// The split exists because:
  /// </para>
  /// <list type="bullet">
  /// <item>Only body-position macros may attach a <see cref="HookNode"/>.</item>
  /// <item>Body macros run for their effect; expression macros are evaluated for their value.</item>
  /// <item>Their children carry different node types — body vs expression — which keeps the visitor pattern strongly typed.</item>
  /// </list>
  /// </summary>
  public class MacroNode : IBodyNode
  {
    /// <summary>The macro name (without the surrounding parens or trailing colon), e.g. <c>"if"</c>, <c>"set"</c>, <c>"goto"</c>.</summary>
    public string Name;

    /// <summary>Arguments passed to the macro. Each argument is an expression-tree node.</summary>
    public List<IExpressionNode> Arguments;

    /// <summary>
    /// The bracketed hook that follows the macro, if any. For
    /// <c>(if: $x)[then]</c> this holds the <c>[then]</c> block; for
    /// <c>(set: $x to 1)</c> this is <c>null</c>. Only command macros in body
    /// position can attach a hook — that is why this field lives here and not
    /// on <see cref="MacroCallNode"/>.
    /// </summary>
    public HookNode AttachedHook;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
