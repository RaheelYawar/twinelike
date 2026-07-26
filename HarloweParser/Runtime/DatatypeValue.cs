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
  /// <para>Immutable, so a datatype can be stored, copied between variables, and
  /// captured in a save without any aliasing risk — the same reasoning as
  /// <see cref="ColourValue"/>.</para>
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

    /// <summary>True iff <paramref name="word"/> is a datatype name (case-insensitive).</summary>
    public static bool IsNamed(string word) => word != null && Spellings.ContainsKey(word);

    /// <summary>
    /// Build a datatype from a lexed name, canonicalising the spelling. Returns
    /// null for anything that isn't a datatype name — the tokenizer only emits
    /// names that pass <see cref="IsNamed"/>, so null means a caller bug.
    /// </summary>
    public static DatatypeValue FromLexeme(string lexeme)
      => lexeme != null && Spellings.TryGetValue(lexeme, out var canonical)
        ? new DatatypeValue(canonical)
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
        case HarloweValueKind.Array: return new DatatypeValue("array");
        case HarloweValueKind.Datamap: return new DatatypeValue("dm");
        case HarloweValueKind.Datatype: return new DatatypeValue("datatype");
        case HarloweValueKind.Changer: return new DatatypeValue("changer");
        case HarloweValueKind.Colour: return new DatatypeValue("colour");
        case HarloweValueKind.Lambda: return new DatatypeValue("lambda");
        case HarloweValueKind.String: return new DatatypeValue("str");
        case HarloweValueKind.Number: return new DatatypeValue("num");
        case HarloweValueKind.Bool: return new DatatypeValue("bool");
      }
      return null;
    }

    /// <summary>
    /// Does <paramref name="value"/> belong to this type? Reference's
    /// <c>isTypeOf</c> over the <c>typeIndex</c> table in
    /// <c>ts/datatypes/datatype.ts</c>. An unknown name — which here means a
    /// name for a value type this library doesn't implement — is false, as in
    /// reference (<c>typeIndex[name] ? … : false</c>).
    /// </summary>
    public bool IsTypeOf(HarloweValue value)
    {
      if (value == null) return false;
      switch (Name)
      {
        // Reference's `any` matches anything; `const` matches everything too
        // (its real work is a special case in VarRef's set(), and this arm
        // exists "only for destructuring").
        case "any":
        case "const":
          return true;

        case "array": return value.Kind == HarloweValueKind.Array;
        case "dm": return value.Kind == HarloweValueKind.Datamap;
        case "datatype": return value.Kind == HarloweValueKind.Datatype;
        case "changer": return value.Kind == HarloweValueKind.Changer;
        case "colour": return value.Kind == HarloweValueKind.Colour;
        case "lambda": return value.Kind == HarloweValueKind.Lambda;
        case "str": return value.Kind == HarloweValueKind.String;
        case "num": return value.Kind == HarloweValueKind.Number;
        case "bool": return value.Kind == HarloweValueKind.Bool;

        case "even": return IsParity(value, 0);
        case "odd": return IsParity(value, 1);

        // Reference tests `obj === (obj|0)`, a *32-bit* truncation, so a whole
        // number beyond int32 range is not an `int` there either. Matched
        // deliberately — an author's `$n is an int` guard should agree across
        // implementations even at the edges. NaN and the infinities fail the
        // same test.
        case "int":
          return value.Kind == HarloweValueKind.Number
            && value.AsNumber >= int.MinValue && value.AsNumber <= int.MaxValue
            && value.AsNumber == (int)value.AsNumber;

        case "empty":
          switch (value.Kind)
          {
            case HarloweValueKind.String: return value.AsString.Length == 0;
            case HarloweValueKind.Array: return value.AsArray.Count == 0;
            case HarloweValueKind.Datamap: return value.AsDatamap.Count == 0;
          }
          return false;

        case "uppercase": return IsSingleCasedCodePoint(value, requireUpper: true);
        case "lowercase": return IsSingleCasedCodePoint(value, requireUpper: false);
        case "anycase": return IsSingleCasedCodePoint(value, requireUpper: null);

        case "whitespace": return IsSingleCodePointWhere(value, IsRealWhitespace);
        case "digit": return IsSingleCodePointWhere(value, s => s.Length == 1 && s[0] >= '0' && s[0] <= '9');
        case "alnum": return IsSingleCodePointWhere(value, IsRealLetter);

        // The one type that can match two characters: reference's `anyNewline`
        // alternates \n, \r, and \r\n, so a CRLF pair is a single line break.
        case "linebreak":
          return value.Kind == HarloweValueKind.String
            && (value.AsString == "\n" || value.AsString == "\r" || value.AsString == "\r\n");
      }

      // A name whose value type isn't implemented here (ds, gradient, image,
      // macro, command, codehook, measure): nothing can match it.
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
    /// True when the value is a string of exactly one code point that changes
    /// under case conversion — reference's <c>uppercase</c>/<c>lowercase</c>
    /// (a character differing from its own lower/upper form) and <c>anycase</c>
    /// (one whose lower and upper forms differ from each other). Invariant
    /// casing per code point, the same conversion <c>(uppercase:)</c> and
    /// <c>(lowercase:)</c> use — which is the consistency reference notes for
    /// these types.
    /// </summary>
    private static bool IsSingleCasedCodePoint(HarloweValue value, bool? requireUpper)
    {
      if (value.Kind != HarloweValueKind.String) return false;
      string s = value.AsString;
      if (CodePoints.Count(s) != 1) return false;
      string lower = s.ToLowerInvariant();
      string upper = s.ToUpperInvariant();
      if (requireUpper == null) return lower != upper;
      return requireUpper.Value ? s != lower : s != upper;
    }

    private static bool IsSingleCodePointWhere(HarloweValue value, Func<string, bool> predicate)
      => value.Kind == HarloweValueKind.String
        && CodePoints.Count(value.AsString) == 1
        && predicate(value.AsString);

    /// <summary>
    /// Reference's <c>realWhitespace</c> class in <c>ts/utils.ts</c>: "all forms
    /// of Unicode 6 whitespace … except Ogham space mark" — space, the five
    /// ASCII whitespace controls, U+00A0, U+2000 through U+200A, U+2028, U+2029,
    /// U+202F, U+205F, U+3000. Spelled out rather than deferring to
    /// <see cref="char.IsWhiteSpace"/>, which also accepts U+1680 OGHAM SPACE
    /// MARK and U+0085 NEL.
    /// </summary>
    private static bool IsRealWhitespace(string s)
    {
      if (s.Length != 1) return false;
      char c = s[0];
      return c == ' ' || c == '\n' || c == '\r' || c == '\f' || c == '\t' || c == '\v'
        || c == '\u00a0' || (c >= '\u2000' && c <= '\u200a')
        || c == '\u2028' || c == '\u2029' || c == '\u202f' || c == '\u205f' || c == '\u3000';
    }

    /// <summary>
    /// Reference's <c>anyRealLetter</c> class in <c>ts/utils.ts</c> — ASCII
    /// alphanumerics plus the Latin-1/Latin-Extended-A ranges it names
    /// (U+00C0 through U+00FF, and the four Hungarian double-acute letters
    /// U+0150, U+0151, U+0170, U+0171), plus any astral character: the class
    /// ends with the whole surrogate range, so a code point above U+FFFF is
    /// alphanumeric there by construction.
    /// </summary>
    private static bool IsRealLetter(string s)
    {
      if (s.Length == 2) return char.IsSurrogatePair(s, 0);
      if (s.Length != 1) return false;
      char c = s[0];
      return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
        || (c >= '\u00c0' && c <= '\u00ff')
        || c == '\u0150' || c == '\u0151' || c == '\u0170' || c == '\u0171';
    }

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
    /// </summary>
    public override string ToString() => "[the " + Name + " datatype]";
  }
}
