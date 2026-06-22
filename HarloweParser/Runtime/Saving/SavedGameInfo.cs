namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// One entry from <see cref="ISaveStorage.Enumerate"/>: a stored save's
  /// <see cref="Key"/> (the full storage key, IFID-prefixed by the library) and its
  /// host-supplied <see cref="Filename"/> (display name). Filenames are stored
  /// alongside blobs so listing saves never has to read or parse a blob.
  /// </summary>
  public class SavedGameInfo
  {
    /// <summary>The storage key the save was written under (IFID-prefixed; see <see cref="SaveKeys"/>).</summary>
    public string Key;

    /// <summary>The display filename passed to <see cref="ISaveStorage.TryWrite"/>.</summary>
    public string Filename;
  }
}
