using System;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Shared state passed to every <see cref="IMacro.Invoke"/> call. Carries the
  /// variable store (so <c>(set:)</c>/<c>(put:)</c> can write), the evaluation
  /// context (so a macro can re-evaluate sub-expressions if v2 needs it), the
  /// macro invoker (so a macro can call sibling macros), an RNG (seeded for
  /// test determinism), and per-render mutable flags for navigation and
  /// passage display.
  ///
  /// <para>
  /// Concrete class with public fields rather than an interface: this is a
  /// plain DTO and the project style favours public fields for DTOs. If a
  /// future test needs to mock it, an interface can be extracted then.
  /// </para>
  /// </summary>
  public class MacroContext
  {
    /// <summary>Variable store mutated by assignment-flavoured macros. Required.</summary>
    public IVariableStore Store;

    /// <summary>Read-only session view used for built-in identifiers. Optional.</summary>
    public IEvaluationContext EvaluationContext;

    /// <summary>Macro dispatcher used by the evaluator when a value-returning macro call is nested inside an expression (e.g. <c>(set: $r to (random: 1, 6))</c>). Optional.</summary>
    public IMacroInvoker Invoker;

    /// <summary>RNG used by <c>random</c>/<c>either</c>. Initialised with a system seed; tests may overwrite with a seeded instance.</summary>
    public Random Rng = new Random();

    /// <summary>
    /// Renders another passage by name and returns its visible text as a
    /// <see cref="HarloweValueKind.String"/>. Set by <see cref="StorySession"/>
    /// before each render; null in standalone evaluator/renderer tests, in
    /// which case <c>(display:)</c> emits an in-prose error rather than
    /// crashing.
    /// </summary>
    public Func<string, HarloweValue> RenderPassage;

    /// <summary>
    /// Set by <c>(goto:)</c>. The body renderer reads this after each macro to
    /// decide whether to abort further node processing and signal a navigation
    /// to the session. <c>null</c> means no goto requested.
    /// </summary>
    public string PendingGoto;

    /// <summary>
    /// Set by <c>(if:)</c>/<c>(unless:)</c> after they evaluate. Read by
    /// <c>(else:)</c> to decide whether its hook should render. Cleared by the
    /// body renderer after each non-conditional sibling so an else only ever
    /// pairs with the immediately preceding if/unless.
    /// </summary>
    public bool? LastConditional;

    /// <summary>Convenience setter for <see cref="PendingGoto"/>; equivalent to <c>ctx.PendingGoto = name</c> but reads better in macro implementations.</summary>
    public void RequestGoto(string passageName) => PendingGoto = passageName;
  }
}
