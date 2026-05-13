using Harlowe.Ast.Expression;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Runtime wrapper around a parsed <see cref="LambdaNode"/>. Lambdas are
  /// opaque values until a consuming macro (<c>(find:)</c>, <c>(altered:)</c>,
  /// <c>(folded:)</c>, ...) invokes them; the wrapper exists so the tagged
  /// union has somewhere to point.
  ///
  /// <para>
  /// Lambdas in Harlowe see the caller's variable store at invocation time
  /// rather than closing over construction-time bindings, so no environment
  /// snapshot is needed here — just the AST. <see cref="LambdaInvoker"/>
  /// handles parameter binding and clause evaluation.
  /// </para>
  /// </summary>
  public class LambdaValue
  {
    public LambdaNode Node;

    /// <summary>
    /// Reference equality on the underlying AST. Two parses of the same source
    /// produce distinct nodes and are therefore not equal — Harlowe authors
    /// rarely compare lambdas directly, so structural equality isn't worth the
    /// AST-walk cost. Keeps <see cref="HarloweValue.Equals"/> cheap.
    /// </summary>
    public override bool Equals(object obj) => obj is LambdaValue other && ReferenceEquals(Node, other.Node);

    public override int GetHashCode() => Node == null ? 0 : Node.GetHashCode();
  }
}
