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
  /// <b>Undo.</b> Single-step in v1: only the state immediately before the
  /// most recent <see cref="Goto"/> call can be restored. After
  /// <see cref="Undo"/> returns <c>true</c>, call <see cref="Render"/> to
  /// redisplay the restored passage. A second <see cref="Undo"/> call (with no
  /// intervening <see cref="Goto"/>) returns <c>false</c>.
  /// </para>
  ///
  /// <para>
  /// <b>Display.</b> <c>(display:)</c> macros inline-render another passage
  /// using the current variable store and macro context. The inlined passage's
  /// text is returned as a string value; it does not affect navigation state
  /// or visit counts.
  /// </para>
  /// </summary>
  public class StorySession : IEvaluationContext
  {
    private readonly Harlowe _story;
    private readonly MacroRegistry _registry;
    private readonly HarloweVariableStore _store;
    private string _currentPassage;
    private Dictionary<string, int> _visitCounts;
    private SessionSnapshot _undoSnapshot;
    private readonly Stopwatch _passageTimer;

    private const int MaxGotoDepth = 20;

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

    /// <summary>Datamap describing the current passage. Contains at least <c>name</c>.</summary>
    public HarloweValue Passage
    {
      get
      {
        var map = new Dictionary<string, HarloweValue>();
        map["name"] = HarloweValue.OfString(_currentPassage ?? string.Empty);
        return HarloweValue.OfDatamap(map);
      }
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
    /// Navigates to <paramref name="passageName"/>, snapshots the current
    /// state for <see cref="Undo"/>, increments the target's visit count,
    /// clears passage-scoped variables, and returns the rendered result.
    /// Overwrites the previous undo snapshot (v1 is single-step). Any
    /// <c>(goto:)</c> macros in the target passage are followed automatically.
    /// </summary>
    public RenderResult Goto(string passageName)
    {
      _undoSnapshot = new SessionSnapshot
      {
        PassageName = _currentPassage,
        StoreSnapshot = _store.Snapshot(),
        VisitCounts = CopyVisitCounts()
      };
      EnterPassage(passageName);
      return RenderInternal(0);
    }

    /// <summary>
    /// Restores the state captured immediately before the most recent
    /// <see cref="Goto"/> call: passage name, variable store, and visit counts.
    /// Returns <c>true</c> if the snapshot was available and applied;
    /// <c>false</c> if there is nothing to undo (no prior <see cref="Goto"/>,
    /// or already undone once). After returning <c>true</c>, call
    /// <see cref="Render"/> to display the restored passage.
    /// </summary>
    public bool Undo()
    {
      if (_undoSnapshot == null) return false;
      _currentPassage = _undoSnapshot.PassageName;
      _store.Restore(_undoSnapshot.StoreSnapshot);
      _visitCounts = _undoSnapshot.VisitCounts;
      _undoSnapshot = null;
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
      ctx.RenderPassage = name => InlineDisplayPassage(name, ctx);
      _registry.Context = ctx;

      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, _registry, ctx).Render(passage.Ast);

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

      return new RenderResult
      {
        PassageName = _currentPassage,
        Text = buf.Text,
        Entries = buf.Entries
      };
    }

    private HarloweValue InlineDisplayPassage(string name, MacroContext ctx)
    {
      var passage = _story.GetPassage(name);
      if (passage == null) return HarloweValue.OfError($"passage '{name}' not found");

      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, _registry, ctx).Render(passage.Ast);
      return HarloweValue.OfString(buf.Text);
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
