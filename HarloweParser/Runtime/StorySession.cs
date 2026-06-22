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
  /// <b>Undo/redo.</b> Multi-step: every <see cref="Goto"/> finalises the live
  /// turn into the past timeline (and clears the redo future); <see cref="Undo"/>
  /// moves the present back one turn (onto the future) and restores its passage,
  /// variable store, and visit counts, while <see cref="Redo"/> re-applies the
  /// most recently undone turn. After either returns <c>true</c>, call
  /// <see cref="Render"/> to redisplay. <see cref="Undo"/> returns <c>false</c>
  /// at the first turn; <see cref="Redo"/> returns <c>false</c> with nothing
  /// undone. The timeline is unbounded.
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
    private readonly Saving.ISaveStorage _storage;
    private readonly HarloweVariableStore _store;
    private string _currentPassage;
    // Timeline: completed turns (_past), the live turn (_present), and undone
    // turns available to redo (_future). Goto finalises _present into _past and
    // clears _future; Undo/Redo move the present between _past and _future.
    private readonly List<Moment> _past;
    private Moment _present;
    private readonly List<Moment> _future;
    private readonly Stopwatch _passageTimer;

    // One RNG for the whole session, threaded into every MacroContext so every
    // (random:)/(either:) draw forms a single continuous stream (a fresh instance
    // per render leg would restart the stream). Its (Seed, SeedIter) state is the
    // serialisable RNG position the save model records; undo/redo restore it to
    // the restored turn's start (see RestoreRng) so a re-render reproduces that
    // turn's draws.
    private readonly IRng _rng;

    // RNG position at the start of the live turn — lets FinalizePresent tell
    // whether the present turn drew (and so whether to record its SeedIter).
    private int _turnStartIter;

    // True when the live store/RNG sit at the present turn's START (set by undo/redo
    // restore, cleared once a render rebuilds the turn's end-state). Lets Goto tell
    // whether an undo/redo happened with no intervening render and must therefore
    // rebuild the previous turn's end-state before starting a new turn.
    private bool _liveAtTurnStart;

    // When restoring into a multi-passage redirect turn, the entry passage the next
    // render replays from (Moment.Visits[0]); null otherwise. Kept separate from
    // _currentPassage so CurrentPassage still reports the turn's resting passage
    // between Undo() and the replay Render().
    private string _replayFrom;

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
    public StorySession(Harlowe story) : this(story, new MulberryRng(), new Saving.InMemorySaveStorage()) { }

    /// <summary>
    /// Builds a session with a fixed RNG seed, so <c>(random:)</c>/<c>(either:)</c>
    /// produce a reproducible sequence across the whole session — useful for
    /// tests and replays.
    /// </summary>
    public StorySession(Harlowe story, int seed) : this(story, new MulberryRng(seed), new Saving.InMemorySaveStorage()) { }

    /// <summary>
    /// Builds a session with a host-supplied save backend. Pass <c>null</c> to
    /// disable saving (<c>(save-game:)</c> then returns false); the storage-free
    /// constructors use a session-lifetime <see cref="Saving.InMemorySaveStorage"/>.
    /// </summary>
    public StorySession(Harlowe story, Saving.ISaveStorage storage) : this(story, new MulberryRng(), storage) { }

    /// <summary>Seeded session with a host-supplied save backend (pass null to disable saving).</summary>
    public StorySession(Harlowe story, int seed, Saving.ISaveStorage storage) : this(story, new MulberryRng(seed), storage) { }

    private StorySession(Harlowe story, IRng rng, Saving.ISaveStorage storage)
    {
      _story = story;
      _storage = storage;
      _registry = new MacroRegistry();
      StandardMacros.RegisterAll(_registry);
      _store = new HarloweVariableStore();
      _past = new List<Moment>();
      _future = new List<Moment>();
      _passageTimer = Stopwatch.StartNew();
      _rng = rng;

      var startPassage = story.GetStartPassage();
      var startName = startPassage != null ? startPassage.Name : string.Empty;
      // The first Moment carries the session's initial seed — the baseline the
      // save model and undo/redo reconstruct the RNG from.
      _present = new Moment { PassageName = startName, Seed = _rng.Seed };
      _turnStartIter = _rng.SeedIter;
      EnterPassage(startName);
    }

    // IEvaluationContext ---------------------------------------------------

    /// <summary>Milliseconds elapsed since the current passage was entered.</summary>
    public HarloweValue Time => HarloweValue.OfNumber(_passageTimer.ElapsedMilliseconds);

    /// <summary>
    /// How many times the current passage has been entered this session, including
    /// the current visit. Derived by counting the passage across every turn's
    /// entered sequence (the resting passage, or the full redirect trail) — the
    /// present plus all past turns.
    /// </summary>
    public HarloweValue Visits
    {
      get
      {
        if (string.IsNullOrEmpty(_currentPassage)) return HarloweValue.OfNumber(0);
        int count = CountEntered(_present, _currentPassage);
        for (int i = 0; i < _past.Count; i++) count += CountEntered(_past[i], _currentPassage);
        return HarloweValue.OfNumber(count);
      }
    }

    /// <summary>
    /// Total number of turns this session, counting the current one. <c>_past</c>
    /// holds one Moment per completed turn (and excludes the redo <c>_future</c>),
    /// so the count plus the current-turn-is-live bit gives the total — it drops
    /// after an <see cref="Undo"/>.
    /// </summary>
    public HarloweValue Turns =>
      HarloweValue.OfNumber(_past.Count + (string.IsNullOrEmpty(_currentPassage) ? 0 : 1));

    /// <summary>
    /// Passage names in visit order, oldest first, excluding the current passage.
    /// Backs the <c>(history:)</c> macro. Derived by flattening every turn's entered
    /// sequence — each turn contributes its resting passage, or its full
    /// auto-<c>(goto:)</c> redirect trail — across <c>_past</c> then the present,
    /// then dropping the final entry (the current passage).
    /// </summary>
    public HarloweValue History
    {
      get
      {
        var all = new List<string>();
        for (int i = 0; i < _past.Count; i++) AppendEntered(all, _past[i]);
        AppendEntered(all, _present);
        if (all.Count > 0) all.RemoveAt(all.Count - 1);   // exclude the current passage
        var list = new List<HarloweValue>(all.Count);
        for (int i = 0; i < all.Count; i++) list.Add(HarloweValue.OfString(all[i]));
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
      FinalizePresent();
      _past.Add(_present);
      _future.Clear();
      _present = new Moment { PassageName = passageName };
      // A fresh forward turn never replays from a restored entry. Clear any pending
      // replay marker (set by an undo/redo into a redirect turn) so it can't hijack
      // this turn's render when no render intervened between the undo/redo and here.
      _replayFrom = null;
      // A new turn starts where the previous one *ended*. Normally the live store
      // and RNG already sit there (the previous turn just rendered), so we only note
      // the RNG position. But after an undo/redo with no intervening render the live
      // state still sits at the restored turn's *start* — rebuild it to the previous
      // turn's end first, or the new turn starts from stale state and disagrees with
      // what a later undo reconstructs. FlattenStore() == flatten(_past), the
      // start-of-present-turn state, which for the new (empty) present is exactly the
      // previous turn's end.
      if (_liveAtTurnStart)
      {
        _store.ResetStoryVars(FlattenStore());
        RestoreRng();
      }
      else
      {
        _turnStartIter = _rng.SeedIter;
      }
      EnterPassage(passageName);
      return RenderInternal(0);
    }

    /// <summary>
    /// Moves the present back one turn: finalises the live turn onto the redo
    /// future, pops the most recent past Moment as the new present, and restores
    /// its passage name, story-variable state (reconstructed from the per-turn
    /// deltas), and visit counts. Returns <c>true</c> if a past turn was available;
    /// <c>false</c> at the first turn. After <c>true</c>, call <see cref="Render"/>
    /// to display the restored passage. May be called repeatedly to walk back
    /// through every <see cref="Goto"/>; <see cref="Redo"/> walks forward again.
    ///
    /// <para>
    /// The live render tree, click-handler registry, and enchantment list are
    /// torn down — they belonged to the turn we left, not the one we're returning
    /// to. A <see cref="DispatchEvent"/> call made between <see cref="Undo"/> and
    /// the next <see cref="Render"/> is a no-op rather than firing handlers against
    /// a stale tree. The next <see cref="Render"/> rebuilds both the tree and the
    /// handler/enchantment state from passage source, so anything the restored
    /// passage's <c>(click:)</c>/<c>(enchant:)</c> macros register gets
    /// re-registered fresh.
    /// </para>
    /// </summary>
    public bool Undo() => Move(_past, _future);

    /// <summary>Alias for <see cref="Undo"/> (reference Harlowe's <c>rewind</c>).</summary>
    public bool Rewind() => Undo();

    /// <summary>
    /// Re-applies the most recently undone turn: finalises the present back into
    /// <c>_past</c>, pops the last <c>_future</c> Moment as the new present, and
    /// restores its passage and variable state. Returns <c>false</c> when there is
    /// nothing to redo — a <see cref="Goto"/> clears the redo future, so redo is
    /// available only immediately after one or more <see cref="Undo"/>s. After
    /// <c>true</c>, call <see cref="Render"/> to redisplay; the live tree is rebuilt
    /// on that render exactly as for undo.
    /// </summary>
    public bool Redo() => Move(_future, _past);

    /// <summary>Alias for <see cref="Redo"/> (reference Harlowe's <c>fastForward</c>).</summary>
    public bool FastForward() => Redo();

    // ----- Save / load -----

    /// <summary>
    /// Why the most recent <see cref="SaveGame"/> failed, or null if it succeeded —
    /// so a host (or <c>(save-game:)</c>) can report a no-backend / unsaveable-value /
    /// write-rejected reason rather than a bare false.
    /// </summary>
    public string LastSaveError { get; private set; }

    /// <summary>Why the most recent <see cref="LoadGame"/> failed (missing slot, corrupt blob, vanished passage, …), or null on success.</summary>
    public string LastLoadError { get; private set; }

    /// <summary>
    /// Serialise the current timeline (completed turns + the live turn, excluding the
    /// redo future) to the configured <see cref="Saving.ISaveStorage"/> under
    /// <paramref name="slot"/>, with an optional display <paramref name="filename"/>
    /// (defaulting to the slot name). The slot is IFID-namespaced internally
    /// (<see cref="Saving.SaveKeys"/>). Returns false — setting
    /// <see cref="LastSaveError"/> — if saving is disabled (null backend), the state
    /// holds an unsaveable value (a non-finite number, an unstamped changer), or the
    /// backend refuses the write.
    ///
    /// <para><b>Fidelity boundary.</b> The live present turn is saved by passage +
    /// timeline; its variable state is rebuilt by <em>re-rendering</em> that passage
    /// on load (start-of-turn store + replay — the same model as undo). Top-level
    /// <c>(set:)</c>s reproduce exactly, but a variable changed by a click/hover
    /// interaction on the current passage (a dispatch, with no navigation) is
    /// <em>not</em> recaptured — re-render rebuilds the interaction but doesn't
    /// re-fire it. So a save taken after such an in-place choice loses that change.
    /// This is the delta+replay timeline model's trade-off (reference's full-variable
    /// snapshot would capture it); navigating before saving finalises the turn into
    /// the timeline and persists its effect. The in-passage <c>(save-game:)</c> macro
    /// is largely unaffected, since clicks fire after the render.</para>
    /// </summary>
    public bool SaveGame(string slot, string filename = null)
    {
      LastSaveError = null;
      if (_storage == null) { LastSaveError = "saving is disabled (no storage backend)"; return false; }

      string blob = Saving.SaveSerializer.SerialiseTimeline(_past, _present);
      if (blob == null) { LastSaveError = "the story state holds a value that can't be saved"; return false; }

      string key = Saving.SaveKeys.ToStorageKey(_story?.Ifid, slot);
      if (!_storage.TryWrite(key, blob, filename ?? slot))
      { LastSaveError = "the storage backend rejected the save"; return false; }
      return true;
    }

    /// <summary>
    /// Restore the timeline saved under <paramref name="slot"/>, replacing the current
    /// timeline and positioning the session at the saved present turn (the next
    /// <see cref="Render"/> shows it). Returns false — setting
    /// <see cref="LastLoadError"/> and leaving session state untouched — if saving is
    /// disabled, the slot is empty, or the blob is corrupt / references a passage that
    /// no longer exists (deserialisation is atomic). Story variables and the RNG
    /// restore to the loaded present's start so the re-render reproduces it.
    ///
    /// <para>This is the host-facing load; the <c>(load-game:)</c> macro layers the
    /// in-passage navigation + infinite-loop guard atop the same install.</para>
    /// </summary>
    public bool LoadGame(string slot)
    {
      LastLoadError = null;
      if (_storage == null) { LastLoadError = "saving is disabled (no storage backend)"; return false; }

      string key = Saving.SaveKeys.ToStorageKey(_story?.Ifid, slot);
      if (!_storage.TryRead(key, out var blob))
      { LastLoadError = $"there is no saved game in slot '{slot}'"; return false; }

      var result = Saving.SaveSerializer.DeserialiseTimeline(blob, _story, _registry, NewSaveContext());
      if (!result.Ok) { LastLoadError = result.Error; return false; }

      InstallTimeline(result.Past, result.Present);
      return true;
    }

    /// <summary>
    /// The saved games for this story as slot → display filename. Filters the
    /// backend's contents to this story's IFID namespace and strips the prefix, so a
    /// backend shared across stories lists only this one's saves. Backs
    /// <c>(saved-games:)</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> SavedGames()
    {
      var games = new Dictionary<string, string>();
      if (_storage == null) return games;
      string ifid = _story?.Ifid;
      foreach (var info in _storage.Enumerate())
        if (Saving.SaveKeys.TryGetSlot(ifid, info.Key, out var slot))
          games[slot] = info.Filename;
      return games;
    }

    /// <summary>
    /// Install a freshly-loaded timeline and position the session at its present turn,
    /// reusing the undo/redo restore path (<see cref="RestoreToPresent"/>) to rebuild
    /// the store, RNG, and passage from the loaded moments. Shared by
    /// <see cref="LoadGame"/> and (later) the <c>(load-game:)</c> macro.
    /// </summary>
    private void InstallTimeline(List<Moment> past, Moment present)
    {
      _past.Clear();
      if (past != null) _past.AddRange(past);
      _future.Clear();
      _present = present;
      RestoreToPresent();
    }

    /// <summary>A context for re-evaluating saved value source during a load (deserialise dispatches collection/changer macros through the registry).</summary>
    private MacroContext NewSaveContext()
      => new MacroContext { Store = _store, EvaluationContext = this, Invoker = _registry, Rng = _rng };

    /// <summary>
    /// Shared body of <see cref="Undo"/>/<see cref="Redo"/>: finalise the live
    /// turn, move it onto <paramref name="to"/>, pop the most recent
    /// <paramref name="from"/> Moment as the new present, and restore its state.
    /// Returns <c>false</c> when <paramref name="from"/> is empty. Kept as one body
    /// so undo and redo cannot silently drift apart as the timeline grows.
    /// </summary>
    private bool Move(List<Moment> from, List<Moment> to)
    {
      if (from.Count == 0) return false;
      FinalizePresent();
      to.Add(_present);
      _present = from[from.Count - 1];
      from.RemoveAt(from.Count - 1);
      RestoreToPresent();
      return true;
    }

    /// <summary>
    /// Captures a freshly-played turn's state into <c>_present</c> before it leaves
    /// the present slot: resting passage, the forward delta of story vars changed
    /// this turn (<see cref="HarloweVariableStore.TakeStoryDelta"/>), and the visit
    /// counts. <b>Only a live turn is captured.</b> A Moment restored from the
    /// timeline by <see cref="Move"/> already holds its real delta and is treated
    /// as immutable: re-capturing it would read the dirty-set that
    /// <see cref="RestoreToPresent"/> just cleared and overwrite the delta with an
    /// empty one, losing that turn's variables on a later undo/redo. A non-null
    /// <see cref="Moment.StoreDelta"/> marks an already-finalised Moment (a fresh
    /// turn starts null), so it doubles as the "is this turn live?" signal. (The
    /// redirect trail is built during the render, not here; visit counts are
    /// derived from it rather than snapshotted.)
    /// </summary>
    private void FinalizePresent()
    {
      if (_present.StoreDelta != null) return;
      _present.PassageName = _currentPassage;
      _present.StoreDelta = _store.TakeStoryDelta();
      // Sparse RNG recording: stamp SeedIter only if the turn actually drew, so a
      // no-draw turn stays compressible (SeedIter null). The shared stream's live
      // position is this turn's end, since its draws are the most recent.
      if (_rng.SeedIter != _turnStartIter)
        _present.SeedIter = _rng.SeedIter;
    }

    /// <summary>
    /// Installs the Moment now in the present slot after a <see cref="Move"/>:
    /// reconstructs the story-var state and RNG to the restored turn's <em>start</em>
    /// (<see cref="FlattenStore"/> / <see cref="RestoreRng"/>), sets the passage to
    /// the turn's resting passage, and tears down the live tree (it belonged to the
    /// turn we left). The next <see cref="Render"/> rebuilds the end-state by
    /// re-running the turn — re-applying its <c>(set:)</c>s and reproducing its draws
    /// exactly once (so an accumulating set like <c>$x to $x + 1</c> isn't doubled).
    ///
    /// <para>The replay starts from <see cref="_replayFrom"/>: for a multi-passage
    /// (auto-<c>(goto:)</c> redirect) turn that's the entry passage
    /// (<see cref="Moment.Visits"/>[0]) so the whole chain replays; for a
    /// single-passage turn it's null and the render runs the resting passage
    /// directly. <see cref="CurrentPassage"/> stays the resting passage throughout,
    /// even before the replay render.</para>
    /// </summary>
    private void RestoreToPresent()
    {
      _store.ResetStoryVars(FlattenStore());   // start-of-turn store; the replay re-applies the turn's sets
      RestoreRng();                            // start-of-turn RNG; the replay reproduces the turn's draws
      _currentPassage = _present.PassageName;
      _replayFrom = _present.Visits != null && _present.Visits.Count > 0
        ? _present.Visits[0]
        : null;
      _liveAtTurnStart = true;
      _liveRoot = null;
      _liveContext = null;
      _passageTimer.Restart();
    }

    /// <summary>
    /// Positions the RNG at the <em>start</em> of the turn now in the present slot
    /// — whether freshly entered (a <see cref="Goto"/>, whose new turn starts where
    /// the previous one ended) or restored by undo/redo (so a re-render reproduces
    /// that turn's draws instead of advancing past them). <see cref="Moment.SeedIter"/>
    /// is taken from the most recent turn recorded <em>strictly before</em> the
    /// present (scanning <c>_past</c>; <c>0</c> when none drew) — i.e. the end of the
    /// previous drawing turn. <see cref="Moment.Seed"/> is the most recent recorded
    /// <em>at or before</em> the present (inclusive), which for this slice is always
    /// the session-initial seed the first Moment holds. (Using the present's own
    /// SeedIter — its end — would re-roll differently; the seed boundary is inclusive
    /// so restoring to the first turn still finds the baseline seed.) Multi-passage
    /// redirect turns reproduce too: <see cref="RestoreToPresent"/> restores to the
    /// entry passage, so the whole chain replays from this start position.
    /// </summary>
    private void RestoreRng()
    {
      int seedIter = 0;
      for (int i = _past.Count - 1; i >= 0; i--)
        if (_past[i].SeedIter.HasValue) { seedIter = _past[i].SeedIter.Value; break; }

      // The first Moment always carries the session-initial seed and is always in
      // scope, so this resolves; SetSeed tolerates a null seed defensively anyway.
      string seed = _present.Seed;
      if (seed == null)
        for (int i = _past.Count - 1; i >= 0; i--)
          if (_past[i].Seed != null) { seed = _past[i].Seed; break; }

      _rng.SetSeed(seed, seedIter);
      _turnStartIter = _rng.SeedIter;
    }

    /// <summary>
    /// Reconstructs the story-variable state at the <em>start</em> of the present
    /// turn: the cumulative effect of every <em>past</em> turn's forward delta, in
    /// chronological order, last write winning. The present turn's own delta is
    /// <em>excluded</em> — the re-render re-runs that turn's <c>(set:)</c>s, so
    /// including it would double a relative set (<c>$x to $x + 1</c>) on undo. For a
    /// freshly-created present (a new <see cref="Goto"/> turn, delta still null) this
    /// is exactly the previous turn's end. (The redo <c>_future</c> is excluded; it
    /// lies ahead of the present.) The returned dictionary holds references into the
    /// deltas; the caller deep-copies on install.
    /// </summary>
    private Dictionary<string, HarloweValue> FlattenStore()
    {
      var flat = new Dictionary<string, HarloweValue>();
      for (int j = 0; j < _past.Count; j++)
      {
        var delta = _past[j].StoreDelta;
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
        // Rebuild the turn's redirect trail from scratch on each top-level render:
        // a re-render (e.g. replaying a restored turn from its entry passage) must
        // not append to the existing trail. The redirect path below re-populates it.
        _present.Visits = null;
        // A replay of a restored turn starts from its entry passage; redirects then
        // walk _currentPassage on to the resting passage. Consume the marker here.
        if (_replayFrom != null) { _currentPassage = _replayFrom; _replayFrom = null; }
        // From here the render rebuilds the present turn's end-state, so the live
        // store/RNG are no longer at the turn start.
        _liveAtTurnStart = false;
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
        RecordRedirect(ctx.PendingGoto);
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
          Rendering.RenderNodes.Splice(container, source, handler.Mode);
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

    /// <summary>
    /// Records one auto-<c>(goto:)</c> redirect into the present turn's
    /// <see cref="Moment.Visits"/> trail — the ordered passages entered this turn.
    /// The first redirect seeds the trail with the departing (entry) passage before
    /// appending the target, so <c>Visits</c> reads entry-first and ends at the
    /// resting passage. Null while the turn is single-passage, keeping such a Moment
    /// compressible in a save blob.
    /// </summary>
    private void RecordRedirect(string target)
    {
      if (_present.Visits == null) _present.Visits = new List<string> { _currentPassage };
      _present.Visits.Add(target);
    }

    /// <summary>Appends a Moment's entered sequence — its <see cref="Moment.Visits"/> trail, or just its resting <see cref="Moment.PassageName"/> when single-passage — to <paramref name="dst"/>. Backs the derived <c>(history:)</c>.</summary>
    private static void AppendEntered(List<string> dst, Moment m)
    {
      if (m.Visits != null) dst.AddRange(m.Visits);
      else dst.Add(m.PassageName ?? string.Empty);
    }

    /// <summary>Counts how many times <paramref name="name"/> appears in a Moment's entered sequence. Backs the derived <c>visits</c> keyword.</summary>
    private static int CountEntered(Moment m, string name)
    {
      if (m.Visits != null)
      {
        int c = 0;
        for (int i = 0; i < m.Visits.Count; i++) if (m.Visits[i] == name) c++;
        return c;
      }
      return (m.PassageName ?? string.Empty) == name ? 1 : 0;
    }

  }
}
