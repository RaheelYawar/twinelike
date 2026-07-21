using System.Globalization;

namespace Harlowe
{
  /// <summary>
  /// A compatibility profile: the set of behaviours that belong to one Harlowe
  /// major version. The library delivers per-major lock-in — a story keeps the
  /// semantics of the major it declares, indefinitely — and this type is where
  /// each deliberate difference between majors is spelled out as a switch.
  ///
  /// <para>One engine, append-only profiles. Every switch is a get-only
  /// <c>bool</c> instance property, and adding a major means adding a value for
  /// it on every existing profile rather than rewriting the older ones. Only
  /// differences reference Harlowe made <em>intentionally</em> between majors
  /// earn a switch: our own bugs are fixed under every profile, reference's
  /// bugfixes apply under every profile, and macros new in a later major stay
  /// registered under earlier ones (under real 3.x an unknown macro was an
  /// in-prose error, so no shipped story can depend on its absence).</para>
  ///
  /// <para>A switch declared here with nothing reading it is caught by
  /// <c>CompatibilityProfileTests</c>, which reflects over these properties and
  /// demands a behavioural probe per switch — so adding a switch means adding
  /// the proof it does something.</para>
  /// </summary>
  public sealed class HarloweProfile
  {
    /// <summary>
    /// Whether <c>--</c> is comment markup. Harlowe 4.0 added the
    /// <c>comment</c> rule (<c>ts/markup/patterns.ts</c>), which eliminates the
    /// element that follows it; under 3.x the same characters are ordinary
    /// prose, so <c>it was -- and remains -- fine</c> renders whole and
    /// <c>5--3</c> is <c>5 - (-3)</c> = 8. Consumed by
    /// <see cref="Tokens.HarloweTokenizer"/> at three sites — the body-mode and
    /// expression-mode dispatch arms and the prose-run break in
    /// <c>ScanText</c>. The <c>&lt;!-- … --&gt;</c> HTML comment form exists in
    /// both majors and is not gated by this switch.
    /// </summary>
    public bool CommentMarkup { get; private set; }

    private HarloweProfile() { }

    /// <summary>Harlowe 3 semantics, pinned to 3.3.9 — the last 3.x release. Twine binds a story to the newest version within its major, so a 3.2-authored story has been running under 3.3.9 rules for years; lock-in is per major, not per minor.</summary>
    public static readonly HarloweProfile V3 = new HarloweProfile
    {
      CommentMarkup = false,
    };

    /// <summary>Harlowe 4 semantics, tracking the 4.0-unstable branch until 4.0 releases.</summary>
    public static readonly HarloweProfile V4 = new HarloweProfile
    {
      CommentMarkup = true,
    };

    /// <summary>
    /// The newest profile the library knows about. Used when a story declares
    /// no format version, or declares one from a major newer than any we
    /// implement — in both cases the story is likelier to want current
    /// semantics than a historical reconstruction.
    /// <para><b>This moves</b> when a new major is added, which is what makes
    /// it wrong for anything that must stay stable across releases — see
    /// <see cref="SaveFormat"/>.</para>
    /// </summary>
    public static readonly HarloweProfile Latest = V4;

    /// <summary>
    /// The profile save blobs are re-lexed under.
    /// <b>Never follows the story.</b> A blob's value sources are emitted by
    /// <c>HarloweValue.ToSource()</c>, so they are engine-written, not
    /// author-written, and author compatibility policy has no bearing on them.
    /// Following the story would silently re-lex existing saves under different
    /// rules the moment an author bumped <c>format-version</c> in Twine — data
    /// loss for no benefit. Deliberately not an alias of
    /// <see cref="Latest"/>, which moves; changing this constant is a
    /// save-format break and requires a <c>SaveBlobVersion.Current</c> bump.
    /// </summary>
    public static readonly HarloweProfile SaveFormat = V4;

    /// <summary>
    /// Selects the profile for a story's declared format version (the
    /// <c>format-version</c> attribute of <c>&lt;tw-storydata&gt;</c>, or the
    /// same key in a Twee <c>:: StoryData</c> block). Only the leading integer
    /// major is read — <c>"3.3.9"</c>, <c>"3"</c> and <c>"3.0.0-beta"</c> all
    /// select <see cref="V3"/>.
    ///
    /// <para>Absent, unparseable, or from a major newer than we implement
    /// selects <see cref="Latest"/>. A major <em>below</em> 3 also selects
    /// <see cref="V3"/> rather than the newest: the lock-in promise starts at
    /// 3.x because 1.x/2.x were never audited here, but a 2.x story's prose is
    /// exactly as likely to contain <c>--</c> em-dashes as a 3.x story's, so
    /// clamping down preserves that prose where defaulting up would eat it.
    /// <see cref="GetCompatibilityNotices"/> on the story reports each of these
    /// cases; nothing here throws.</para>
    /// </summary>
    public static HarloweProfile Resolve(string formatVersion)
    {
      int major = ParseMajor(formatVersion);
      if (major < 0) return Latest;   // absent or unparseable
      if (major <= 3) return V3;      // 3.x, and the below-3 clamp
      if (major == 4) return V4;
      return Latest;                  // a major newer than we implement
    }

    /// <summary>
    /// Reads the leading run of ASCII digits as the major version, returning
    /// -1 when there isn't one. Deliberately lenient about what follows —
    /// anything from <c>"3.3.9"</c> to <c>"4.0.0-unstable"</c> parses — because
    /// the trailing text never affects which major's semantics apply. The digit
    /// test is ASCII-only for the same reason the tokenizer's is: <c>char.IsDigit</c>
    /// accepts non-ASCII decimal digits, which <c>int.Parse</c> here would
    /// then have to make sense of.
    /// </summary>
    internal static int ParseMajor(string formatVersion)
    {
      if (string.IsNullOrEmpty(formatVersion)) return -1;

      int i = 0;
      while (i < formatVersion.Length && formatVersion[i] >= '0' && formatVersion[i] <= '9') i++;
      if (i == 0) return -1;

      // A version string long enough to overflow an int isn't a version string.
      if (i > 9) return int.MaxValue;
      return int.Parse(formatVersion.Substring(0, i), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The newest major this type reproduces. Read by
    /// <see cref="Harlowe.GetCompatibilityNotices"/> to tell "from a newer
    /// major than we implement" apart from "a major we implement".
    /// </summary>
    internal const int LatestKnownMajor = 4;

    /// <summary>Diagnostic name — <c>"Harlowe 3"</c> / <c>"Harlowe 4"</c> — for notice messages and test failure output.</summary>
    public override string ToString()
    {
      if (ReferenceEquals(this, V3)) return "Harlowe 3";
      if (ReferenceEquals(this, V4)) return "Harlowe 4";
      return "Harlowe (unknown profile)";
    }
  }
}
