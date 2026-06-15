using System;
using System.Collections.Generic;
using Harlowe.Runtime.Rendering;

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
    /// True if a passage with the given name exists in the story. Set by
    /// <see cref="StorySession"/> so <c>(goto:)</c> can validate its target
    /// before navigating (reference Harlowe's <c>Passages.hasValid</c> check).
    /// Null in standalone evaluator/renderer tests with no story behind them —
    /// callers must null-check and skip validation when it's absent, preserving
    /// the bare "record the goto" behaviour those tests rely on.
    /// </summary>
    public Func<string, bool> PassageExists;

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
    /// Set by <c>(if:)</c>/<c>(unless:)</c>/<c>(else-if:)</c> after they
    /// evaluate. Read by <c>(else:)</c>/<c>(else-if:)</c> to decide whether
    /// their hook should render. Cleared by the body renderer after each
    /// non-conditional sibling so an else only ever pairs with the immediately
    /// preceding conditional. (<c>(else-if:)</c> deliberately preserves this
    /// value when it hides its hook — see <see cref="Macros.ElseIfMacro"/>.)
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

    /// <summary>
    /// Persistent interactions registered by the <c>(click:)</c>/<c>(mouseover:)</c>
    /// family. The session runs <see cref="InteractionPass.Update"/> over the
    /// finished tree after the main render and after every dispatch, so an
    /// interaction catches hooks declared after the macro and content spliced in
    /// by a click — the analogue of <see cref="Enchantments"/>. Fresh per render.
    /// </summary>
    public List<Interaction> Interactions = new List<Interaction>();

    /// <summary>
    /// Registered interaction handlers keyed by <see cref="InteractiveRegion"/>
    /// id. Rebuilt from <see cref="Interactions"/> by
    /// <see cref="InteractionPass.Update"/> each pass; consumed by
    /// <see cref="StorySession.DispatchEvent"/>. Shared with the session — the
    /// same dictionary is reused across the main render and any subsequent
    /// dispatch re-renders.
    /// </summary>
    public Dictionary<string, ClickHandler> ClickHandlers = new Dictionary<string, ClickHandler>();

    /// <summary>
    /// The live render tree the session is building or has built. Macros that
    /// target rendered content (<c>(replace:)</c>, <c>(change:)</c>,
    /// <c>(click:)</c>, …) resolve against this rather than the current
    /// renderer's output, so a deferred-hook render whose own
    /// <see cref="IRenderOutput"/> is a detached builder still targets the
    /// passage's live tree. Null when there is no render tree (a plain
    /// <see cref="BufferedRenderOutput"/> in a standalone unit test).
    /// </summary>
    public RenderRoot LiveRoot;

    /// <summary>
    /// Resolves the render tree that tree-targeting macros (<c>(replace:)</c>,
    /// <c>(change:)</c>, <c>(click:)</c>, …) operate on: the session's
    /// <see cref="LiveRoot"/> if set, otherwise the root of
    /// <paramref name="output"/> when it is itself a
    /// <see cref="RenderTreeBuilder"/>. Preferring <see cref="LiveRoot"/> lets a
    /// deferred-dispatch hook (whose own output is a detached builder) still
    /// target the passage's live tree. Null-safe in <paramref name="ctx"/> so a
    /// pure revision changer may pass a null context; returns null when there
    /// is no tree to target (a plain buffer). Centralized here so every
    /// tree-targeting path shares one rule.
    /// </summary>
    public static RenderRoot ResolveLiveRoot(MacroContext ctx, IRenderOutput output)
      => ctx?.LiveRoot ?? (output as RenderTreeBuilder)?.Root;

    /// <summary>
    /// Counter for generating fresh <see cref="InteractiveRegion"/> ids
    /// (rendered as <c>"r-N"</c>). Stays unique across the main render and any
    /// dispatch re-renders within the same session render cycle.
    /// </summary>
    public int NextRegionIndex;

    /// <summary>
    /// Number of <c>(display:)</c> calls currently on the stack — incremented by
    /// <see cref="StorySession"/> before recursing into the displayed passage
    /// and decremented on the way out. Used to bound runaway recursion when a
    /// passage displays itself (directly or through a cycle); compare against
    /// <see cref="StorySession"/>'s <c>MaxDisplayDepth</c>. Lives here rather
    /// than on the session so the count survives across the fresh
    /// <see cref="BodyRenderer"/> each inline display creates.
    /// </summary>
    public int DisplayDepth;

    /// <summary>Convenience setter for <see cref="PendingGoto"/>; equivalent to <c>ctx.PendingGoto = name</c> but reads better in macro implementations.</summary>
    public void RequestGoto(string passageName) => PendingGoto = passageName;

    /// <summary>Pull and consume the next unique region id for an interactive wrap. Format <c>"r-N"</c>.</summary>
    public string AllocateRegionId() => "r-" + NextRegionIndex++;
  }
}
