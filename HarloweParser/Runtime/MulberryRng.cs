using System;
using System.Globalization;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Mulberry32 PRNG seeded with MurmurHash3, ported from reference Harlowe's
  /// <c>ts/state/prng.ts</c> (the <c>mulberryMurmur32</c> function). The seed is a
  /// string; <c>(random:)</c>/<c>(either:)</c> draw through <see cref="NextDouble"/>.
  /// The whole state is (<see cref="Seed"/>, <see cref="SeedIter"/>) and
  /// reconstructs in O(1) — <c>h = murmur(seed) + 0x6D2B79F5 * iter</c> — so a
  /// saved or rewound stream resumes exactly without replay.
  ///
  /// <para>
  /// Implemented with native 32-bit <see cref="uint"/> wrapping arithmetic (inside
  /// <c>unchecked</c> blocks), which reproduces JavaScript's bitwise 32-bit
  /// semantics directly — uint multiply keeps the low 32 bits like <c>Math.imul</c>,
  /// and uint <c>&gt;&gt;</c> is the logical shift JS spells <c>&gt;&gt;&gt;</c>.
  /// Reference accumulates its counter in a JS <see cref="double"/> that only grows;
  /// this wraps at 2^32 instead. The two are bit-identical for any realistic session
  /// and would diverge only after ~5 million draws taken without an intervening
  /// save/load — the point where reference's float counter starts rounding, a regime
  /// no story reaches. Verified against reference-generated vectors in
  /// <c>MulberryRngTests</c>.
  /// </para>
  /// </summary>
  public class MulberryRng : IRng
  {
    private string _seed;
    private int _seedIter;
    private uint _h;

    /// <summary>Time-seeded, non-reproducible — the per-session default.</summary>
    public MulberryRng() : this(DefaultSeed(), 0) { }

    /// <summary>Seed from an explicit string, at stream position 0.</summary>
    public MulberryRng(string seed) : this(seed, 0) { }

    /// <summary>
    /// Seed from an integer by its invariant decimal string, so
    /// <c>new StorySession(story, 42)</c> is reproducible. (Reference seeds with a
    /// string; we map the int to one rather than inventing a second hash path.)
    /// </summary>
    public MulberryRng(int seed) : this(seed.ToString(CultureInfo.InvariantCulture), 0) { }

    /// <summary>Restore a saved state: seed plus a stream position.</summary>
    public MulberryRng(string seed, int seedIter) { SetSeed(seed, seedIter); }

    public string Seed => _seed;
    public int SeedIter => _seedIter;

    public void SetSeed(string seed, int seedIter)
    {
      _seed = seed ?? string.Empty;
      _seedIter = seedIter;
      _h = InitialState(_seed, seedIter);
    }

    public double NextDouble()
    {
      _seedIter += 1;
      unchecked
      {
        _h += 0x6D2B79F5u;                 // reference: let t = h += 0x6D2B79F5
        uint t = _h;
        t = (t ^ (t >> 15)) * (t | 1u);
        t ^= t + (t ^ (t >> 7)) * (t | 61u);
        return (t ^ (t >> 14)) / 4294967296.0;
      }
    }

    /// <summary>
    /// MurmurHash3 over the seed's UTF-16 code units, then the mulberry32 base
    /// state offset by <paramref name="iter"/> — reference's <c>mulberryMurmur32</c>
    /// body up to (and including) the <c>h = (h &gt;&gt;&gt; 0) + 0x6D2B79F5 * iter</c>
    /// line, in wrapping uint arithmetic.
    /// </summary>
    private static uint InitialState(string s, int iter)
    {
      unchecked
      {
        uint h = 2166136261u;
        for (int i = 0; i < s.Length; i++)
        {
          uint k = (uint)s[i] * 3432918353u;
          k = (k << 15) | (k >> 17);
          h ^= k * 461845907u;
          h = (h << 13) | (h >> 19);
          h = h * 5u + 3864292196u;
        }
        h ^= (uint)s.Length;
        h ^= h >> 16; h *= 2246822507u;
        h ^= h >> 13; h *= 3266489909u;
        h ^= h >> 16;
        return h + 0x6D2B79F5u * (uint)iter;
      }
    }

    /// <summary>
    /// A clock-derived single-character seed, kept inside the BMP and below the
    /// surrogate range so it is a valid, JSON-serialisable string. Non-reproducible
    /// by design (mirrors reference seeding from <c>Date.now()</c>); only explicit
    /// seeds need to round-trip.
    /// </summary>
    private static string DefaultSeed()
    {
      long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
      int cp = (int)(((ms % 0xD800) + 0xD800) % 0xD800);
      return ((char)cp).ToString();
    }
  }
}
