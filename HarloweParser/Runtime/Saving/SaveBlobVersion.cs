namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// Version stamp for the save-blob format. Written into every blob and checked on
  /// load — <see cref="SaveSerializer.DeserialiseTimeline"/> refuses a blob from a
  /// newer version (forward-incompatible) rather than misreading it. Blob
  /// interchange with browser Harlowe is a non-goal, so this is our own scheme.
  /// </summary>
  public static class SaveBlobVersion
  {
    /// <summary>The blob format version this build writes and can read.</summary>
    public const int Current = 1;
  }
}
