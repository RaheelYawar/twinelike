namespace Harlowe.Runtime
{
  /// <summary>
  /// Seedable pseudo-random source backing <c>(random:)</c>/<c>(either:)</c> (and
  /// the future <c>(seed:)</c>). The entire state is captured by
  /// (<see cref="Seed"/>, <see cref="SeedIter"/>) and reconstructable in O(1) via
  /// <see cref="SetSeed"/>, so a saved game can reproduce a random stream exactly
  /// across save/load and undo/redo. <see cref="MulberryRng"/> is the production
  /// implementation (reference Harlowe's mulberry32 + MurmurHash3).
  /// </summary>
  public interface IRng
  {
    /// <summary>Next value in the half-open interval <c>[0, 1)</c>; advances <see cref="SeedIter"/> by one.</summary>
    double NextDouble();

    /// <summary>The seed string the current stream was initialised from.</summary>
    string Seed { get; }

    /// <summary>
    /// Number of draws taken since the seed was last (re)set — the serialisable
    /// stream position. Combined with <see cref="Seed"/> it pins the exact state.
    /// </summary>
    int SeedIter { get; }

    /// <summary>
    /// Reseed and fast-forward to a stream position in one step: equivalent to
    /// seeding with <paramref name="seed"/> then taking <paramref name="seedIter"/>
    /// draws, but O(1) (the generator state is a closed function of the two).
    /// Used to restore a saved or rewound RNG state.
    /// </summary>
    void SetSeed(string seed, int seedIter);
  }
}
