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
  /// + ts/internaltypes/varref.ts:941-947). A <see cref="Get"/> walks
  /// inner-to-outer through the stack and returns the first match. A
  /// <see cref="Set"/> walks outer-to-inner looking for an existing
  /// declaration: if any scope already declares the name, the write goes to
  /// the OUTERMOST such scope ("inner hooks can modify outer hooks' values").
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

    public HarloweVariableStore()
    {
      _tempStack = new List<Dictionary<string, HarloweValue>>();
      _tempStack.Add(new Dictionary<string, HarloweValue>());
    }

    public HarloweValue It => _it;

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

    private static Dictionary<string, HarloweValue> DeepCopyBucket(Dictionary<string, HarloweValue> src)
    {
      var dst = new Dictionary<string, HarloweValue>(src.Count);
      foreach (var kv in src) dst[kv.Key] = DeepCopyValue(kv.Value);
      return dst;
    }

    private static HarloweValue DeepCopyValue(HarloweValue v)
    {
      if (v == null) return null;
      switch (v.Kind)
      {
        case HarloweValueKind.Array:
          var srcArr = v.AsArray;
          var dstArr = new List<HarloweValue>(srcArr.Count);
          for (int i = 0; i < srcArr.Count; i++) dstArr.Add(DeepCopyValue(srcArr[i]));
          return HarloweValue.OfArray(dstArr);
        case HarloweValueKind.Datamap:
          var srcMap = v.AsDatamap;
          var dstMap = new Dictionary<string, HarloweValue>(srcMap.Count);
          foreach (var kv in srcMap) dstMap[kv.Key] = DeepCopyValue(kv.Value);
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
