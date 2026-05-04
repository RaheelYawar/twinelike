using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A single Harlowe macro implementation. Both value-returning macros (used
  /// inside expressions, e.g. <c>(random:)</c>) and command macros (used in
  /// passage body, e.g. <c>(set:)</c>, <c>(goto:)</c>) share this signature.
  /// The split between the two flavours is enforced by the AST — a value
  /// macro is a <see cref="Ast.Expression.MacroCallNode"/>, a command macro is
  /// a <see cref="Ast.Body.MacroNode"/> — so a separate
  /// <c>IValueMacro</c>/<c>ICommandMacro</c> split would not add information
  /// the dispatcher cannot already see.
  ///
  /// <para>
  /// Argument-count validation lives in <see cref="MacroRegistry"/>, not in
  /// each macro. <see cref="MinArgs"/> and <see cref="MaxArgs"/> declare the
  /// arity envelope; the registry rejects calls outside it with an in-prose
  /// error before <see cref="Invoke"/> is ever called.
  /// </para>
  /// </summary>
  public interface IMacro
  {
    /// <summary>The macro name as it appears in source — without the surrounding parens or trailing colon.</summary>
    string Name { get; }

    /// <summary>Minimum legal argument count (zero is allowed for nullary macros like <c>(else:)</c>).</summary>
    int MinArgs { get; }

    /// <summary>Maximum legal argument count, or <c>-1</c> for unlimited (variadic). The registry enforces this when non-negative.</summary>
    int MaxArgs { get; }

    /// <summary>
    /// Run the macro. Pre-evaluated <paramref name="args"/> are arity-checked
    /// before this method is called. Value macros return their result; command
    /// macros mutate <paramref name="context"/> and return null. The registry
    /// translates a null return into an empty (no-op) result for value-macro
    /// callers.
    /// </summary>
    HarloweValue Invoke(List<HarloweValue> args, MacroContext context);
  }
}
