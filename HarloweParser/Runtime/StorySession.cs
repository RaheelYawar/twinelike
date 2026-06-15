using System;
using System.Collections.Generic;
using System.Diagnostics;
using Harlowe.Runtime.Macros;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Top-level engine surface. Wraps a parsed <see cref="Harlowe"/> story and
  /// drives it through the runtime layer: variable store, macro registry, body
  /// renderer, and visit tracking. Implements <see cref="IEvaluationContext"/>
  /// so the evaluator can resolve <c>time</c>, <c>visits</c>, and
  /// <c>passage</c> against live session state.
  ///
  /// <para>
  /// <b>Navigation model.</b> <see cref="Goto"/> transitions to a new passage
  /// (snapshots current state for undo, clears passage-scoped variables,
  /// increments visit count) and returns the rendered result.
  /// <see cref="Render"/> re-renders the current passage without changing
  /// navigation state — call it for the initial render and after
  /// <see cref="Undo"/>. Both methods automatically follow any
  /// <c>(goto:)</c> macros encountered during render; up to
  /// <see cref="MaxGotoDepth"/> consecutive redirects are followed before an
  /// error entry is emitted.
  /// </para>
  ///
  /// <para>
  /// <b>Undo.</b> Multi-step: every <see cref="Goto"/> pushes a snapshot onto
  /// the undo stack; <see cref="Undo"/> pops the most recent snapshot and
  /// restores its passage, variable store, and visit counts. After
  /// <see cref="Undo"/> returns <c>true</c>, call <see cref="Render"/> to
  /// redisplay the restored passage. <see cref="Undo"/> returns <c>false</c>
  /// only when the stack is empty (no <see cref="Goto"/> has happened, or
  /// every prior step has already been undone). The stack is unbounded.
  /// </para>
  ///
  /// <para>
  /// <b>Display.</b> <c>(display:)</c> macros inline-render another passage
  /// using the current variable store and macro context. In body position
  /// (the common case) the passage renders directly into the active output
  /// so Link/Error/Style events propagate; in expression position
  /// (e.g. <c>(set: $x to (display: "P"))</c>) the rendered text is captured
  /// and returned as a String value. (display:) does not affect navigation
  /// state or visit counts.
  /// </para>
  /// </summary>
  public class StorySession : IEvaluationContext
  {
    private readonly Harlowe _story;
    private readonly MacroRegistry _registry;
    private readonly HarloweVariableStore _store;
    private string _currentPassage;
    private Dictionary<string, int> _visitCounts;
    private readonly List<SessionSnapshot> _undoStack;
    private readonly Stopwatch _passageTimer;

    // One RNG for the whole session, threaded into every MacroContext. A fresh
    // `new Random()` per render leg would re-seed from the system tick, so on
    // .NET Framework / Mono (tick-seeded Random) successive legs within the
    // same ~15ms tick — e.g. a passage and the passage a (goto:) redirects to —
    // produced identical (random:)/(either:) sequences. One shared instance
    // gives a single continuous stream instead.
    private readonly Random _rng;

    // The live render-tree state for the most recent main render. Kept alive
    // across renders so DispatchEvent can mutate it, re-flush, and return an
    // updated RenderResult without re-rendering the whole passage. Both reset
    // at the start of each main render.
    private Rendering.RenderRoot _liveRoot;
    private MacroContext _liveContext;

    private const int MaxGotoDepth = 20;

    // Default ceiling on (display:) recursion. A passage displaying itself
    // (or a cycle through several passages) would otherwise blow the .NET
    // stack before any author-visible error appeared. Mirrors MaxGotoDepth so
    // the two navigation-shaped macros have a consistent default ceiling, but
    // authors building modular UIs out of nested displays can raise the
    // ceiling via the public setter at construction time.
    private const int DefaultMaxDisplayDepth = 20;

    // Absolute ceiling, regardless of consumer configuration. .NET's default
    // 1MB stack with a few hundred bytes per recursion frame leaves room for
    // a few thousand levels, but we leave a safety margin — exceeding the
    // real stack throws StackOverflowException, which is uncatchable and
    // crashes the host process, violating the no-throws runtime contract.
    private const int AbsoluteMaxDisplayDepth = 256;

    private int _maxDisplayDepth = DefaultMaxDisplayDepth;

    /// <summary>
    /// Maximum (display:) nesting depth. Mutating mid-render is supported but
    /// only affects subsequent (display:) calls — the active stack frame
    /// continues unaffected. Values are clamped to <c>[1, 256]</c>: under 1
    /// would refuse a single (display:) call (the documented baseline);
    /// above 256 risks a StackOverflowException, which terminates the host
    /// process and bypasses the in-prose error contract.
    /// </summary>
    public int MaxDisplayDepth
    {
      get => _maxDisplayDepth;
      set
      {
        if (value < 1) value = 1;
        else if (value > AbsoluteMaxDisplayDepth) value = AbsoluteMaxDisplayDepth;
        _maxDisplayDepth = value;
      }
    }

    /// <summary>Name of the passage currently loaded into the session.</summary>
    public string CurrentPassage => _currentPassage;

    /// <summary>
    /// Builds a session from a parsed story. The session starts at the passage
    /// whose pid matches <see cref="Harlowe.StartNode"/>; call
    /// <see cref="Render"/> to obtain its content.
    /// </summary>
    public StorySession(Harlowe story) : this(story, new Random()) { }

    /// <summary>
    /// Builds a session with a fixed RNG seed, so <c>(random:)</c>/<c>(either:)</c>
    /// produce a reproducible sequence across the whole session — useful for
    /// tests and replays.
    /// </summary>
    public StorySession(Harlowe story, int seed) : this(story, new Random(seed)) { }

    private StorySession(Harlowe story, Random rng)
    {
      _story = story;
      _registry = new MacroRegistry();
      StandardMacros.RegisterAll(_registry);
      _store = new HarloweVariableStore();
      _visitCounts = new Dictionary<string, int>();
      _undoStack = new List<SessionSnapshot>();
      _passageTimer = Stopwatch.StartNew();
      _rng = rng;

      var startPassage = story.GetStartPassage();
      EnterPassage(startPassage != null ? startPassage.Name : string.Empty);
    }

    // IEvaluationContext ---------------------------------------------------

    /// <summary>Milliseconds elapsed since the current passage was entered.</summary>
    public HarloweValue Time => HarloweValue.OfNumber(_passageTimer.ElapsedMilliseconds);

    /// <summary>How many times the current passage has been entered this session.</summary>
    public HarloweValue Visits
    {
      get
      {
        if (string.IsNullOrEmpty(_currentPassage)) return HarloweValue.OfNumber(0);
        if (_visitCounts.TryGetValue(_currentPassage, out var v)) return HarloweValue.OfNumber(v);
        return HarloweValue.OfNumber(0);
      }
    }

    /// <summary>
    /// Total number of passage transitions in the current session, counting
    /// the current one. <see cref="_undoStack"/> holds one entry per past
    /// passage (appended at the start of every <see cref="Goto"/>), so the
    /// count plus the current-passage-is-live bit gives the total.
    /// </summary>
    public HarloweValue Turns =>
      HarloweValue.OfNumber(_undoStack.Count + (string.IsNullOrEmpty(_currentPassage) ? 0 : 1));

    /// <summary>
    /// Past passage names in visit order, oldest first, excluding the
    /// current passage. Backs the <c>(history:)</c> macro. Each undo entry
    /// stores its prior passage name and the list is oldest-first (appended at
    /// each <see cref="Goto"/>), so a forward walk yields visit order directly.
    /// </summary>
    public HarloweValue History
    {
      get
      {
        var list = new List<HarloweValue>(_undoStack.Count);
        for (int i = 0; i < _undoStack.Count; i++)
          list.Add(HarloweValue.OfString(_undoStack[i].PassageName ?? string.Empty));
        return HarloweValue.OfArray(list);
      }
    }

    /// <summary>Datamap describing the current passage. Contains <c>name</c> (String) and <c>tags</c> (Array of String).</summary>
    public HarloweValue Passage
    {
      get
      {
        var map = new Dictionary<string, HarloweValue>();
        map["name"] = HarloweValue.OfString(_currentPassage ?? string.Empty);
        map["tags"] = HarloweValue.OfArray(BuildTags());
        return HarloweValue.OfDatamap(map);
      }
    }

    private List<HarloweValue> BuildTags()
    {
      var list = new List<HarloweValue>();
      if (string.IsNullOrEmpty(_currentPassage)) return list;
      var passage = _story.GetPassage(_currentPassage);
      if (passage == null || passage.Tags == null) return list;
      for (int i = 0; i < passage.Tags.Count; i++) list.Add(HarloweValue.OfString(passage.Tags[i]));
      return list;
    }

    // Public API -----------------------------------------------------------

    /// <summary>
    /// Renders the current passage and returns the result. Does not change
    /// navigation state or visit counts. Automatically follows any
    /// <c>(goto:)</c> macros (updating <see cref="CurrentPassage"/> as a side
    /// effect if a redirect fires). Call this for the initial render and after
    /// <see cref="Undo"/>.
    /// </summary>
    public RenderResult Render() => RenderInternal(0);

    /// <summary>
    /// Navigates to <paramref name="passageName"/>, records the leaving turn's
    /// variable delta as an undo entry, increments the target's visit count,
    /// clears passage-scoped variables, and returns the rendered result. Any
    /// <c>(goto:)</c> macros in the target passage are followed automatically.
    /// </summary>
    public RenderResult Goto(string passageName)
    {
      _undoStack.Add(new SessionSnapshot
      {
        PassageName = _currentPassage,
        StoreDelta = _store.TakeStoryDelta(),
        VisitCounts = CopyVisitCounts()
      });
      EnterPassage(passageName);
      return RenderInternal(0);
    }

    /// <summary>
    /// Removes the most recent undo entry and restores its passage name,
    /// story-variable state (reconstructed from the per-turn deltas), and visit
    /// counts. Returns <c>true</c> if an entry was available and applied;
    /// <c>false</c> if the stack is
    /// empty. After returning <c>true</c>, call <see cref="Render"/> to
    /// display the restored passage. May be called repeatedly to walk back
    /// through every <see cref="Goto"/> the session has performed.
    ///
    /// <para>
    /// The live render tree, click-handler registry, and enchantment list are
    /// torn down — they belonged to the post-<see cref="Goto"/> passage, not
    /// the one we're returning to. A <see cref="DispatchEvent"/> call made
    /// between <see cref="Undo"/> and the next <see cref="Render"/> is a
    /// no-op rather than firing handlers against a stale tree. The next
    /// <see cref="Render"/> rebuilds both the tree and the handler/enchantment
    /// state from passage source, so anything the restored passage's
    /// <c>(click:)</c>/<c>(enchant:)</c> macros register gets re-registered
    /// fresh.
    /// </para>
    /// </summary>
    public bool Undo()
    {
      if (_undoStack.Count == 0) return false;
      int i = _undoStack.Count - 1;
      var snap = _undoStack[i];
      // Reconstruct the full story-var state at this undo point by flattening
      // every delta up to and including it (last-write-wins), then install it.
      // ResetStoryVars deep-copies on install, so the timeline's deltas stay
      // independent of the live store.
      _store.ResetStoryVars(Flatten(i));
      _currentPassage = snap.PassageName;
      _visitCounts = snap.VisitCounts;
      _undoStack.RemoveAt(i);
      _liveRoot = null;
      _liveContext = null;
      _passageTimer.Restart();
      return true;
    }

    /// <summary>
    /// Reconstructs the full story-variable state at undo point
    /// <paramref name="upToInclusive"/> by applying each entry's forward delta
    /// in chronological order (oldest first), last write winning. The returned
    /// dictionary holds references into the deltas; the caller deep-copies on
    /// install.
    /// </summary>
    private Dictionary<string, HarloweValue> Flatten(int upToInclusive)
    {
      var flat = new Dictionary<string, HarloweValue>();
      for (int j = 0; j <= upToInclusive; j++)
      {
        var delta = _undoStack[j].StoreDelta;
        if (delta == null) continue;
        foreach (var kv in delta) flat[kv.Key] = kv.Value;
      }
      return flat;
    }

    // Private helpers ------------------------------------------------------

    private void EnterPassage(string passageName)
    {
      _currentPassage = passageName ?? string.Empty;
      _store.BeginPassage();
      if (!string.IsNullOrEmpty(_currentPassage))
      {
        if (!_visitCounts.ContainsKey(_currentPassage)) _visitCounts[_currentPassage] = 0;
        _visitCounts[_currentPassage]++;
      }
      _passageTimer.Restart();
    }

    private RenderResult RenderInternal(int depth)
    {
      // Reset live state at the start of each top-level render. If this
      // render fails (missing passage, goto-depth exceeded), there is no
      // tree for DispatchEvent to mutate — and dispatching into a stale
      // tree from a previous passage under the new passage name would be a
      // correctness bug. The success path sets _liveRoot/_liveContext
      // again below. Recursive calls (goto chains) pass depth > 0 and skip
      // the reset so the outer-most clear stays authoritative.
      if (depth == 0)
      {
        _liveRoot = null;
        _liveContext = null;
      }

      if (string.IsNullOrEmpty(_currentPassage))
        return EmptyResult(_currentPassage);

      var passage = _story.GetPassage(_currentPassage);
      if (passage == null)
        return EmptyResult(_currentPassage);

      var ctx = new MacroContext
      {
        Store = _store,
        EvaluationContext = this,
        Invoker = _registry,
        Rng = _rng
      };
      ctx.RenderPassage = (name, output) => InlineDisplayPassage(name, output, ctx);
      ctx.PassageExists = name => _story.GetPassage(name) != null;
      _registry.Context = ctx;

      // Render into a tree, then flush the finished tree to the buffer the
      // RenderResult is built from. The tree is kept alive on the session so
      // DispatchEvent can mutate it (splice click-deferred content, re-run
      // enchantments) without re-rendering the whole passage.
      var builder = new Rendering.RenderTreeBuilder();
      ctx.LiveRoot = builder.Root;
      new BodyRenderer(builder, _registry, ctx).Render(passage.Ast);

      if (ctx.PendingGoto != null)
      {
        if (depth >= MaxGotoDepth)
        {
          var errEntries = new List<BufferedRenderOutput.Entry>();
          errEntries.Add(new BufferedRenderOutput.Entry
          {
            Kind = BufferedRenderOutput.Kind.Error,
            Content = "too many (goto:) redirects"
          });
          return new RenderResult { PassageName = _currentPassage, Text = string.Empty, Entries = errEntries };
        }
        EnterPassage(ctx.PendingGoto);
        return RenderInternal(depth + 1);
      }

      // Re-resolve interactions ((click:)/(mouseover:)) then enchantments over
      // the finished tree. By now every later-declared hook is in the tree and
      // every revision mutation has happened, so the first pass catches
      // everything — including forward-referenced (click: ?b) targets that the
      // old eager apply-time resolution missed. Both passes are idempotent
      // (strip + re-apply), so DispatchEvent re-runs them after click-driven
      // mutations without double-wrapping. Interactions first so enchantment
      // restylings layer outside the interactive wraps, matching prior nesting.
      InteractionPass.Update(builder.Root, ctx.Interactions, ctx.ClickHandlers);
      EnchantmentPass.Update(builder.Root, ctx.Enchantments);

      // Remember the live tree + context for DispatchEvent.
      _liveRoot = builder.Root;
      _liveContext = ctx;

      return BuildResultFromLiveTree();
    }

    /// <summary>
    /// Report a user interaction (click, hover-enter, hover-leave) reported
    /// by the host engine for one of the regions the most recent
    /// <see cref="RenderResult"/> exposed. The session fires the registered
    /// handler — unwrapping the consumed interactive region, rendering the
    /// deferred prose into the targeted nodes via the same revision machinery
    /// <c>(replace:)</c> uses, and re-running the enchantment pass — and
    /// returns a fresh <see cref="RenderResult"/> reflecting the updated live
    /// tree. Single-use: the handler is removed from the registry on dispatch.
    ///
    /// <para>
    /// An unknown <paramref name="regionId"/> (one the engine reports for a
    /// region that has already fired, or that never existed) is a no-op —
    /// the current view is returned unchanged. A deferred hook that runs
    /// <c>(goto:)</c> transitions to the new passage via <see cref="Goto"/>.
    /// </para>
    /// </summary>
    public RenderResult DispatchEvent(string regionId)
    {
      if (_liveRoot == null || _liveContext == null) return EmptyResult(_currentPassage);
      if (regionId == null || !_liveContext.ClickHandlers.TryGetValue(regionId, out var handler))
        return BuildResultFromLiveTree();

      // Consume — single-use. Remove the fired interaction from the persistent
      // list so the interaction pass below won't re-wrap or re-register it.
      // (The handler dictionary is rebuilt from the list by the pass, so there
      // is no separate registry entry to remove.)
      for (int i = 0; i < _liveContext.Interactions.Count; i++)
      {
        if (_liveContext.Interactions[i].RegionId == regionId)
        {
          _liveContext.Interactions.RemoveAt(i);
          break;
        }
      }

      // Render the deferred hook into a detached subtree using the live
      // context — so inner (click:)/(enchant:)/(replace:) calls inside the
      // deferred hook register against the live session state (e.g. a nested
      // (click:) appends to _liveContext.Interactions and is picked up below).
      var detached = new Rendering.RenderTreeBuilder();
      handler.RenderDeferredHook?.Invoke(detached);
      var source = detached.Root.Children;

      // Splice the source into every node the target re-resolves to right
      // now — the query is fresh, matching Harlowe's "?name is a query" rule.
      // The consumed region's leftover wrap (still present for append/prepend)
      // is harmless: the interaction pass strips all wraps next.
      var targets = Rendering.HookResolver.Resolve(_liveRoot, handler.Target);
      for (int i = 0; i < targets.Count; i++)
      {
        if (targets[i] is Rendering.IRenderContainer container)
          SpliceInto(container, source, handler.Mode);
      }

      // Re-run both passes (strip + re-apply — idempotent). The interaction
      // pass strips every interactive wrap (including the consumed region's)
      // and re-wraps the surviving interactions, re-registering their handlers;
      // then enchantments re-layer.
      //
      // Ordering invariant: both passes run BEFORE the PendingGoto check below.
      // Safe because neither takes a MacroContext — InteractionPass.Update and
      // EnchantmentPass.Update both operate on (root, …) only, with no surface
      // through which they could mutate the click's queued navigation. If a
      // future refactor threads MacroContext into either, this ordering would
      // let pass-time macro execution clobber the click's goto;
      // EnchantmentPassCannotMutatePendingGoto in the test suite guards that.
      InteractionPass.Update(_liveRoot, _liveContext.Interactions, _liveContext.ClickHandlers);
      EnchantmentPass.Update(_liveRoot, _liveContext.Enchantments);

      // A (goto:) inside the deferred hook navigates now.
      if (_liveContext.PendingGoto != null)
      {
        var target = _liveContext.PendingGoto;
        _liveContext.PendingGoto = null;
        return Goto(target);
      }

      return BuildResultFromLiveTree();
    }

    /// <summary>Re-flush the live tree into a fresh <see cref="RenderResult"/>. Returns an empty result when there is no live tree yet (no render has run).</summary>
    private RenderResult BuildResultFromLiveTree()
    {
      if (_liveRoot == null) return EmptyResult(_currentPassage);
      var buf = new BufferedRenderOutput();
      Rendering.RenderTreeFlusher.Flush(_liveRoot, buf);
      return new RenderResult
      {
        PassageName = _currentPassage,
        Text = buf.Text,
        Entries = buf.Entries
      };
    }

    /// <summary>Splice deep clones of <paramref name="source"/> into <paramref name="target"/>'s children according to <paramref name="mode"/>. Mirrors <c>Changer.Splice</c>; kept here so dispatch doesn't need internal access into the changer.</summary>
    private static void SpliceInto(Rendering.IRenderContainer target, List<Rendering.RenderNode> source, RevisionMode mode)
    {
      var copy = Rendering.RenderNodes.CloneAll(source);
      switch (mode)
      {
        case RevisionMode.Replace:
          target.Children.Clear();
          target.Children.AddRange(copy);
          break;
        case RevisionMode.Append:
          target.Children.AddRange(copy);
          break;
        case RevisionMode.Prepend:
          target.Children.InsertRange(0, copy);
          break;
      }
    }

    /// <summary>
    /// Renders the named passage into <paramref name="output"/>, which the
    /// caller picked: <see cref="DisplayMacro"/> hands in the active body
    /// render sink for in-prose use (so Link/Error/Style events propagate),
    /// or a private buffer for expression-position use (so the rendered text
    /// can be returned as a String). Returns an Error value if the passage
    /// isn't found or if <see cref="MaxDisplayDepth"/> nested displays would
    /// be exceeded; otherwise returns an empty String — the work is the
    /// side-effect on <paramref name="output"/>.
    /// </summary>
    private HarloweValue InlineDisplayPassage(string name, IRenderOutput output, MacroContext ctx)
    {
      var passage = _story.GetPassage(name);
      if (passage == null) return HarloweValue.OfError($"passage '{name}' not found");
      if (ctx.DisplayDepth >= MaxDisplayDepth)
        return HarloweValue.OfError($"(display:) recursion limit reached at '{name}'");
      ctx.DisplayDepth++;
      try { new BodyRenderer(output, _registry, ctx).Render(passage.Ast); }
      finally { ctx.DisplayDepth--; }
      return HarloweValue.OfString(string.Empty);
    }

    private static RenderResult EmptyResult(string passageName)
      => new RenderResult
      {
        PassageName = passageName,
        Text = string.Empty,
        Entries = new List<BufferedRenderOutput.Entry>()
      };

    private Dictionary<string, int> CopyVisitCounts()
    {
      var copy = new Dictionary<string, int>(_visitCounts.Count);
      foreach (var kv in _visitCounts) copy[kv.Key] = kv.Value;
      return copy;
    }

    private class SessionSnapshot
    {
      public string PassageName;
      // The forward delta of story ($) variables changed during this turn —
      // only the vars that changed, not a full store clone. Flattened
      // oldest-first to reconstruct full state on undo.
      public Dictionary<string, HarloweValue> StoreDelta;
      public Dictionary<string, int> VisitCounts;
    }
  }
}
