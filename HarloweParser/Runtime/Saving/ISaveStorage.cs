using System.Collections.Generic;

namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// Host-supplied persistence backend for <c>(save-game:)</c>/<c>(load-game:)</c>/
  /// <c>(saved-games:)</c> — the save-model analogue of <see cref="IRenderOutput"/>,
  /// constructor-injected into <see cref="StorySession"/>. A key-value store of
  /// opaque string blobs plus a display filename per save; the library handles
  /// serialisation and IFID key-prefixing (see <see cref="SaveKeys"/>), so this
  /// interface never sees a <see cref="HarloweValue"/>.
  ///
  /// <para><b>IFID / collision contract.</b> The library prefixes each slot key with
  /// the story's IFID before calling here, so distinct stories sharing one backend
  /// don't collide. A story with no IFID gets unprefixed keys — its saves <em>can</em>
  /// collide with another IFID-less story on the same backend; give every story an
  /// IFID to avoid this.</para>
  ///
  /// <para>The default backend is <see cref="InMemorySaveStorage"/> (session-lifetime,
  /// non-persistent). Passing <c>null</c> for the backend disables saving entirely —
  /// <c>(save-game:)</c> then returns <c>false</c>.</para>
  /// </summary>
  public interface ISaveStorage
  {
    /// <summary>Store <paramref name="blob"/> and <paramref name="filename"/> under <paramref name="key"/>, overwriting any existing entry. Returns false if the backend refused (e.g. quota exceeded) — <c>(save-game:)</c> then returns false.</summary>
    bool TryWrite(string key, string blob, string filename);

    /// <summary>Read the blob stored under <paramref name="key"/>. Returns false (and null <paramref name="blob"/>) if no save exists there.</summary>
    bool TryRead(string key, out string blob);

    /// <summary>Delete the save under <paramref name="key"/>. Returns false if there was nothing to delete. (Backs a future <c>(delete-save:)</c>.)</summary>
    bool TryDelete(string key);

    /// <summary>Enumerate every stored save's key + filename — cheaply, without reading blobs. Order is unspecified.</summary>
    IEnumerable<SavedGameInfo> Enumerate();
  }
}
