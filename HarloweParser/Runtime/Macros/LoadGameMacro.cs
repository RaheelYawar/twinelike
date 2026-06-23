using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(load-game: "Slot 1")</c>. A navigating side-effect (no Command type): on
  /// success it stages the deserialised timeline on
  /// <see cref="MacroContext.PendingLoad"/>, so the body renderer halts — as for
  /// <c>(goto:)</c> — and the session installs it and shows the loaded passage after
  /// the render. A missing slot or corrupt save renders an in-prose error (and the
  /// session records it in <c>LastLoadError</c> for the host).
  ///
  /// <para>A <c>(load-game:)</c> reached while rendering a just-loaded passage no-ops
  /// — the infinite-loop guard (reference's <c>section.loadedGame</c>): it catches a
  /// passage that directly reloads itself, while still permitting load→<c>(goto:)</c>→load
  /// (the navigation clears the guard).</para>
  /// </summary>
  public class LoadGameMacro : IMacro
  {
    public string Name => "load-game";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(load-game:) requires a string slot name, got {args[0].Kind}");
      string slot = args[0].AsString;

      // Loop guard: a (load-game:) while rendering a just-loaded passage would reload
      // forever — silently no-op it (reference does the same via loadedGame).
      if (context.LoadedGame) return null;

      if (context.PrepareLoad == null) return null; // no session wired (standalone render)

      var result = context.PrepareLoad(slot);
      if (!result.Ok)
        return HarloweValue.OfError(result.Error);

      context.PendingLoad = result;
      return null;
    }
  }
}
