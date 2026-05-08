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
    private readonly Stack<SessionSnapshot> _undoStack;
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
    /// </summary>
    public bool Undo()
    {
      if (_undoStack.Count == 0) return false;
      var snap = _undoStack.Pop();
      _currentPassage = snap.PassageName;
      _store.Restore(snap.StoreSnapshot);
      _visitCounts = snap.VisitCounts;
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
