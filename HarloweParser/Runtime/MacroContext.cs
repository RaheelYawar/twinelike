using System;
using System.Collections.Generic;

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
    /// Renders the named passage into <paramref name="output"/>. Returns
    /// <see cref="HarloweValue.OfString(string)"/> with an empty payload on
    /// success, or an <see cref="HarloweValueKind.Error"/> value if the
    /// passage doesn't exist. Set by <see cref="StorySession"/> before each
    /// render; null in standalone evaluator/renderer tests, in which case
    /// <c>(display:)</c> emits an in-prose error rather than crashing.
    /// </summary>
    public Func<string, IRenderOutput, HarloweValue> RenderPassage;

    /// <summary>
    /// The active body-position render sink. <see cref="BodyRenderer"/> sets
    /// this around each macro dispatch and clears it on the way out; the
    /// expression evaluator never sets it. Command macros like
    /// <c>(display:)</c> consult it to decide whether to render directly into
    /// the parent output (preserving Link/Error/Style events) or to capture
    /// into a private buffer and return the rendered text as a String value
    /// (for expression-position use such as <c>(set: $x to (display: "P"))</c>).
    /// </summary>
    public IRenderOutput Output;

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

    /// <summary>
    /// Persistent restylings registered by <c>(enchant:)</c>. The session runs
    /// <see cref="EnchantmentPass.Update"/> over the finished render tree once
    /// the passage's main render completes, so an enchantment catches hooks
    /// declared after the macro and content rewritten by revision macros.
    /// Fresh per render — each <see cref="StorySession"/> render pass starts
    /// with an empty list.
    /// </summary>
    public List<Enchantment> Enchantments = new List<Enchantment>();

    /// <summary>Convenience setter for <see cref="PendingGoto"/>; equivalent to <c>ctx.PendingGoto = name</c> but reads better in macro implementations.</summary>
    public void RequestGoto(string passageName) => PendingGoto = passageName;
  }
}
