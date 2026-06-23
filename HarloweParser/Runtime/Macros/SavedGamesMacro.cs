using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(saved-games:)</c> → a Datamap of save slot → display filename for this
  /// story's saves (the backend's contents filtered to this story's IFID namespace).
  /// Empty when nothing is saved or no session is wired. Backs save-menu UIs —
  /// <c>(saved-games:)'s "Slot 1"</c> gives that slot's filename.
  /// </summary>
  public class SavedGamesMacro : IMacro
  {
    public string Name => "saved-games";
    public int MinArgs => 0;
    public int MaxArgs => 0;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var map = new Dictionary<string, HarloweValue>();
      if (context.SavedGames != null)
        foreach (var kv in context.SavedGames())
          map[kv.Key] = HarloweValue.OfString(kv.Value);
      return HarloweValue.OfDatamap(map);
    }
  }
}
