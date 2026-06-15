using System;
using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Default <see cref="IVariableStore"/> implementation. Stores story-scoped
  /// variables in a single dictionary, and passage-scoped "temporary"
  /// variables in a stack of scopes — one per active hook/render boundary —
  /// so authors who write <c>(set: _x to ...)</c> inside a hook see the
  /// documented hook-scoped semantics rather than leak-through to the
  /// enclosing passage.
  ///
  /// <para>
  /// Temp scope semantics match reference Harlowe (ts/internaltypes/varscope.ts
  /// for the prototype-chain construction, plus ts/internaltypes/varref.ts —
  /// see its set() parent-walk loop that searches for an existing declaration
  /// up the scope chain). A <see cref="Get"/> walks inner-to-outer through
  /// the stack and returns the first match. A <see cref="Set"/> walks
  /// outer-to-inner looking for an existing declaration: if any scope already
  /// declares the name, the write goes to the OUTERMOST such scope ("inner
  /// hooks can modify outer hooks' values").
  /// If the name is undeclared anywhere, the write goes to the INNERMOST
  /// (current) scope, where it dies on the next <see cref="PushTempScope"/>
  /// pop.
  /// </para>
  ///
  /// <para>
  /// Snapshots deep-copy mutable collections (arrays/datamaps) so a later
  /// mutation to a stored value does not retroactively alter the snapshot.
  /// Primitives (<see cref="HarloweValueKind.Number"/>,
  /// <see cref="HarloweValueKind.String"/>, <see cref="HarloweValueKind.Bool"/>,
  /// <see cref="HarloweValueKind.Error"/>) are immutable, so they are kept by
  /// reference.
  /// </para>
  /// </summary>
  public class HarloweVariableStore : IVariableStore
  {
    private Dictionary<string, HarloweValue> _story = new Dictionary<string, HarloweValue>();
    private List<Dictionary<string, HarloweValue>> _tempStack;
    private HarloweValue _it;
    private HarloweValue _pos;

    // Names of story ($) variables written since the last TakeStoryDelta. Lets
    // the session record one undo point as a per-turn forward delta instead of
    // a full store clone. Only real author mutations (the story branch of Set)
    // are tracked — temp vars and transient lambda bindings bypass Set's story
    // branch, so they never appear in a delta.
    private readonly HashSet<string> _dirtyStoryVars = new HashSet<string>();

    public HarloweVariableStore()
    {
      _tempStack = new List<Dictionary<string, HarloweValue>>();
      _tempStack.Add(new Dictionary<string, HarloweValue>());
    }

    public HarloweValue It => _it;
    public HarloweValue Pos => _pos;

    public HarloweValue Get(string name, bool isTemporary)
    {
      if (!isTemporary)
      {
        if (!_story.TryGetValue(name, out var v)) return null;
        return v;
      }
      // Walk inner-to-outer.
      for (int i = _tempStack.Count - 1; i >= 0; i--)
      {
        if (_tempStack[i].TryGetValue(name, out var v)) return v;
      }
      return null;
    }

    public void Set(string name, bool isTemporary, HarloweValue value)
    {
      if (!isTemporary)
      {
        _story[name] = value;
        _it = value;
        _dirtyStoryVars.Add(name);
        return;
      }
      // Walk outer-to-inner looking for an existing declaration; write to the
      // OUTERMOST one if found (reference: "inner hooks can modify outer
      // hooks' values"). Else write to the innermost (current) scope.
      for (int i = 0; i < _tempStack.Count; i++)
      {
        if (_tempStack[i].ContainsKey(name))
        {
          _tempStack[i][name] = value;
          _it = value;
          return;
        }
      }
      _tempStack[_tempStack.Count - 1][name] = value;
      _it = value;
    }

    public void BeginPassage()
    {
      _tempStack.Clear();
      _tempStack.Add(new Dictionary<string, HarloweValue>());
    }

    public object Snapshot()
    {
      var stackCopy = new List<Dictionary<string, HarloweValue>>(_tempStack.Count);
      for (int i = 0; i < _tempStack.Count; i++) stackCopy.Add(DeepCopyBucket(_tempStack[i]));
      return new Snap
      {
        Story = DeepCopyBucket(_story),
        TempStack = stackCopy,
        It = DeepCopyValue(_it)
      };
    }

    public void Restore(object snapshot)
    {
      if (!(snapshot is Snap snap))
        throw new ArgumentException("Snapshot was not produced by this store.", nameof(snapshot));
      _story = DeepCopyBucket(snap.Story);
      _tempStack = new List<Dictionary<string, HarloweValue>>(snap.TempStack.Count);
      for (int i = 0; i < snap.TempStack.Count; i++) _tempStack.Add(DeepCopyBucket(snap.TempStack[i]));
      if (_tempStack.Count == 0) _tempStack.Add(new Dictionary<string, HarloweValue>());
      _it = DeepCopyValue(snap.It);
    }

    /// <summary>
    /// Captures and clears the forward delta of story (<c>$</c>) variables
    /// changed since the previous call: a deep-copied map of each dirtied
    /// variable's current value. Lets a session record one undo point as a
    /// per-turn delta instead of cloning the whole store. Temp variables and
    /// transient lambda bindings are never included (they don't flow through
    /// <see cref="Set"/>'s story branch). The deep copy keeps the returned
    /// delta independent of later live-store mutations.
    /// </summary>
    public Dictionary<string, HarloweValue> TakeStoryDelta()
    {
      var delta = new Dictionary<string, HarloweValue>(_dirtyStoryVars.Count);
      foreach (var name in _dirtyStoryVars)
      {
        if (_story.TryGetValue(name, out var v)) delta[name] = DeepCopyValue(v);
      }
      _dirtyStoryVars.Clear();
      return delta;
    }

    /// <summary>
    /// Replaces the story (<c>$</c>) namespace with deep copies of
    /// <paramref name="flattened"/> and resets the temp stack to a single fresh
    /// scope — the state-installation half of an undo. The deep copy keeps the
    /// timeline's deltas independent of the live store. Temp variables are
    /// passage-local and cleared here (undo does not re-enter the passage and
    /// the post-undo render does not call <see cref="BeginPassage"/>);
    /// <c>it</c>/<c>pos</c> are left as-is, matching <see cref="BeginPassage"/>.
    /// </summary>
    public void ResetStoryVars(Dictionary<string, HarloweValue> flattened)
    {
      _story = DeepCopyBucket(flattened ?? new Dictionary<string, HarloweValue>());
      _dirtyStoryVars.Clear();
      _tempStack = new List<Dictionary<string, HarloweValue>>();
      _tempStack.Add(new Dictionary<string, HarloweValue>());
    }

    private static Dictionary<string, HarloweValue> DeepCopyBucket(Dictionary<string, HarloweValue> src)
    {
      var dst = new Dictionary<string, HarloweValue>(src.Count);
      foreach (var kv in src) dst[kv.Key] = DeepCopyValue(kv.Value);
      return dst;
    }

    // Ceiling on value-nesting depth copied per snapshot. Real data nests only
    // a few levels; the cap exists so a self-deepening value — e.g.
    // `(set: $a to (a: $a))` re-run each turn — can't overflow the stack during
    // Snapshot/Restore. This path runs at runtime under the no-throw policy, so
    // past the ceiling we stop recursing and alias the remaining subtree rather
    // than throwing; the aliasing is only reachable by already-degenerate
    // nesting and is far better than an uncatchable StackOverflowException.
    private const int MaxCopyDepth = 256;

    private static HarloweValue DeepCopyValue(HarloweValue v) => DeepCopyValue(v, 0);

    private static HarloweValue DeepCopyValue(HarloweValue v, int depth)
    {
      if (v == null) return null;
      if (depth >= MaxCopyDepth) return v;
      switch (v.Kind)
      {
        case HarloweValueKind.Array:
          var srcArr = v.AsArray;
          var dstArr = new List<HarloweValue>(srcArr.Count);
          for (int i = 0; i < srcArr.Count; i++) dstArr.Add(DeepCopyValue(srcArr[i], depth + 1));
          return HarloweValue.OfArray(dstArr);
        case HarloweValueKind.Datamap:
          var srcMap = v.AsDatamap;
          var dstMap = new Dictionary<string, HarloweValue>(srcMap.Count);
          foreach (var kv in srcMap) dstMap[kv.Key] = DeepCopyValue(kv.Value, depth + 1);
          return HarloweValue.OfDatamap(dstMap);
        default:
          return v;
      }
    }

    public IDisposable PushBinding(string name, bool isTemporary, HarloweValue value)
    {
      if (isTemporary)
      {
        // PushBinding shadows in the INNERMOST scope and restores the prior
        // state on dispose — used by lambdas for their single-iteration
        // parameter binding. We deliberately don't walk the parent chain
        // here: lambda parameters are local-by-design and shouldn't write
        // through to an outer-scope binding that coincidentally shares the
        // same name.
        var bucket = _tempStack[_tempStack.Count - 1];
        bool hadPrior = bucket.TryGetValue(name, out var prior);
        bucket[name] = value;
        return new BucketBindingScope(bucket, name, hadPrior, prior);
      }
      else
      {
        bool hadPrior = _story.TryGetValue(name, out var prior);
        _story[name] = value;
        return new BucketBindingScope(_story, name, hadPrior, prior);
      }
    }

    public IDisposable PushItBinding(HarloweValue value)
    {
      var prior = _it;
      _it = value;
      return new ItBindingScope(this, prior);
    }

    public IDisposable PushPosBinding(int oneIndexedPos)
    {
      var prior = _pos;
      _pos = HarloweValue.OfNumber(oneIndexedPos);
      return new PosBindingScope(this, prior);
    }

    public IDisposable PushTempScope()
    {
      _tempStack.Add(new Dictionary<string, HarloweValue>());
      return new TempScopeFrame(this);
    }

    private class ItBindingScope : IDisposable
    {
      private readonly HarloweVariableStore _store;
      private readonly HarloweValue _prior;
      private bool _disposed;

      public ItBindingScope(HarloweVariableStore store, HarloweValue prior)
      {
        _store = store;
        _prior = prior;
      }

      public void Dispose()
      {
        if (_disposed) return;
        _disposed = true;
        _store._it = _prior;
      }
    }

    private class PosBindingScope : IDisposable
    {
      private readonly HarloweVariableStore _store;
      private readonly HarloweValue _prior;
      private bool _disposed;

      public PosBindingScope(HarloweVariableStore store, HarloweValue prior)
      {
        _store = store;
        _prior = prior;
      }

      public void Dispose()
      {
        if (_disposed) return;
        _disposed = true;
        _store._pos = _prior;
      }
    }

    private class BucketBindingScope : IDisposable
    {
      private readonly Dictionary<string, HarloweValue> _bucket;
      private readonly string _name;
      private readonly bool _hadPrior;
      private readonly HarloweValue _prior;
      private bool _disposed;

      public BucketBindingScope(Dictionary<string, HarloweValue> bucket, string name, bool hadPrior, HarloweValue prior)
      {
        _bucket = bucket;
        _name = name;
        _hadPrior = hadPrior;
        _prior = prior;
      }

      public void Dispose()
      {
        if (_disposed) return;
        _disposed = true;
        if (_hadPrior) _bucket[_name] = _prior;
        else _bucket.Remove(_name);
      }
    }

    private class TempScopeFrame : IDisposable
    {
      private readonly HarloweVariableStore _store;
      private bool _disposed;

      public TempScopeFrame(HarloweVariableStore store) { _store = store; }

      public void Dispose()
      {
        if (_disposed) return;
        _disposed = true;
        // Guard against popping the root scope — that would leave the stack
        // empty and break subsequent Sets. Defensive against unbalanced
        // dispose patterns.
        if (_store._tempStack.Count > 1)
          _store._tempStack.RemoveAt(_store._tempStack.Count - 1);
      }
    }

    private class Snap
    {
      public Dictionary<string, HarloweValue> Story;
      public List<Dictionary<string, HarloweValue>> TempStack;
      public HarloweValue It;
    }
  }
}
