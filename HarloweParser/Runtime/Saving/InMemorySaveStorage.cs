using System.Collections.Generic;

namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// The default <see cref="ISaveStorage"/>: a plain in-process dictionary. Saves
  /// live for the lifetime of this instance and are <b>not persisted</b> — a host
  /// that wants saves to survive the process supplies its own backend (a file, the
  /// browser's <c>localStorage</c>, a Unity/Godot save system, …). Useful as-is for
  /// tests, previews, and stories whose undo/redo-style "saves" only need to last the
  /// session.
  /// </summary>
  public class InMemorySaveStorage : ISaveStorage
  {
    private sealed class Slot
    {
      public string Blob;
      public string Filename;
    }

    private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();

    public bool TryWrite(string key, string blob, string filename)
    {
      if (key == null) return false;
      _slots[key] = new Slot { Blob = blob, Filename = filename };
      return true;
    }

    public bool TryRead(string key, out string blob)
    {
      if (key != null && _slots.TryGetValue(key, out var slot)) { blob = slot.Blob; return true; }
      blob = null;
      return false;
    }

    public bool TryDelete(string key) => key != null && _slots.Remove(key);

    public IEnumerable<SavedGameInfo> Enumerate()
    {
      foreach (var kv in _slots)
        yield return new SavedGameInfo { Key = kv.Key, Filename = kv.Value.Filename };
    }
  }
}
