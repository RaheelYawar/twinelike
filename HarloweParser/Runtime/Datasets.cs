using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// The list operations behind <c>(ds:)</c> values. A dataset is a list in the
  /// order its values were added, because that's the order it prints in.
  /// Spreading it with <c>...</c> sorts it instead, as reference's
  /// <c>spreadSet</c> does.
  /// </summary>
  internal static class Datasets
  {
    /// <summary>
    /// Finds values by <c>is</c> through a hash of their contents, so building or
    /// subtracting a large dataset isn't quadratic. Reference is, and locks up on
    /// a large one.
    /// </summary>
    private sealed class Membership
    {
      private readonly Dictionary<int, List<HarloweValue>> _buckets = new Dictionary<int, List<HarloweValue>>();

      public bool Contains(HarloweValue value)
      {
        if (!_buckets.TryGetValue(ContentHash(value, 0), out var bucket)) return false;
        for (int i = 0; i < bucket.Count; i++)
          if (bucket[i].Equals(value)) return true;
        return false;
      }

      public void Add(HarloweValue value)
      {
        int hash = ContentHash(value, 0);
        if (!_buckets.TryGetValue(hash, out var bucket))
        {
          bucket = new List<HarloweValue>(1);
          _buckets[hash] = bucket;
        }
        bucket.Add(value);
      }
    }

    /// <summary>The nesting a content hash looks through. Values that differ only deeper share a bucket.</summary>
    private const int MaxHashDepth = 16;

    /// <summary>
    /// A hash that's equal for values that are <c>is</c>-equal and, unlike
    /// <see cref="HarloweValue.GetHashCode"/>, reads everything inside a
    /// collection, because a dataset can hold thousands of similar ones.
    /// </summary>
    private static int ContentHash(HarloweValue value, int depth)
    {
      switch (value.Kind)
      {
        case HarloweValueKind.Number:
          double number = value.AsNumber;
          return number == 0 ? 0 : number.GetHashCode();
        case HarloweValueKind.Array:
        {
          var items = value.AsArray;
          int hash = (int)value.Kind * 397 ^ items.Count;
          if (depth >= MaxHashDepth) return hash;
          for (int i = 0; i < items.Count; i++) hash = unchecked(hash * 31 + ContentHash(items[i], depth + 1));
          return hash;
        }
        case HarloweValueKind.Dataset:
        {
          var items = value.AsDataset;
          int hash = (int)value.Kind * 397 ^ items.Count;
          if (depth >= MaxHashDepth) return hash;
          // Added up, because a dataset's order doesn't count either.
          for (int i = 0; i < items.Count; i++) hash = unchecked(hash + ContentHash(items[i], depth + 1) * 31);
          return hash;
        }
        case HarloweValueKind.Datamap:
        {
          var map = value.AsDatamap;
          int hash = (int)value.Kind * 397 ^ map.Count;
          if (depth >= MaxHashDepth) return hash;
          // Added up, because a datamap's order doesn't count.
          foreach (var entry in map)
            hash = unchecked(hash + (entry.Key.GetHashCode() * 397 ^ ContentHash(entry.Value, depth + 1)));
          return hash;
        }
        default:
          return value.GetHashCode();
      }
    }

    /// <summary>
    /// Keeps the first of each value, in order: reference's <c>filter(unique)</c>.
    /// NaN is dropped entirely, since it isn't equal even to itself and so never
    /// finds its own position there.
    /// </summary>
    internal static List<HarloweValue> Unique(List<HarloweValue> items)
    {
      var seen = new Membership();
      var kept = new List<HarloweValue>(items.Count);
      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];
        if (item.Kind == HarloweValueKind.Number && double.IsNaN(item.AsNumber)) continue;
        if (seen.Contains(item)) continue;
        seen.Add(item);
        kept.Add(item);
      }
      return kept;
    }

    /// <summary>The union of two datasets: the left's values, then any new ones from the right.</summary>
    internal static HarloweValue Union(HarloweValue left, HarloweValue right)
    {
      var all = new List<HarloweValue>(left.AsDataset.Count + right.AsDataset.Count);
      all.AddRange(left.AsDataset);
      all.AddRange(right.AsDataset);
      return HarloweValue.OfDataset(all);
    }

    /// <summary>The left dataset's values that aren't in the right one, in order.</summary>
    internal static HarloweValue Difference(HarloweValue left, HarloweValue right)
    {
      var remove = new Membership();
      var removing = right.AsDataset;
      for (int i = 0; i < removing.Count; i++) remove.Add(removing[i]);
      var source = left.AsDataset;
      var kept = new List<HarloweValue>(source.Count);
      for (int i = 0; i < source.Count; i++)
        if (!remove.Contains(source[i])) kept.Add(source[i]);
      return HarloweValue.OfUniqueDataset(kept);
    }

    /// <summary>
    /// The values of a dataset grouped by content hash, for <c>is</c> to search
    /// without comparing every pair.
    /// </summary>
    internal static Dictionary<int, List<HarloweValue>> Buckets(List<HarloweValue> items)
    {
      var buckets = new Dictionary<int, List<HarloweValue>>(items.Count);
      for (int i = 0; i < items.Count; i++)
      {
        int hash = ContentHash(items[i], 0);
        if (!buckets.TryGetValue(hash, out var bucket))
        {
          bucket = new List<HarloweValue>(1);
          buckets[hash] = bucket;
        }
        bucket.Add(items[i]);
      }
      return buckets;
    }

    /// <summary>The bucket a value would be in under <see cref="Buckets"/>, or null.</summary>
    internal static List<HarloweValue> BucketFor(Dictionary<int, List<HarloweValue>> buckets, HarloweValue value)
      => buckets.TryGetValue(ContentHash(value, 0), out var bucket) ? bucket : null;

    /// <summary>
    /// True when a datatype or typed variable sits anywhere inside these values.
    /// Without one, <c>matches</c> between them is the same as <c>is</c>.
    /// </summary>
    internal static bool HoldsDatatype(List<HarloweValue> items) => HoldsDatatype(items, 0);

    private static bool HoldsDatatype(List<HarloweValue> items, int depth)
    {
      for (int i = 0; i < items.Count; i++)
        if (HoldsDatatype(items[i], depth)) return true;
      return false;
    }

    private static bool HoldsDatatype(HarloweValue value, int depth)
    {
      // Past this, the comparison itself refuses to go any deeper.
      if (depth >= MaxKeyDepth) return false;
      switch (value.Kind)
      {
        case HarloweValueKind.Datatype:
        case HarloweValueKind.TypedVar:
          return true;
        case HarloweValueKind.Array:
        case HarloweValueKind.Dataset:
          return HoldsDatatype((List<HarloweValue>)value.Raw, depth + 1);
        case HarloweValueKind.Datamap:
          foreach (var entry in value.AsDatamap)
            if (HoldsDatatype(entry.Value, depth + 1)) return true;
          return false;
        default:
          return false;
      }
    }

    /// <summary>True when two lists of unique values hold the same values, in any order.</summary>
    internal static bool SameMembers(List<HarloweValue> left, List<HarloweValue> right)
    {
      if (left.Count != right.Count) return false;
      var members = new Membership();
      for (int i = 0; i < right.Count; i++) members.Add(right[i]);
      for (int i = 0; i < left.Count; i++)
        if (!members.Contains(left[i])) return false;
      return true;
    }

    /// <summary>The order a dataset spreads into, reference's <c>spreadSet</c>: <c>NaturalSort</c> over each value's string form.</summary>
    internal static List<HarloweValue> SpreadOrder(List<HarloweValue> items)
    {
      var keys = new string[items.Count];
      for (int i = 0; i < keys.Length; i++) keys[i] = SortKey(items[i], 0);
      var order = NaturalOrder.SortedIndices(keys);
      var sorted = new List<HarloweValue>(items.Count);
      for (int i = 0; i < order.Length; i++) sorted.Add(items[order[i]]);
      return sorted;
    }

    /// <summary>
    /// JavaScript's <c>String(value)</c> for the values a dataset can hold, which
    /// is what reference's sort compares. An array joins its items with commas.
    /// Anything without a string form of its own reads as the same placeholder,
    /// so such values keep their relative order.
    /// </summary>
    private static string SortKey(HarloweValue value, int depth)
    {
      switch (value.Kind)
      {
        case HarloweValueKind.Number: return HarloweValue.FormatNumber(value.AsNumber);
        case HarloweValueKind.String: return value.AsString;
        case HarloweValueKind.Bool: return value.AsBool ? "true" : "false";
        case HarloweValueKind.Datamap: return "[object Map]";
        case HarloweValueKind.Dataset: return "[object Set]";
        case HarloweValueKind.Array:
          if (depth >= MaxKeyDepth) return string.Empty;
          var items = value.AsArray;
          var sb = new System.Text.StringBuilder();
          for (int i = 0; i < items.Count; i++)
          {
            if (i > 0) sb.Append(',');
            sb.Append(SortKey(items[i], depth + 1));
          }
          return sb.ToString();
        default: return "[object Object]";
      }
    }

    /// <summary>The depth cap on walking a value's items, matching the others on value recursion.</summary>
    private const int MaxKeyDepth = 256;
  }
}
