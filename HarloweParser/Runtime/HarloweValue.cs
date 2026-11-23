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
    public static HarloweValue OfDatatype(DatatypeValue datatype) => new HarloweValue(HarloweValueKind.Datatype, datatype ?? throw new ArgumentNullException(nameof(datatype)));
    public static HarloweValue OfCodeHook(CodeHookValue hook) => new HarloweValue(HarloweValueKind.CodeHook, hook ?? throw new ArgumentNullException(nameof(hook)));
    public static HarloweValue OfTypedVar(TypedVarValue typedVar) => new HarloweValue(HarloweValueKind.TypedVar, typedVar ?? throw new ArgumentNullException(nameof(typedVar)));
    public static HarloweValue OfCustomMacro(CustomMacroValue macro) => new HarloweValue(HarloweValueKind.CustomMacro, macro ?? throw new ArgumentNullException(nameof(macro)));
    public static HarloweValue OfCommand(CommandValue command) => new HarloweValue(HarloweValueKind.Command, command ?? throw new ArgumentNullException(nameof(command)));

    /// <summary>
    /// A String whose text is printed exactly as it stands instead of being
    /// rendered as markup. <c>(print:)</c> uses this for a changer's descriptive
    /// form, <c>[A (font:) changer]</c>, whose brackets are not a hook. Reference
    /// gets the same result by wrapping it in verbatim markup.
    /// <paramref name="source"/> is the expression that recreates the value in a
    /// save, since a plain string literal would come back as markup.
    /// </summary>
    internal static HarloweValue OfLiteralText(string s, string source = null)
      => new HarloweValue(HarloweValueKind.String, s ?? string.Empty) { IsLiteralText = true, _literalSource = source };

    /// <summary>True for a String built by <see cref="OfLiteralText"/>. Doesn't affect equality.</summary>
    internal bool IsLiteralText { get; private set; }

    private string _literalSource;

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
    public DatatypeValue AsDatatype => (DatatypeValue)Raw;
    public CodeHookValue AsCodeHook => (CodeHookValue)Raw;
    public TypedVarValue AsTypedVar => (TypedVarValue)Raw;
    public CustomMacroValue AsCustomMacro => (CustomMacroValue)Raw;
    public CommandValue AsCommand => (CommandValue)Raw;

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
      => obj is HarloweValue other && ValuesEqual(this, other, 0);

    /// <summary>
    /// Depth cap for the structural walk, matching the caps elsewhere (the
    /// parsers, <c>ToSource</c>, <c>DeepCopyValue</c>, <c>(datapattern:)</c>,
    /// <c>matches</c>). Past it the values are reported unequal rather than
    /// recursed into: <see cref="Equals"/> is an <c>object</c> override with
    /// nowhere to put an in-prose error, and a wrong <c>false</c> is a far
    /// better failure than the uncatchable
    /// <see cref="System.StackOverflowException"/> this runtime's
    /// never-throw contract forbids. Reachable in practice because
    /// <c>DeepCopyValue</c> stops copying at its own cap instead of erroring,
    /// so an over-deep structure can sit in the store.
    /// </summary>
    private const int MaxEqualsDepth = 256;

    private static bool ValuesEqual(HarloweValue self, HarloweValue other, int depth)
    {
      if (self.Kind != other.Kind) return false;
      if (depth >= MaxEqualsDepth) return false;

      object raw = self.Raw;
      switch (self.Kind)
      {
        case HarloweValueKind.Number:
          return (double)raw == (double)other.Raw;
        case HarloweValueKind.String:
          return (string)raw == (string)other.Raw;
        case HarloweValueKind.Bool:
          return (bool)raw == (bool)other.Raw;
        case HarloweValueKind.Array:
          return ArraysEqual((List<HarloweValue>)raw, (List<HarloweValue>)other.Raw, depth);
        case HarloweValueKind.Datamap:
          return DatamapsEqual((Dictionary<string, HarloweValue>)raw, (Dictionary<string, HarloweValue>)other.Raw, depth);
        case HarloweValueKind.Error:
          return (string)raw == (string)other.Raw;
        case HarloweValueKind.Changer:
          return ((Changer)raw).Equals((Changer)other.Raw);
        case HarloweValueKind.Lambda:
          return ((LambdaValue)raw).Equals((LambdaValue)other.Raw);
        case HarloweValueKind.HookName:
          return ((HookNameValue)raw).Equals((HookNameValue)other.Raw);
        case HarloweValueKind.Colour:
          return ((ColourValue)raw).EqualsColour((ColourValue)other.Raw);
        case HarloweValueKind.Datatype:
          return ((DatatypeValue)raw).EqualsDatatype((DatatypeValue)other.Raw);
        case HarloweValueKind.CodeHook:
          return ((CodeHookValue)raw).EqualsHook((CodeHookValue)other.Raw);
        case HarloweValueKind.TypedVar:
        case HarloweValueKind.CustomMacro:
          return ReferenceEquals(raw, other.Raw);
        case HarloweValueKind.Command:
          return ((CommandValue)raw).EqualsCommand((CommandValue)other.Raw);
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
        case HarloweValueKind.Datatype: h = (h * 397) ^ ((DatatypeValue)Raw).GetHashCode(); break;
        case HarloweValueKind.CodeHook: h = (h * 397) ^ ((CodeHookValue)Raw).GetHashCode(); break;
        case HarloweValueKind.TypedVar:
        case HarloweValueKind.CustomMacro:
          h = (h * 397) ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Raw);
          break;
        case HarloweValueKind.Command:
          h = (h * 397) ^ ((CommandValue)Raw).CommandHash();
          break;
      }
      return h;
    }

    private static bool ArraysEqual(List<HarloweValue> a, List<HarloweValue> b, int depth)
    {
      if (a.Count != b.Count) return false;
      for (int i = 0; i < a.Count; i++)
      {
        if (!ValuesEqual(a[i], b[i], depth + 1)) return false;
      }
      return true;
    }

    private static bool DatamapsEqual(Dictionary<string, HarloweValue> a, Dictionary<string, HarloweValue> b, int depth)
    {
      if (a.Count != b.Count) return false;
      foreach (var kv in a)
      {
        if (!b.TryGetValue(kv.Key, out var bv)) return false;
        if (!ValuesEqual(kv.Value, bv, depth + 1)) return false;
      }
      return true;
    }

    /// <summary>
    /// Canonical number → string for value display, save source, and Twee
    /// reserialization, matching JavaScript's <c>Number.prototype.toString</c>
    /// — what reference Harlowe prints. The digits are the shortest that
    /// re-parse to the same <see cref="double"/>; the layout follows
    /// ECMAScript's <c>Number::toString</c>: plain digits from 1e-6 up to 1e21,
    /// otherwise an exponent form like <c>1e+21</c> or <c>1.5e-7</c>, and
    /// <c>0</c> for negative zero. .NET only supplies the digits: its own
    /// layout switches to exponents at 1e15 (.NET Framework) or 1e17 (.NET 8)
    /// and below 1e-4, writes them <c>1E-05</c>, and prints <c>-0</c> on .NET
    /// Core 3.0+, so the same story would display differently per host. Every
    /// form re-lexes: the tokenizer's number rule accepts exponents.
    /// </summary>
    public static string FormatNumber(double d)
    {
      if (double.IsNaN(d)) return "NaN";
      if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
      if (d == 0) return "0";
      var ic = CultureInfo.InvariantCulture;
      // Whole numbers below 1e15 (exact in a long) are the common case.
      if (Math.Abs(d) < 1e15 && d == Math.Floor(d)) return ((long)d).ToString(ic);

      // "R" is shortest on .NET Core 3.0+, but on .NET Framework it is G15
      // falling back to G17 and can under-round, so a 17-digit or
      // non-round-tripping answer gets a 16-digit retry.
      string s = d.ToString("R", ic);
      SplitDigits(s, out bool negative, out string digits, out int point);
      if (digits.Length == 17 || !RoundTrips(s, d))
      {
        string g16 = d.ToString("G16", ic);
        s = RoundTrips(g16, d) ? g16 : d.ToString("G17", ic);
        SplitDigits(s, out negative, out digits, out point);
      }

      // ECMAScript Number::toString, with the value as 0.digits × 10^point.
      int k = digits.Length, n = point;
      var sb = new StringBuilder(k + 8);
      if (negative) sb.Append('-');
      if (k <= n && n <= 21)
        sb.Append(digits).Append('0', n - k);
      else if (0 < n && n <= 21)
        sb.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
      else if (-6 < n && n <= 0)
        sb.Append("0.").Append('0', -n).Append(digits);
      else
      {
        sb.Append(digits[0]);
        if (k > 1) sb.Append('.').Append(digits, 1, k - 1);
        int e = n - 1;
        sb.Append('e').Append(e < 0 ? '-' : '+').Append((e < 0 ? -e : e).ToString(ic));
      }
      return sb.ToString();
    }

    private static bool RoundTrips(string s, double d)
      => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var back) && back.Equals(d);

    /// <summary>
    /// Splits a non-zero invariant-culture "R"/"G" string into its sign, its
    /// significant digits (no leading or trailing zeros), and the decimal
    /// point's position, so the value is 0.digits × 10^point.
    /// </summary>
    private static void SplitDigits(string s, out bool negative, out string digits, out int point)
    {
      negative = s[0] == '-';
      int e = s.IndexOf('E');
      int end = e < 0 ? s.Length : e;
      int exponent = e < 0 ? 0 : int.Parse(s.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
      var all = new StringBuilder(end);
      int intDigits = 0;
      bool pastDot = false;
      for (int i = negative ? 1 : 0; i < end; i++)
      {
        if (s[i] == '.') { pastDot = true; continue; }
        all.Append(s[i]);
        if (!pastDot) intDigits++;
      }
      int lead = 0;
      while (lead < all.Length - 1 && all[lead] == '0') lead++;
      int trail = all.Length;
      while (trail > lead + 1 && all[trail - 1] == '0') trail--;
      digits = all.ToString(lead, trail - lead);
      point = intDigits + exponent - lead;
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
    internal string ToSource(int depth)
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
          // Literal text with no recreating expression can't be saved: as a
          // plain string it would come back as markup, so the save fails loudly.
          return IsLiteralText ? _literalSource : Twee.MarkupPrinter.StringLiteral((string)Raw);
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
        case HarloweValueKind.Datatype:
          return ((DatatypeValue)Raw).ToSource();
        case HarloweValueKind.CodeHook:
          return ((CodeHookValue)Raw).Source;
        case HarloweValueKind.TypedVar:
          return ((TypedVarValue)Raw).ToSource(depth + 1);
        case HarloweValueKind.CustomMacro:
          return ((CustomMacroValue)Raw).ToSource(depth + 1);
        case HarloweValueKind.Command:
          return ((CommandValue)Raw).ToSource(depth + 1);
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
        case HarloweValueKind.Datatype:
          // Reference's print() is "[the num datatype]" wrapped in verbatim
          // markup. The body renderer prints only strings as markup, so a
          // datatype's brackets stay literal.
          return ((DatatypeValue)Raw).ToPrintedString();
        case HarloweValueKind.CodeHook:
          return ((CodeHookValue)Raw).InnerSource;
        case HarloweValueKind.TypedVar:
          return "[A typed variable name]";
        case HarloweValueKind.CustomMacro:
          return "[" + ((CustomMacroValue)Raw).ObjectName + "]";
        case HarloweValueKind.Command:
          return ((CommandValue)Raw).PrintedName;
      }
      return string.Empty;
    }

    /// <summary>
    /// Reference's <c>objectName</c>: how an error message refers to this value,
    /// such as <c>the number 5</c> or <c>the string "x"</c>.
    /// </summary>
    internal string ObjectName
    {
      get
      {
        switch (Kind)
        {
          case HarloweValueKind.Number: return "the number " + FormatNumber((double)Raw);
          case HarloweValueKind.String: return "the string \"" + (string)Raw + "\"";
          case HarloweValueKind.Bool: return "the boolean value '" + ((bool)Raw ? "true" : "false") + "'";
          case HarloweValueKind.Array: return ((List<HarloweValue>)Raw).Count == 0 ? "an empty array" : "an array";
          case HarloweValueKind.Datamap: return ((Dictionary<string, HarloweValue>)Raw).Count == 0 ? "an empty datamap" : "a datamap";
          case HarloweValueKind.Changer:
            string front = ((Changer)Raw).FrontMacroName;
            return front != null ? "a (" + front + ":) changer" : "a changer";
          case HarloweValueKind.Lambda: return "a lambda";
          case HarloweValueKind.HookName: return "a hook name";
          case HarloweValueKind.Colour: return "a colour";
          case HarloweValueKind.Datatype: return "the " + ((DatatypeValue)Raw).Name + " datatype";
          case HarloweValueKind.CodeHook: return "a code hook";
          case HarloweValueKind.TypedVar: return ((TypedVarValue)Raw).ObjectName;
          case HarloweValueKind.CustomMacro: return ((CustomMacroValue)Raw).ObjectName;
          case HarloweValueKind.Command: return ((CommandValue)Raw).ObjectName;
          default: return "an error";
        }
      }
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
