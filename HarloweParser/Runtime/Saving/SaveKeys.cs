using System;

namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// Translates between a story-author's save <em>slot</em> name and the
  /// IFID-namespaced <em>storage key</em> the library writes to an
  /// <see cref="ISaveStorage"/>. Mirrors reference Harlowe, which prefixes its
  /// localStorage key with the story's IFID (<c>"(Saved Game &lt;ifid&gt;) &lt;slot&gt;"</c>)
  /// so distinct stories sharing one backend don't clobber each other's saves.
  /// A story with no IFID uses the bare slot as the key (the documented
  /// collision-prone case).
  /// </summary>
  public static class SaveKeys
  {
    private const string Open = "(Saved Game ";
    private const string Close = ") ";

    /// <summary>Slot → storage key. Bare slot when <paramref name="ifid"/> is null/empty; otherwise <c>"(Saved Game &lt;ifid&gt;) &lt;slot&gt;"</c>.</summary>
    public static string ToStorageKey(string ifid, string slot)
    {
      slot = slot ?? string.Empty;
      if (string.IsNullOrEmpty(ifid)) return slot;
      return Open + ifid + Close + slot;
    }

    /// <summary>
    /// Storage key → slot, the inverse of <see cref="ToStorageKey"/>: true with the
    /// slot out-param when <paramref name="key"/> belongs to <paramref name="ifid"/>'s
    /// story, false otherwise (a save from a different story on a shared backend).
    /// With an empty IFID every key is treated as this story's (the bare-key case).
    /// </summary>
    public static bool TryGetSlot(string ifid, string key, out string slot)
    {
      if (key == null) { slot = null; return false; }
      if (string.IsNullOrEmpty(ifid)) { slot = key; return true; }
      string prefix = Open + ifid + Close;
      if (key.StartsWith(prefix, StringComparison.Ordinal))
      {
        slot = key.Substring(prefix.Length);
        return true;
      }
      slot = null;
      return false;
    }
  }
}
