using System;
using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Default <see cref="IVariableStore"/> implementation. Stores story- and
  /// passage-scoped variables in two separate dictionaries so the temporary
  /// namespace can be cleared cheaply at every passage boundary.
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
    private Dictionary<string, HarloweValue> _temp = new Dictionary<string, HarloweValue>();
    private HarloweValue _it;

    public HarloweValue It => _it;

    public HarloweValue Get(string name, bool isTemporary)
    {
      var bucket = isTemporary ? _temp : _story;
      if (!bucket.TryGetValue(name, out var value)) return null;
      return value;
    }

    public void Set(string name, bool isTemporary, HarloweValue value)
    {
      var bucket = isTemporary ? _temp : _story;
      bucket[name] = value;
      _it = value;
    }

    public void BeginPassage()
    {
      _temp.Clear();
    }

    public object Snapshot()
    {
      return new Snap
      {
        Story = DeepCopyBucket(_story),
        Temp = DeepCopyBucket(_temp),
        It = DeepCopyValue(_it)
      };
    }

    public void Restore(object snapshot)
    {
      if (!(snapshot is Snap snap))
        throw new ArgumentException("Snapshot was not produced by this store.", nameof(snapshot));
      _story = DeepCopyBucket(snap.Story);
      _temp = DeepCopyBucket(snap.Temp);
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
      var bucket = isTemporary ? _temp : _story;
      bool hadPrior = bucket.TryGetValue(name, out var prior);
      bucket[name] = value;
      return new BucketBindingScope(bucket, name, hadPrior, prior);
    }

    public IDisposable PushItBinding(HarloweValue value)
    {
      var prior = _it;
      _it = value;
      return new ItBindingScope(this, prior);
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

    private class Snap
    {
      public Dictionary<string, HarloweValue> Story;
      public Dictionary<string, HarloweValue> Temp;
      public HarloweValue It;
    }
  }
}
