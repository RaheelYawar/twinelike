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
    private readonly Stack<SessionSnapshot> _undoStack;
    private readonly Stopwatch _passageTimer;

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

    /// <summary>
    /// Maximum (display:) nesting depth. Mutating mid-render is supported but
    /// only affects subsequent (display:) calls — the active stack frame
    /// continues unaffected. Values &lt; 1 are treated as 1 (a single
    /// (display:) call always succeeds).
    /// </summary>
    public int MaxDisplayDepth { get; set; } = DefaultMaxDisplayDepth;

    /// <summary>Name of the passage currently loaded into the session.</summary>
    public string CurrentPassage => _currentPassage;

    /// <summary>
    /// Builds a session from a parsed story. The session starts at the passage
    /// whose pid matches <see cref="Harlowe.StartNode"/>; call
    /// <see cref="Render"/> to obtain its content.
    /// </summary>
    public StorySession(Harlowe story)
    {
      _story = story;
      _registry = new MacroRegistry();
      StandardMacros.RegisterAll(_registry);
      _store = new HarloweVariableStore();
      _visitCounts = new Dictionary<string, int>();
      _undoStack = new Stack<SessionSnapshot>();
      _passageTimer = Stopwatch.StartNew();

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
    /// Past passage names in visit order, oldest first, excluding the
    /// current passage. Backs the <c>(history:)</c> macro. The undo stack
    /// stores each snapshot's prior passage name, so iterating it
    /// bottom-to-top yields the visit order; <see cref="System.Collections.Generic.Stack{T}.ToArray"/>
    /// returns top-first (LIFO), hence the reverse loop.
    /// </summary>
    public HarloweValue History
    {
      get
      {
        var snaps = _undoStack.ToArray();
        var list = new List<HarloweValue>(snaps.Length);
        for (int i = snaps.Length - 1; i >= 0; i--)
          list.Add(HarloweValue.OfString(snaps[i].PassageName ?? string.Empty));
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
    /// Navigates to <paramref name="passageName"/>, pushes a snapshot of the
    /// current state onto the undo stack, increments the target's visit
    /// count, clears passage-scoped variables, and returns the rendered
    /// result. Any <c>(goto:)</c> macros in the target passage are followed
    /// automatically.
    /// </summary>
    public RenderResult Goto(string passageName)
    {
      _undoStack.Push(new SessionSnapshot
      {
        PassageName = _currentPassage,
        StoreSnapshot = _store.Snapshot(),
        VisitCounts = CopyVisitCounts()
      });
      EnterPassage(passageName);
      return RenderInternal(0);
    }

    /// <summary>
    /// Pops the most recent snapshot from the undo stack and restores its
    /// passage name, variable store, and visit counts. Returns <c>true</c> if
    /// a snapshot was available and applied; <c>false</c> if the stack is
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
      var snap = _undoStack.Pop();
      _currentPassage = snap.PassageName;
      _store.Restore(snap.StoreSnapshot);
      _visitCounts = snap.VisitCounts;
      _liveRoot = null;
      _liveContext = null;
      _passageTimer.Restart();
      return true;
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
        Invoker = _registry
      };
      ctx.RenderPassage = (name, output) => InlineDisplayPassage(name, output, ctx);
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

      // Run registered (enchant:) enchantments over the finished tree. By now
      // every later-declared hook is in the tree and every revision mutation
      // has happened, so the first pass catches everything. The pass is
      // idempotent (disenchant + re-enchant) so DispatchEvent can re-run it
      // after click-driven mutations without double-wrapping.
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

      // Consume — single-use. Remove from the registry before running so a
      // re-entrant dispatch can't double-fire.
      _liveContext.ClickHandlers.Remove(regionId);

      // Unwrap every interactive node with this id so the wrap stops being
      // clickable (and so append/prepend don't leave a stale wrap behind).
      UnwrapInteractive(_liveRoot, regionId);

      // Render the deferred hook into a detached subtree using the live
      // context — so inner (click:)/(enchant:)/(replace:) calls inside the
      // deferred hook register against the live session state.
      var detached = new Rendering.RenderTreeBuilder();
      handler.RenderDeferredHook?.Invoke(detached);
      var source = detached.Root.Children;

      // Splice the source into every node the target re-resolves to right
      // now — the query is fresh, matching Harlowe's "?name is a query" rule.
      var targets = Rendering.HookResolver.Resolve(_liveRoot, handler.Target);
      for (int i = 0; i < targets.Count; i++)
      {
        if (targets[i] is Rendering.IRenderContainer container)
          SpliceInto(container, source, handler.Mode);
      }

      // Re-run the enchantment pass (disenchant + re-enchant — idempotent).
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

    /// <summary>
    /// Walk <paramref name="container"/> and splice out every
    /// <see cref="Rendering.RenderInteractiveNode"/> whose region matches
    /// <paramref name="regionId"/>, replacing each with its children. Also
    /// strips any <see cref="Rendering.RenderStyleNode"/> tagged with the same
    /// region id — those are the composed style layers a changer like
    /// <c>(click-append: ?m) + (text-style: "bold")</c> wraps around the
    /// interactive node, and they need to disappear with the wrap so the
    /// target returns to its pre-interaction styling once the handler fires.
    /// Used by <see cref="DispatchEvent"/> to consume the fired region so it
    /// can't be re-clicked and so the post-splice tree doesn't carry a stale
    /// wrap.
    /// </summary>
    private static void UnwrapInteractive(Rendering.IRenderContainer container, string regionId)
    {
      if (container == null) return;
      var children = container.Children;

      // Recurse first, then rebuild this level — symmetric with the
      // disenchant sweep and the text-occurrence finder.
      for (int i = 0; i < children.Count; i++)
        if (children[i] is Rendering.IRenderContainer c) UnwrapInteractive(c, regionId);

      var rebuilt = new List<Rendering.RenderNode>(children.Count);
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is Rendering.RenderInteractiveNode iv && iv.Region?.Id == regionId)
          rebuilt.AddRange(iv.Children);
        else if (children[i] is Rendering.RenderStyleNode sn && sn.SourceRegionId == regionId)
          rebuilt.AddRange(sn.Children);
        else
          rebuilt.Add(children[i]);
      }
      children.Clear();
      children.AddRange(rebuilt);
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
      public object StoreSnapshot;
      public Dictionary<string, int> VisitCounts;
    }
  }
}
