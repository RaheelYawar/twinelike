namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// Version stamp for the save-blob format. Written into every blob and checked on
  /// load — <see cref="SaveSerializer.DeserialiseTimeline"/> refuses a blob from a
  /// newer version (forward-incompatible) rather than misreading it. Blob
  /// interchange with browser Harlowe is a non-goal, so this is our own scheme.
  ///
  /// <para><b>A blob is stamped with the lowest version that can represent it</b>,
  /// not always <see cref="Current"/>. A timeline that uses no version-2 feature is
  /// written as <see cref="Original"/>, byte-identical to what builds predating
  /// version 2 wrote, so a session that never touches the newer features keeps
  /// producing saves those builds can still load. Only a blob that actually carries
  /// a newer feature is stamped newer — and an older build then refuses it cleanly
  /// rather than loading it and silently dropping the data it can't see.</para>
  /// </summary>
  public static class SaveBlobVersion
  {
    /// <summary>The original format: passage, variable delta, redirect trail, and RNG state per turn.</summary>
    public const int Original = 1;

    /// <summary>Adds host turn data (<see cref="Moment.TurnData"/>). Written only when at least one saved turn carries some.</summary>
    public const int TurnData = 2;

    /// <summary>Adds variables removed by <c>(move:)</c>, written as JSON <c>null</c> in a turn's variables. Written only when at least one saved turn removed one.</summary>
    public const int DeletedVariables = 3;

    /// <summary>Adds type restrictions on story variables (<see cref="Moment.TypeDelta"/>). Written only when at least one saved turn defined one.</summary>
    public const int TypeRestrictions = 4;

    /// <summary>The newest blob format this build can read.</summary>
    public const int Current = TypeRestrictions;
  }
}
