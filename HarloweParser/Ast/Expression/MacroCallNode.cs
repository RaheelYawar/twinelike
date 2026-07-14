using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A macro invocation used as a *value* — i.e. one that appears inside
  /// another macro's argument list. Example: in <c>(set: $r to (random: 1, 6))</c>
  /// the inner <c>(random: 1, 6)</c> is a <see cref="MacroCallNode"/> while the
  /// outer <c>(set: ...)</c> is a <see cref="Body.MacroNode"/>.
  ///
  /// <para>
  /// Why split this from <see cref="Body.MacroNode"/>:
  /// </para>
  /// <list type="bullet">
  /// <item>Expression-position macros never carry a hook, so there is no <c>AttachedHook</c> field — the type system rules out invalid trees.</item>
  /// <item>The runtime contract is different: this node is *evaluated* to produce a value; a body macro is *executed* and may emit text.</item>
  /// <item>The visitor that handles expressions is separate from the one that handles body content.</item>
  /// </list>
  ///
  /// The same Harlowe macro name (e.g. <c>random</c>) can legally appear as
  /// either kind of node, depending on where it sits in the source.
  /// </summary>
  public class MacroCallNode : IExpressionNode
  {
    /// <summary>The macro name, e.g. <c>"random"</c>, <c>"either"</c>, <c>"a"</c>.</summary>
    public string Name;

    /// <summary>The arguments passed to the macro, each itself an expression.</summary>
    public List<IExpressionNode> Arguments;

    /// <summary>
    /// 1-based line of the macro's name token in the passage body, or <c>0</c>
    /// when unknown (a hand-built AST). Carried so a diagnostic can point the
    /// author at the call — see <see cref="Harlowe.GetBrokenLinks"/>.
    /// </summary>
    public int Line;

    /// <summary>1-based column of the macro's name token; <c>0</c> when unknown.</summary>
    public int Column;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
