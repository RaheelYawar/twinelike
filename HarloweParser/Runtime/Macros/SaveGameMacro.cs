using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(save-game: "Slot 1", ["My File"])</c> → Boolean. Serialises the current
  /// timeline to the session's save backend under the slot, with an optional display
  /// filename (defaulting to the slot name). Returns <c>true</c> on success, or
  /// <c>false</c> when saving is disabled, the state holds an unsaveable value, or the
  /// backend refuses — matching reference Harlowe's Boolean result, so authors can
  /// branch on it (<c>(if: (save-game: "A"))[Saved!]</c>). A standalone render with no
  /// session wired always reports false.
  /// </summary>
  public class SaveGameMacro : IMacro
  {
    public string Name => "save-game";
    public int MinArgs => 1;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(save-game:) requires a string slot name, got {args[0].Kind}");
      string slot = args[0].AsString;

      string filename = null;
      if (args.Count > 1)
      {
        if (args[1].Kind != HarloweValueKind.String)
          return HarloweValue.OfError($"(save-game:)'s filename must be a string, got {args[1].Kind}");
        filename = args[1].AsString;
      }

      bool ok = context.SaveGame != null && context.SaveGame(slot, filename);
      return HarloweValue.OfBool(ok);
    }
  }
}
