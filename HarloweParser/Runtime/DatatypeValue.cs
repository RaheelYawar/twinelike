using System;
using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A Harlowe datatype — the keyword value (<c>num</c>, <c>str</c>, <c>even</c>,
  /// …) that <c>is a</c> and <c>matches</c> compare against. Reference's
  /// <c>ts/datatypes/datatype.ts</c>.
  ///
  /// <para>Only the canonical <see cref="Name"/> is stored: reference's
  /// constructor folds each long spelling onto its abbreviation
  /// (<c>datamap</c>→<c>dm</c>, <c>number</c>→<c>num</c>, …), so <c>num</c> and
  /// <c>number</c> are one value and compare equal. The author's spelling
  /// survives separately, on the <c>LiteralNode</c> the parser builds, which is
  /// what lets a dirty passage reserialize verbatim.</para>
  ///
  /// <para>Immutable and <em>interned</em>: there is exactly one instance per
  /// canonical name, handed out by <see cref="FromLexeme"/> and
  /// <see cref="From"/>. Evaluating a datatype literal is therefore a dictionary
  /// lookup rather than an allocation, which matters on the loop-heavy paths
  /// (<c>(for:)</c> bodies testing <c>is a num</c>) this library's game-engine
  /// consumers run under IL2CPP. Interning also makes reference equality agree
  /// with <see cref="EqualsDatatype"/>.</para>
  ///
  /// <para><em>Not yet implemented:</em> spread datatypes (<c>...num</c>), which
  /// wait on the <c>...</c> spread syntax, and the <c>(p:)</c> string-pattern
  /// family, which subclasses this in reference (<c>ts/datatypes/pattern.ts</c>).
  /// Datatype names for value types this library doesn't have yet — <c>ds</c>,
  /// <c>gradient</c>, <c>image</c>, <c>macro</c>, <c>command</c>, <c>codehook</c>,
  /// <c>measure</c> — still lex and compare; they simply match nothing, which is
  /// what an author sees for a value they can't construct either.</para>
  /// </summary>
  public class DatatypeValue
  {
    /// <summary>The canonical (abbreviated) type name, e.g. <c>num</c>, never <c>number</c>.</summary>
    public readonly string Name;

    private DatatypeValue(string name)
    {
      Name = name;
    }

    /// <summary>
    /// Every spelling the lexer accepts, mapped to its canonical name. Mirrors
    /// reference's <c>datatype</c> pattern in <c>ts/markup/patterns.ts</c> ("This
    /// MUST line up with every type in datatypes.ts") folded through the
    /// abbreviation table in the <c>Datatype</c> constructor. Matched
    /// case-insensitively, as reference's rule lowercases the match.
    /// </summary>
    private static readonly Dictionary<string, string> Spellings =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        // Basic types.
        { "array", "array" },
        { "datamap", "dm" }, { "dm", "dm" },
        { "dataset", "ds" }, { "ds", "ds" },
        { "datatype", "datatype" },
        { "changer", "changer" },
        { "colour", "colour" }, { "color", "colour" },
        { "gradient", "gradient" },
        { "image", "image" },
        { "lambda", "lambda" },
        { "macro", "macro" },
        { "codehook", "codehook" },
        { "command", "command" },
        { "measure", "measure" },
        { "string", "str" }, { "str", "str" },
        { "number", "num" }, { "num", "num" },
        { "boolean", "bool" }, { "bool", "bool" },
        // Subset types.
        { "even", "even" },
        { "odd", "odd" },
        { "empty", "empty" },
        { "integer", "int" }, { "int", "int" },
        { "uppercase", "uppercase" },
        { "lowercase", "lowercase" },
        { "anycase", "anycase" },
        { "whitespace", "whitespace" },
        { "digit", "digit" },
        { "alphanumeric", "alnum" }, { "alnum", "alnum" },
        { "linebreak", "linebreak" }, { "newline", "linebreak" },
        { "any", "any" },
        { "const", "const" },
      };

    /// <summary>
    /// The one instance per canonical name. Built from <see cref="Spellings"/>
    /// so a name can never be interned here but unspellable by the lexer.
    /// </summary>
    private static readonly Dictionary<string, DatatypeValue> Interned = BuildInterned();

    private static Dictionary<string, DatatypeValue> BuildInterned()
    {
      var map = new Dictionary<string, DatatypeValue>(StringComparer.Ordinal);
      foreach (var pair in Spellings)
      {
        if (!map.ContainsKey(pair.Value)) map[pair.Value] = new DatatypeValue(pair.Value);
      }
      return map;
    }

    /// <summary>
    /// Does <paramref name="value"/> belong to the named type? Reference's
    /// <c>typeIndex</c> table in <c>ts/datatypes/datatype.ts</c>, kept as a table
    /// here too so "which names are implemented" is a readable list rather than
    /// something you derive by diffing a switch against
    /// <see cref="Spellings"/>. A name absent from this table — which here means
    /// a name for a value type this library doesn't implement (<c>ds</c>,
    /// <c>gradient</c>, <c>image</c>, <c>macro</c>, <c>command</c>,
    /// <c>codehook</c>, <c>measure</c>) — matches nothing, as in reference
    /// (<c>typeIndex[name] ? … : false</c>).
    /// </summary>
    private static readonly Dictionary<string, Func<HarloweValue, bool>> TypeIndex =
      new Dictionary<string, Func<HarloweValue, bool>>(StringComparer.Ordinal)
      {
        // Reference's `any` matches anything; `const` matches everything too
        // (its real work is a special case in VarRef's set(), and this arm
        // exists "only for destructuring").
        { "any", v => true },
        { "const", v => true },

        { "array", v => v.Kind == HarloweValueKind.Array },
        { "dm", v => v.Kind == HarloweValueKind.Datamap },
        { "datatype", v => v.Kind == HarloweValueKind.Datatype },
        { "changer", v => v.Kind == HarloweValueKind.Changer },
        { "colour", v => v.Kind == HarloweValueKind.Colour },
        { "lambda", v => v.Kind == HarloweValueKind.Lambda },
        { "str", v => v.Kind == HarloweValueKind.String },
        { "num", v => v.Kind == HarloweValueKind.Number },
        { "bool", v => v.Kind == HarloweValueKind.Bool },

        { "even", v => IsParity(v, 0) },
        { "odd", v => IsParity(v, 1) },

        // Reference tests `obj === (obj|0)`, a *32-bit* truncation, so a whole
        // number beyond int32 range is not an `int` there either. Matched
        // deliberately — an author's `$n is an int` guard should agree across
        // implementations even at the edges. NaN and the infinities fail the
        // same test.
        { "int", v => v.Kind == HarloweValueKind.Number
            && v.AsNumber >= int.MinValue && v.AsNumber <= int.MaxValue
            && v.AsNumber == (int)v.AsNumber },

        { "empty", IsEmpty },

        { "uppercase", v => IsSingleCasedCodePoint(v, requireUpper: true) },
        { "lowercase", v => IsSingleCasedCodePoint(v, requireUpper: false) },

        { "anycase", v => IsSingleCodeUnitWhere(v, IsCasedUnit) },
        { "whitespace", v => IsSingleCodeUnitWhere(v, CodePoints.IsRealWhitespace) },
        { "digit", v => IsSingleCodeUnitWhere(v, c => c >= '0' && c <= '9') },
        { "alnum", v => IsSingleCodeUnitWhere(v, IsRealLetter) },

        // The one type that can match two characters: reference's `anyNewline`
        // alternates \n, \r, and \r\n, so a CRLF pair is a single line break.
        { "linebreak", v => v.Kind == HarloweValueKind.String
            && (v.AsString == "\n" || v.AsString == "\r" || v.AsString == "\r\n") },
      };

    /// <summary>True iff <paramref name="word"/> is a datatype name (case-insensitive).</summary>
    public static bool IsNamed(string word) => word != null && Spellings.ContainsKey(word);

    /// <summary>
    /// The datatype for a lexed name, canonicalising the spelling. Returns null
    /// for anything that isn't a datatype name — the tokenizer only emits names
    /// that pass <see cref="IsNamed"/>, so null means a caller bug.
    /// </summary>
    public static DatatypeValue FromLexeme(string lexeme)
      => lexeme != null && Spellings.TryGetValue(lexeme, out var canonical)
        ? Interned[canonical]
        : null;

    /// <summary>
    /// The datatype a value belongs to, for <c>(datatype:)</c> — reference's
    /// <c>Datatype.from()</c>, which searches only <c>basicTypeIndex</c>, so a
    /// value is never described by a subset type (<c>2</c> is <c>num</c>, not
    /// <c>even</c>). Null for a value with no datatype name, which the caller
    /// turns into an in-prose error (reference asserts here; its macro article
    /// promises "if there isn't a known datatype value for the given data … an
    /// error will be produced").
    /// </summary>
    public static DatatypeValue From(HarloweValue value)
    {
      if (value == null) return null;
      switch (value.Kind)
      {
        case HarloweValueKind.Array: return Interned["array"];
        case HarloweValueKind.Datamap: return Interned["dm"];
        case HarloweValueKind.Datatype: return Interned["datatype"];
        case HarloweValueKind.Changer: return Interned["changer"];
        case HarloweValueKind.Colour: return Interned["colour"];
        case HarloweValueKind.Lambda: return Interned["lambda"];
        case HarloweValueKind.String: return Interned["str"];
        case HarloweValueKind.Number: return Interned["num"];
        case HarloweValueKind.Bool: return Interned["bool"];
      }
      return null;
    }

    /// <summary>
    /// Does <paramref name="value"/> belong to this type? Dispatches through
    /// <see cref="TypeIndex"/>; an unlisted name matches nothing.
    /// </summary>
    public bool IsTypeOf(HarloweValue value)
    {
      if (value == null) return false;
      return TypeIndex.TryGetValue(Name, out var predicate) && predicate(value);
    }

    private static bool IsEmpty(HarloweValue value)
    {
      switch (value.Kind)
      {
        case HarloweValueKind.String: return value.AsString.Length == 0;
        case HarloweValueKind.Array: return value.AsArray.Count == 0;
        case HarloweValueKind.Datamap: return value.AsDatamap.Count == 0;
      }
      return false;
    }

    private static bool IsParity(HarloweValue value, int remainder)
    {
      if (value.Kind != HarloweValueKind.Number) return false;
      double n = value.AsNumber;
      if (double.IsNaN(n) || double.IsInfinity(n)) return false;
      return Math.Floor(Math.Abs(n)) % 2 == remainder;
    }

    /// <summary>
    /// True when the value is a string of exactly one <em>code point</em> that
    /// changes under case conversion — reference's <c>uppercase</c>/
    /// <c>lowercase</c>, which are the two arms it defines over
    /// <c>[...obj]</c> rather than a regex, so an astral cased character
    /// (Deseret, Adlam) counts here.
    ///
    /// <para><em>Divergence:</em> reference compares against JS
    /// <c>toUpperCase()</c>, which applies full case mapping and so
    /// <em>expands</em> — <c>"ß".toUpperCase()</c> is <c>"SS"</c>, making
    /// <c>"ß" is a lowercase</c> true there. .NET's invariant casing is simple
    /// (non-expanding) and netstandard2.0 offers no full-case-mapping API, so
    /// <c>ß</c>, <c>ﬁ</c> (U+FB01) and <c>ŉ</c> (U+0149) are not
    /// <c>lowercase</c> here. Deliberate: it keeps these datatypes agreeing
    /// with this library's <c>(uppercase:)</c>/<c>(lowercase:)</c>, which are
    /// non-expanding for the same reason — and reference documents that
    /// agreement as the property it wants ("coincidentally consistent with
    /// (uppercase:), (lowercase:)").</para>
    /// </summary>
    private static bool IsSingleCasedCodePoint(HarloweValue value, bool requireUpper)
    {
      if (value.Kind != HarloweValueKind.String) return false;
      string s = value.AsString;
      if (CodePoints.Count(s) != 1) return false;
      return requireUpper ? s != s.ToLowerInvariant() : s != s.ToUpperInvariant();
    }

    /// <summary>
    /// The single-code-<em>unit</em> test shared by the datatypes reference
    /// implements as an anchored regex (<c>whitespace</c>, <c>digit</c>,
    /// <c>alnum</c>, <c>anycase</c>). Those go through
    /// <c>obj.match(`^…$`)</c> — a RegExp built from a string, so with no
    /// <c>u</c> flag — and an unflagged <c>^[…]$</c> can only match one UTF-16
    /// code unit. An astral character is two units and therefore never matches
    /// in reference, however its code point is classified.
    /// </summary>
    private static bool IsSingleCodeUnitWhere(HarloweValue value, Func<char, bool> predicate)
      => value.Kind == HarloweValueKind.String
        && value.AsString.Length == 1
        && predicate(value.AsString[0]);

    /// <summary>
    /// Reference's <c>anyCasedLetter</c> class in <c>ts/utils.ts</c>: the code
    /// units whose lower and upper forms differ from each other. Reference
    /// materialises the class by scanning the BMP ("any character in the Basic
    /// Multilingual Plane which doesn't round-trip"), which is the same test
    /// applied per unit.
    /// </summary>
    private static bool IsCasedUnit(char c) => char.ToLowerInvariant(c) != char.ToUpperInvariant(c);

    /// <summary>
    /// Reference's <c>anyRealLetter</c> class in <c>ts/utils.ts</c> — ASCII
    /// alphanumerics, the Latin-1/Latin-Extended-A ranges it names (U+00C0
    /// through U+00FF, and the four Hungarian double-acute letters U+0150,
    /// U+0151, U+0170, U+0171), and the surrogate range U+D800 through U+DFFF.
    ///
    /// <para>That last range is why this takes a single code <em>unit</em>: it
    /// lets a lone (unpaired) surrogate match, but it does <em>not</em> make an
    /// astral character alphanumeric, because the anchors in reference's
    /// unflagged <c>^[…]$</c> reject the two-unit sequence a paired surrogate
    /// forms. Kept faithful in both directions.</para>
    /// </summary>
    private static bool IsRealLetter(char c)
      => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
        || (c >= 0x00c0 && c <= 0x00ff)
        || c == 0x0150 || c == 0x0151 || c == 0x0170 || c == 0x0171
        || (c >= 0xd800 && c <= 0xdfff);

    /// <summary>
    /// Equality by canonical name, so <c>num is num</c> holds and the long and
    /// short spellings of one type are the same value (reference's <c>is()</c>,
    /// which compares <c>name</c> alone).
    /// </summary>
    public bool EqualsDatatype(DatatypeValue other) => other != null && other.Name == Name;

    public override bool Equals(object obj) => obj is DatatypeValue d && EqualsDatatype(d);

    public override int GetHashCode() => Name.GetHashCode();

    /// <summary>
    /// Save/load source form: the canonical name, which re-lexes to an equal
    /// value (reference's <c>toSource()</c>).
    /// </summary>
    public string ToSource() => Name;

    /// <summary>
    /// Renderer-facing form, reference's <c>print()</c>/<c>objectName</c>:
    /// <c>[the num datatype]</c>. Reference wraps this in verbatim markup so the
    /// brackets aren't read as a hook; we hand plain text to the render channel,
    /// which never re-parses it.
    ///
    /// <para>Deliberately <em>not</em> <see cref="ToString"/> — this is prose
    /// shown to a player, so it must be requested explicitly rather than
    /// leaking into a log line or debugger view. Same split
    /// <see cref="ColourValue"/> keeps with its <c>ToCssString</c>.</para>
    /// </summary>
    public string ToPrintedString() => "[the " + Name + " datatype]";

    /// <summary>Diagnostic form. For player-facing prose use <see cref="ToPrintedString"/>.</summary>
    public override string ToString() => "Datatype(" + Name + ")";
  }
}
