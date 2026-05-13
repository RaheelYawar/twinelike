using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A tagged-union runtime value. <see cref="Kind"/> tells you which variant is
  /// in play; <see cref="Raw"/> holds the boxed payload (a <see cref="double"/>,
  /// <see cref="string"/>, <see cref="bool"/>, <c>List&lt;HarloweValue&gt;</c>,
  /// <c>Dictionary&lt;string, HarloweValue&gt;</c>, or an error message
  /// <see cref="string"/>). The shape mirrors <see cref="Ast.Expression.LiteralNode"/>'s
  /// Kind+object pair so the evaluator can copy a literal in one line.
  ///
  /// <para>
  /// Boxing for <see cref="HarloweValueKind.Number"/> and
  /// <see cref="HarloweValueKind.Bool"/> is accepted: story execution is not
  /// allocation-bound, and a uniform shape keeps the evaluator and macro
  /// registry simple. If this becomes painful, the migration path is per-variant
  /// subclasses behind the same factory entry points.
  /// </para>
  ///
  /// <para>
  /// Construct values via the static factories (<see cref="OfNumber"/>,
  /// <see cref="OfString"/>, etc.) rather than the constructor so callers cannot
  /// build a <see cref="HarloweValueKind.Number"/> with a string payload by
  /// mistake.
  /// </para>
  /// </summary>
  public class HarloweValue
  {
    public HarloweValueKind Kind { get; }
    public object Raw { get; }

    /// <summary>
    /// Direct constructor. Most call sites should prefer the typed factory
    /// methods; this is exposed for the evaluator's
    /// <see cref="Ast.Expression.LiteralNode"/> fast path where Kind+Value can
    /// be copied verbatim.
    /// </summary>
    public HarloweValue(HarloweValueKind kind, object raw)
    {
      Kind = kind;
      Raw = raw;
    }

    public static HarloweValue OfNumber(double n) => new HarloweValue(HarloweValueKind.Number, n);
    public static HarloweValue OfString(string s) => new HarloweValue(HarloweValueKind.String, s ?? string.Empty);
    public static HarloweValue OfBool(bool b) => new HarloweValue(HarloweValueKind.Bool, b);
    public static HarloweValue OfArray(List<HarloweValue> items) => new HarloweValue(HarloweValueKind.Array, items ?? new List<HarloweValue>());
    public static HarloweValue OfDatamap(Dictionary<string, HarloweValue> map) => new HarloweValue(HarloweValueKind.Datamap, map ?? new Dictionary<string, HarloweValue>());
    public static HarloweValue OfError(string message) => new HarloweValue(HarloweValueKind.Error, message ?? string.Empty);
    public static HarloweValue OfChanger(Changer changer) => new HarloweValue(HarloweValueKind.Changer, changer ?? throw new ArgumentNullException(nameof(changer)));
    public static HarloweValue OfLambda(LambdaValue lambda) => new HarloweValue(HarloweValueKind.Lambda, lambda ?? throw new ArgumentNullException(nameof(lambda)));

    public bool IsError => Kind == HarloweValueKind.Error;

    public double AsNumber => (double)Raw;
    public string AsString => (string)Raw;
    public bool AsBool => (bool)Raw;
    public List<HarloweValue> AsArray => (List<HarloweValue>)Raw;
    public Dictionary<string, HarloweValue> AsDatamap => (Dictionary<string, HarloweValue>)Raw;
    public string ErrorMessage => (string)Raw;
    public Changer AsChanger => (Changer)Raw;
    public LambdaValue AsLambda => (LambdaValue)Raw;

    /// <summary>
    /// Harlowe truthiness: only <c>true</c> for a <see cref="HarloweValueKind.Bool"/>
    /// payload of <c>true</c>. Numbers, strings, collections, and errors are not
    /// implicitly truthy — Harlowe authors must compare explicitly. (Errors
    /// reach this method only when a macro asks; they normally short-circuit
    /// before truthiness is sampled.)
    /// </summary>
    public bool IsTruthy => Kind == HarloweValueKind.Bool && (bool)Raw;

    /// <summary>
    /// Value equality with Harlowe <c>is</c> semantics: same Kind, payloads
    /// equal by value. Numbers compare with CLR <c>==</c> (so NaN is not equal
    /// to itself, matching JavaScript-style behaviour Harlowe inherits).
    /// Arrays and datamaps compare structurally, recursing through nested
    /// values. Two errors are equal iff their messages match — a degenerate
    /// case that mostly just keeps the relation reflexive.
    /// </summary>
    public override bool Equals(object obj)
    {
      if (!(obj is HarloweValue other)) return false;
      if (Kind != other.Kind) return false;

      switch (Kind)
      {
        case HarloweValueKind.Number:
          return (double)Raw == (double)other.Raw;
        case HarloweValueKind.String:
          return (string)Raw == (string)other.Raw;
        case HarloweValueKind.Bool:
          return (bool)Raw == (bool)other.Raw;
        case HarloweValueKind.Array:
          return ArraysEqual((List<HarloweValue>)Raw, (List<HarloweValue>)other.Raw);
        case HarloweValueKind.Datamap:
          return DatamapsEqual((Dictionary<string, HarloweValue>)Raw, (Dictionary<string, HarloweValue>)other.Raw);
        case HarloweValueKind.Error:
          return (string)Raw == (string)other.Raw;
        case HarloweValueKind.Changer:
          return ((Changer)Raw).Equals((Changer)other.Raw);
        case HarloweValueKind.Lambda:
          return ((LambdaValue)Raw).Equals((LambdaValue)other.Raw);
      }
      return false;
    }

    /// <summary>
    /// Hash combines <see cref="Kind"/> with the payload. Collections hash by
    /// count + first-element hash to stay cheap and stable; the structural
    /// fallback lives in <see cref="Equals"/>. Hash collisions are fine —
    /// equality is the source of truth.
    /// </summary>
    public override int GetHashCode()
    {
      int h = (int)Kind;
      switch (Kind)
      {
        case HarloweValueKind.Number: h = (h * 397) ^ ((double)Raw).GetHashCode(); break;
        case HarloweValueKind.String: h = (h * 397) ^ ((string)Raw).GetHashCode(); break;
        case HarloweValueKind.Bool: h = (h * 397) ^ ((bool)Raw).GetHashCode(); break;
        case HarloweValueKind.Array:
          var arr = (List<HarloweValue>)Raw;
          h = (h * 397) ^ arr.Count;
          if (arr.Count > 0) h = (h * 397) ^ arr[0].GetHashCode();
          break;
        case HarloweValueKind.Datamap:
          h = (h * 397) ^ ((Dictionary<string, HarloweValue>)Raw).Count;
          break;
        case HarloweValueKind.Error: h = (h * 397) ^ ((string)Raw).GetHashCode(); break;
        case HarloweValueKind.Changer: h = (h * 397) ^ ((Changer)Raw).GetHashCode(); break;
        case HarloweValueKind.Lambda: h = (h * 397) ^ ((LambdaValue)Raw).GetHashCode(); break;
      }
      return h;
    }

    private static bool ArraysEqual(List<HarloweValue> a, List<HarloweValue> b)
    {
      if (a.Count != b.Count) return false;
      for (int i = 0; i < a.Count; i++)
      {
        if (!a[i].Equals(b[i])) return false;
      }
      return true;
    }

    private static bool DatamapsEqual(Dictionary<string, HarloweValue> a, Dictionary<string, HarloweValue> b)
    {
      if (a.Count != b.Count) return false;
      foreach (var kv in a)
      {
        if (!b.TryGetValue(kv.Key, out var bv)) return false;
        if (!kv.Value.Equals(bv)) return false;
      }
      return true;
    }

    /// <summary>
    /// Renderer-facing string form. Numbers use invariant culture so a story
    /// always prints <c>1.5</c>, never <c>1,5</c>. Booleans render as the
    /// lowercase keywords Harlowe uses in source. Arrays render as
    /// <c>a, b, c</c> (Harlowe's default join). Datamaps render as
    /// <c>key: value, ...</c>. Errors render their message verbatim — the
    /// renderer is expected to route them through
    /// <see cref="IRenderOutput.Error"/> rather than print this directly, but
    /// returning the message keeps debugging easy.
    /// </summary>
    public string ToHarloweString()
    {
      switch (Kind)
      {
        case HarloweValueKind.Number:
          return ((double)Raw).ToString("R", CultureInfo.InvariantCulture);
        case HarloweValueKind.String:
          return (string)Raw;
        case HarloweValueKind.Bool:
          return (bool)Raw ? "true" : "false";
        case HarloweValueKind.Array:
          return JoinArray((List<HarloweValue>)Raw);
        case HarloweValueKind.Datamap:
          return JoinDatamap((Dictionary<string, HarloweValue>)Raw);
        case HarloweValueKind.Error:
          return (string)Raw;
        case HarloweValueKind.Changer:
          // A changer alone has no visible text — its effect is to wrap a
          // hook's content. Returning empty keeps `(print: (text-style: ...))`
          // and similar interpolations from dumping internal state into prose.
          return string.Empty;
        case HarloweValueKind.Lambda:
          return string.Empty;
      }
      return string.Empty;
    }

    private static string JoinArray(List<HarloweValue> items)
    {
      var sb = new StringBuilder();
      for (int i = 0; i < items.Count; i++)
      {
        if (i > 0) sb.Append(',');
        sb.Append(items[i].ToHarloweString());
      }
      return sb.ToString();
    }

    private static string JoinDatamap(Dictionary<string, HarloweValue> map)
    {
      var sb = new StringBuilder();
      bool first = true;
      foreach (var kv in map)
      {
        if (!first) sb.Append(',');
        first = false;
        sb.Append(kv.Key).Append(':').Append(kv.Value.ToHarloweString());
      }
      return sb.ToString();
    }

    public override string ToString() => $"{Kind}({ToHarloweString()})";
  }
}
