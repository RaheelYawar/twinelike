using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Callback the evaluator uses when it encounters a value-returning
  /// <see cref="Ast.Expression.MacroCallNode"/> nested inside an expression
  /// (e.g. the inner <c>(random: 1, 6)</c> in <c>(set: $r to (random: 1, 6))</c>).
  /// Implemented by <see cref="MacroRegistry"/>; the interface is kept narrow
  /// so the evaluator can be built and unit-tested without depending on the
  /// registry's full surface.
  /// </summary>
  public interface IMacroInvoker
  {
    /// <summary>
    /// Look up the macro named <paramref name="name"/> and invoke it with
    /// pre-evaluated <paramref name="args"/>. Returning a
    /// <see cref="HarloweValueKind.Error"/> for unknown names or arity
    /// mismatches lets the error surface in-prose rather than as a thrown
    /// exception.
    /// </summary>
    HarloweValue Invoke(string name, List<HarloweValue> args);
  }
}
