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

    /// <summary>RNG used by <c>random</c>/<c>either</c>. A session-shared <see cref="IRng"/> (default <see cref="MulberryRng"/>, time-seeded); a seeded session or tests overwrite it.</summary>
    public IRng Rng = new MulberryRng();

    /// <summary>
    /// The compatibility profile runtime behaviour follows — the major the
    /// story declared. Defaults to <see cref="HarloweProfile.Latest"/>;
    /// <see cref="StorySession"/> overwrites it with the story's own profile.
    /// Parse-time switches are applied by the tokenizer instead and never
    /// reach here.
    /// </summary>
    public HarloweProfile Profile = HarloweProfile.Latest;

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
    /// go through <see cref="MissingPassageError"/>, which centralizes the
    /// null-check-skips-validation convention those tests rely on.
    /// </summary>
    public Func<string, bool> PassageExists;

    /// <summary>
    /// The in-prose error for a navigation target that doesn't exist, or null
    /// when <paramref name="passageName"/> is fine — the shared gate every
    /// navigation site (<c>(goto:)</c>, <c>(link-goto:)</c>, the
    /// <c>(click-goto:)</c> family, <c>[[…]]</c> rendering) checks before
    /// committing a target. Null (no error) when <see cref="PassageExists"/> is
    /// unwired, so standalone renders skip validation. <paramref name="how"/>
    /// is the verb phrase — <c>"link to"</c>, <c>"(goto:)"</c> — spliced into
    /// the one shared sentence.
    /// </summary>
    public HarloweValue MissingPassageError(string how, string passageName)
    {
      if (PassageExists == null || PassageExists(passageName)) return null;
      return HarloweValue.OfError(MissingPassageMessage(how, passageName));
    }

    /// <summary>The sentence <see cref="MissingPassageError"/> wraps, for callers outside a macro context (<see cref="StorySession.Goto"/>).</summary>
    public static string MissingPassageMessage(string how, string passageName)
      => $"I can't {how} the passage '{passageName}' because it doesn't exist.";

    /// <summary>
    /// True when a past turn exists to undo to. Set by <see cref="StorySession"/>
    /// so the <c>(click-undo:)</c>-family commands can reject first-turn use
    /// (reference Harlowe's <c>State.pastLength &lt; 1</c> check). Null in
    /// standalone tests with no session — callers skip the check, same
    /// convention as <see cref="PassageExists"/>.
    /// </summary>
    public Func<bool> CanUndo;

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
    /// Staged by a successful <c>(load-game:)</c>: the deserialised timeline to
    /// install. Like <see cref="PendingGoto"/> it halts the body render; the session
    /// installs it and navigates to the loaded passage after the render. Set by
    /// <see cref="StorySession"/> per render. Null = no load pending.
    /// </summary>
    public Saving.DeserialiseResult PendingLoad;

    /// <summary>True when render should stop and hand control back to the session for a navigation — a pending <c>(goto:)</c> or <c>(load-game:)</c>.</summary>
    public bool NavigationHalt => PendingGoto != null || PendingLoad != null;

    /// <summary>
    /// True while rendering a just-loaded passage (set by <see cref="StorySession"/>
    /// from its session-held flag, cleared on the next navigation). <c>(load-game:)</c>
    /// reads it to no-op a re-load from within the loaded passage — the infinite-loop
    /// guard, mirroring reference's per-<c>showPassage</c> <c>section.loadedGame</c>.
    /// </summary>
    public bool LoadedGame;

    /// <summary>Save the current timeline to a slot (+ optional filename); returns success. Wired to <see cref="StorySession.SaveGame"/>; null in standalone tests (then <c>(save-game:)</c> is false).</summary>
    public Func<string, string, bool> SaveGame;

    /// <summary>List this story's saves (slot → filename). Wired to <see cref="StorySession.SavedGames"/>; null in standalone tests (then <c>(saved-games:)</c> is empty).</summary>
    public Func<IReadOnlyDictionary<string, string>> SavedGames;

    /// <summary>Read + deserialise a slot's blob without installing it (the deferred half of a load). Wired to <see cref="StorySession"/>; null in standalone tests.</summary>
    public Func<string, Saving.DeserialiseResult> PrepareLoad;

    /// <summary>
    /// The conditional pairing flag — reference's per-frame <c>lastHookShown</c>.
    /// Written only at hook-application time by
    /// <see cref="BodyRenderer"/> (never by the conditional macros themselves):
    /// a shown attached hook — changer or boolean — sets true; a hidden one
    /// sets false only for attached booleans and expressions whose front macro
    /// name is <c>if</c>/<c>unless</c>/<c>else</c> (an <c>(else-if:)</c> hide,
    /// or one from a stored changer in a variable, preserves the prior value).
    /// Read by <c>(else:)</c>/<c>(else-if:)</c> at call time to bake their
    /// decision; null when nothing pairable has rendered in the current hook
    /// scope.
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
    /// The armed interactions keyed by <see cref="InteractiveRegion"/> id — a
    /// pure index into <see cref="Interactions"/> holding only the entries
    /// whose target matched something this pass (an unmatched interaction has
    /// no region to fire). Rebuilt by <see cref="InteractionPass.Update"/> each
    /// pass; consumed by <see cref="StorySession.DispatchEvent"/>, which the
    /// host calls with the region id the user activated. Shared with the
    /// session — the same dictionary is reused across the main render and any
    /// subsequent dispatch re-renders.
    /// </summary>
    public Dictionary<string, Interaction> ClickHandlers = new Dictionary<string, Interaction>();

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

    /// <summary>
    /// Snapshot the session-owned mutable state that a pass-time expression
    /// evaluation must not leak — pending navigation
    /// (<see cref="PendingGoto"/>/<see cref="PendingLoad"/>), the persistent
    /// <see cref="Enchantments"/>/<see cref="Interactions"/> registration lists,
    /// and the <see cref="Rng"/> stream position — and restore it on dispose.
    /// Used to sandbox <c>via</c>-lambda evaluation inside
    /// <see cref="EnchantmentPass"/>: a lambda's only job is to produce a
    /// changer, but our evaluator runs command macros in expression position
    /// (a documented divergence), so a clause reaching a <c>(goto:)</c>,
    /// <c>(enchant:)</c>, or <c>(random:)</c> would otherwise clobber a queued
    /// navigation, grow the very list the pass is iterating, or desync the
    /// reproducible RNG the save model records. The variable store is
    /// deliberately not snapshotted — a per-match deep copy would be a hot-path
    /// cost, and a <c>(set:)</c> reached this way already surfaces as a "not a
    /// changer" error.
    /// </summary>
    public IDisposable PushSideEffectGuard() => new SideEffectGuard(this);

    private sealed class SideEffectGuard : IDisposable
    {
      private readonly MacroContext _ctx;
      private readonly string _pendingGoto;
      private readonly Saving.DeserialiseResult _pendingLoad;
      private readonly int _enchantments;
      private readonly int _interactions;
      private readonly bool _hasRng;
      private readonly string _seed;
      private readonly int _seedIter;

      public SideEffectGuard(MacroContext ctx)
      {
        _ctx = ctx;
        _pendingGoto = ctx.PendingGoto;
        _pendingLoad = ctx.PendingLoad;
        _enchantments = ctx.Enchantments?.Count ?? 0;
        _interactions = ctx.Interactions?.Count ?? 0;
        _hasRng = ctx.Rng != null;
        _seed = _hasRng ? ctx.Rng.Seed : null;
        _seedIter = _hasRng ? ctx.Rng.SeedIter : 0;
      }

      public void Dispose()
      {
        _ctx.PendingGoto = _pendingGoto;
        _ctx.PendingLoad = _pendingLoad;
        Truncate(_ctx.Enchantments, _enchantments);
        Truncate(_ctx.Interactions, _interactions);
        // Restore the stream position only when a draw actually advanced it —
        // SetSeed re-derives the generator state in O(1) from (seed, iter).
        if (_hasRng && _ctx.Rng != null
            && (_ctx.Rng.Seed != _seed || _ctx.Rng.SeedIter != _seedIter))
          _ctx.Rng.SetSeed(_seed, _seedIter);
      }

      private static void Truncate<T>(List<T> list, int count)
      {
        if (list != null && list.Count > count) list.RemoveRange(count, list.Count - count);
      }
    }
  }
}
