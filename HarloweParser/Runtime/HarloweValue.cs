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
    /// Private constructor — callers go through the typed factories
    /// (<see cref="OfNumber"/>, <see cref="OfString"/>, …) so a Kind cannot
    /// be paired with a payload of the wrong CLR type. Was previously public
    /// for "fast-path" literal construction, but no production code took that
    /// path — every <c>new HarloweValue</c> site already routed through a
    /// factory.
    /// </summary>
    private HarloweValue(HarloweValueKind kind, object raw)
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
    public static HarloweValue OfHookName(HookNameValue hookName) => new HarloweValue(HarloweValueKind.HookName, hookName ?? throw new ArgumentNullException(nameof(hookName)));
    public static HarloweValue OfColour(ColourValue colour) => new HarloweValue(HarloweValueKind.Colour, colour ?? throw new ArgumentNullException(nameof(colour)));

    public bool IsError => Kind == HarloweValueKind.Error;

    public double AsNumber => (double)Raw;
    public string AsString => (string)Raw;
    public bool AsBool => (bool)Raw;
    public List<HarloweValue> AsArray => (List<HarloweValue>)Raw;
    public Dictionary<string, HarloweValue> AsDatamap => (Dictionary<string, HarloweValue>)Raw;
    public string ErrorMessage => (string)Raw;
    public Changer AsChanger => (Changer)Raw;
    public LambdaValue AsLambda => (LambdaValue)Raw;
    public HookNameValue AsHookName => (HookNameValue)Raw;
    public ColourValue AsColour => (ColourValue)Raw;

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
        case HarloweValueKind.HookName:
          return ((HookNameValue)Raw).Equals((HookNameValue)other.Raw);
        case HarloweValueKind.Colour:
          return ((ColourValue)Raw).EqualsColour((ColourValue)other.Raw);
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
        case HarloweValueKind.HookName: h = (h * 397) ^ ((HookNameValue)Raw).GetHashCode(); break;
        case HarloweValueKind.Colour: h = (h * 397) ^ ((ColourValue)Raw).GetHashCode(); break;
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
    /// Canonical number → string for both value display and Twee
    /// reserialization: the shortest representation in invariant culture that
    /// re-parses to the same <see cref="double"/>, so a parsed literal survives
    /// a print/re-parse cycle. The "R" specifier is shortest but can
    /// occasionally under-round on .NET Framework, so we verify the round trip
    /// and fall back to "G17" (which always round-trips). Exponent forms like
    /// <c>1E-07</c> are fine — the tokenizer's number rule accepts exponents.
    /// </summary>
    public static string FormatNumber(double d)
    {
      string s = d.ToString("R", CultureInfo.InvariantCulture);
      if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var back) && back.Equals(d))
        return s;
      return d.ToString("G17", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Serialise this value to Harlowe <em>source</em> that re-evaluates (via the
    /// tokenizer + <see cref="ExpressionEvaluator"/>) to an equal value — the
    /// save/load primitive, matching reference's source-based variable store
    /// (<c>toSource</c> + re-<c>eval</c> in <c>varscope.ts</c>). Number →
    /// <see cref="FormatNumber"/>, String → a quoted literal, Bool → <c>true</c>/
    /// <c>false</c>, Array → <c>(a:…)</c>, Datamap → <c>(dm:"k",v,…)</c> (keys
    /// ordinal-sorted for determinism; round-trip is order-independent), Lambda /
    /// HookName → the printed expression, Changer → its stamped
    /// <see cref="Changer.Source"/>.
    ///
    /// <para>Returns <c>null</c> for a value with no source form — a non-finite number
    /// (NaN/±∞), an <see cref="HarloweValueKind.Error"/>, or a Changer whose source was
    /// never stamped — recursing into collections, so a <c>NaN</c> nested in
    /// <c>(a: 1, NaN)</c> makes the whole array un-sourceable. Callers treat null as
    /// "this value can't be saved". Non-throwing, so it is safe to call from the
    /// evaluator's changer-source stamp on the hot path.</para>
    /// </summary>
    public string ToSource() => ToSource(0);

    private const int MaxSourceDepth = 256;

    /// <summary>
    /// Depth-guarded recursive worker for <see cref="ToSource()"/>. Past
    /// <see cref="MaxSourceDepth"/> levels of nesting it returns null — a
    /// pathologically deep value is treated as un-saveable rather than risking an
    /// uncatchable <see cref="System.StackOverflowException"/>, matching the depth
    /// caps elsewhere (DeepCopyValue, the parsers, JsonReader).
    /// </summary>
    private string ToSource(int depth)
    {
      if (depth >= MaxSourceDepth) return null;
      switch (Kind)
      {
        case HarloweValueKind.Number:
        {
          double d = (double)Raw;
          if (double.IsNaN(d) || double.IsInfinity(d)) return null;
          return FormatNumber(d);
        }
        case HarloweValueKind.String:
          return Twee.MarkupPrinter.StringLiteral((string)Raw);
        case HarloweValueKind.Bool:
          return (bool)Raw ? "true" : "false";
        case HarloweValueKind.Array:
          return ArrayToSource((List<HarloweValue>)Raw, depth);
        case HarloweValueKind.Datamap:
          return DatamapToSource((Dictionary<string, HarloweValue>)Raw, depth);
        case HarloweValueKind.Changer:
          return ((Changer)Raw).Source;
        case HarloweValueKind.Lambda:
          return new Twee.MarkupPrinter().Print(((LambdaValue)Raw).Node);
        case HarloweValueKind.HookName:
          return HookNameToSource((HookNameValue)Raw);
        case HarloweValueKind.Colour:
          return ((ColourValue)Raw).ToSource();
        case HarloweValueKind.Error:
          return null;
      }
      return null;
    }

    private static string ArrayToSource(List<HarloweValue> items, int depth)
    {
      var sb = new StringBuilder("(a:");
      for (int i = 0; i < items.Count; i++)
      {
        if (i > 0) sb.Append(',');
        string s = items[i].ToSource(depth + 1);
        if (s == null) return null;
        sb.Append(s);
      }
      return sb.Append(')').ToString();
    }

    private static string DatamapToSource(Dictionary<string, HarloweValue> map, int depth)
    {
      var keys = new List<string>(map.Count);
      foreach (var kv in map) keys.Add(kv.Key);
      keys.Sort(StringComparer.Ordinal);
      var sb = new StringBuilder("(dm:");
      for (int i = 0; i < keys.Count; i++)
      {
        if (i > 0) sb.Append(',');
        string vs = map[keys[i]].ToSource(depth + 1);
        if (vs == null) return null;
        sb.Append(Twee.MarkupPrinter.StringLiteral(keys[i])).Append(',').Append(vs);
      }
      return sb.Append(')').ToString();
    }

    private static string HookNameToSource(HookNameValue hn)
    {
      var node = new Ast.Expression.HookRefNode { Name = hn.Name };
      if (hn.Steps != null)
        foreach (var step in hn.Steps) node.Steps.Add(step);
      return new Twee.MarkupPrinter().Print(node);
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
          return FormatNumber((double)Raw);
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
        {
          // Reference prints changers as "[A (if:) changer]" (changer.ts's
          // print()); unstamped changers have no creation source to name.
          string front = ((Changer)Raw).FrontMacroName;
          return front != null ? "[A (" + front + ":) changer]" : "[A changer]";
        }
        case HarloweValueKind.Lambda:
          return string.Empty;
        case HarloweValueKind.HookName:
          // A hook name alone has no visible text — it is a selector consumed
          // by revision/enchantment macros, not printed prose.
          return string.Empty;
        case HarloweValueKind.Colour:
          // The CSS rgba() form — what a printed colour actually renders as,
          // here and in the body renderer. Reference's print() emits a
          // <tw-colour> swatch element instead; that's HTML, and the core never
          // puts HTML into the engine-agnostic render channel.
          return ((ColourValue)Raw).ToCssString();
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

    /// <summary>
    /// Serialize a datamap as <c>key:value,key:value</c>. Keys are emitted in
    /// ordinal sort order so the rendered text is stable across runtimes —
    /// <see cref="Dictionary{TKey, TValue}"/> enumeration order is
    /// "insertion-order in practice but not contractually guaranteed," and
    /// Mono / Unity IL2CPP have been observed to diverge from CoreCLR. The
    /// equality path is order-independent, so this only affects user-visible
    /// rendering.
    /// </summary>
    private static string JoinDatamap(Dictionary<string, HarloweValue> map)
    {
      var keys = new List<string>(map.Count);
      foreach (var kv in map) keys.Add(kv.Key);
      keys.Sort(System.StringComparer.Ordinal);

      var sb = new StringBuilder();
      for (int i = 0; i < keys.Count; i++)
      {
        if (i > 0) sb.Append(',');
        sb.Append(keys[i]).Append(':').Append(map[keys[i]].ToHarloweString());
      }
      return sb.ToString();
    }

    public override string ToString() => $"{Kind}({ToHarloweString()})";
  }
}
