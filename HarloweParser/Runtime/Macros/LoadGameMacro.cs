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
  /// <para>A <c>(load-game:)</c> reached while rendering a just-loaded passage is an
  /// error — the infinite-loop guard, matching reference's <c>section.loadedGame</c>
  /// (<c>"I can't use (load-game:) immediately after loading a game."</c>). It catches
  /// a passage that directly reloads itself. The guard clears on a navigation to a new
  /// turn (a host-driven <see cref="StorySession.Goto"/> / link click), so
  /// load→navigate→load is permitted. An in-passage <c>(goto:)</c> is an intra-turn
  /// redirect here (not a fresh passage as in reference) and keeps the guard armed —
  /// a known, deliberate divergence from reference.</para>
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
      // forever — error instead, matching reference's loadedGame guard.
      if (context.LoadedGame)
        return HarloweValue.OfError("I can't use (load-game:) immediately after loading a game.");

      if (context.PrepareLoad == null) return null; // no session wired (standalone render)

      var result = context.PrepareLoad(slot);
      if (!result.Ok)
        return HarloweValue.OfError(result.Error);

      context.PendingLoad = result;
      return null;
    }
  }
}
