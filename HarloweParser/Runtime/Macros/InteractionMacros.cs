using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// The click/hover interaction macros — twelve thin wrappers over one
  /// builder. Each macro is just a <see cref="InteractionKind"/> plus a
  /// <see cref="RevisionMode"/>:
  /// <list type="bullet">
  /// <item><c>(click:)</c> = <c>(click-replace:)</c> = Click + Replace.</item>
  /// <item><c>(click-append:)</c> / <c>(click-prepend:)</c> = Click + Append/Prepend.</item>
  /// <item><c>(mouseover:)</c> / <c>(mouseout:)</c> + <c>-replace</c>/<c>-append</c>/<c>-prepend</c> combos.</item>
  /// </list>
  /// All take a hook-name target and an attached hook (the deferred prose
  /// fired on the user event). Grouped in one file since each class differs
  /// only by two constants over the shared shape.
  /// </summary>
  internal static class InteractionChangers
  {
    /// <summary>
    /// Validate <paramref name="arg"/> and build the interaction changer, or
    /// return an in-prose error if the argument isn't a hook name. String
    /// targets are deferred to a follow-up — for this sub-slice, click/hover
    /// macros take a hook-name target only.
    /// </summary>
    public static HarloweValue Build(HarloweValue arg, InteractionKind kind, RevisionMode mode, string macroName)
    {
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.HookName)
        return HarloweValue.OfError($"{macroName} requires a hook name, got {arg.Kind}");
      var spec = new InteractionSpec { HookTarget = arg.AsHookName, Kind = kind, Mode = mode };
      return HarloweValue.OfChanger(Changer.FromInteraction(spec));
    }
  }

  // --- Click family ------------------------------------------------------

  /// <summary><c>(click: ?target)[deferred]</c>. Wraps the target clickably; on click, the deferred hook replaces the target's content. Equivalent to <c>(click-replace:)</c>.</summary>
  public class ClickMacro : IMacro
  {
    public string Name => "click";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.Click, RevisionMode.Replace, "(click:)");
  }

  /// <summary><c>(click-replace: ?target)[deferred]</c>. Explicit name for the replace-on-click form; identical to <c>(click:)</c>.</summary>
  public class ClickReplaceMacro : IMacro
  {
    public string Name => "click-replace";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.Click, RevisionMode.Replace, "(click-replace:)");
  }

  /// <summary><c>(click-append: ?target)[deferred]</c>. On click, the deferred hook is appended after the target's existing content.</summary>
  public class ClickAppendMacro : IMacro
  {
    public string Name => "click-append";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.Click, RevisionMode.Append, "(click-append:)");
  }

  /// <summary><c>(click-prepend: ?target)[deferred]</c>. On click, the deferred hook is prepended before the target's existing content.</summary>
  public class ClickPrependMacro : IMacro
  {
    public string Name => "click-prepend";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.Click, RevisionMode.Prepend, "(click-prepend:)");
  }

  // --- MouseOver family --------------------------------------------------

  /// <summary><c>(mouseover: ?target)[deferred]</c>. Fires on hover-enter; replaces the target's content with the deferred hook.</summary>
  public class MouseOverMacro : IMacro
  {
    public string Name => "mouseover";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOver, RevisionMode.Replace, "(mouseover:)");
  }

  /// <summary><c>(mouseover-replace: ?target)[deferred]</c>. Explicit replace-on-hover form; identical to <c>(mouseover:)</c>.</summary>
  public class MouseOverReplaceMacro : IMacro
  {
    public string Name => "mouseover-replace";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOver, RevisionMode.Replace, "(mouseover-replace:)");
  }

  /// <summary><c>(mouseover-append: ?target)[deferred]</c>. On hover-enter, appends the deferred hook to the target.</summary>
  public class MouseOverAppendMacro : IMacro
  {
    public string Name => "mouseover-append";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOver, RevisionMode.Append, "(mouseover-append:)");
  }

  /// <summary><c>(mouseover-prepend: ?target)[deferred]</c>. On hover-enter, prepends the deferred hook to the target.</summary>
  public class MouseOverPrependMacro : IMacro
  {
    public string Name => "mouseover-prepend";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOver, RevisionMode.Prepend, "(mouseover-prepend:)");
  }

  // --- MouseOut family ---------------------------------------------------

  /// <summary><c>(mouseout: ?target)[deferred]</c>. Fires on hover-leave; replaces the target's content with the deferred hook.</summary>
  public class MouseOutMacro : IMacro
  {
    public string Name => "mouseout";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOut, RevisionMode.Replace, "(mouseout:)");
  }

  /// <summary><c>(mouseout-replace: ?target)[deferred]</c>. Explicit replace-on-hover-leave form; identical to <c>(mouseout:)</c>.</summary>
  public class MouseOutReplaceMacro : IMacro
  {
    public string Name => "mouseout-replace";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOut, RevisionMode.Replace, "(mouseout-replace:)");
  }

  /// <summary><c>(mouseout-append: ?target)[deferred]</c>. On hover-leave, appends the deferred hook to the target.</summary>
  public class MouseOutAppendMacro : IMacro
  {
    public string Name => "mouseout-append";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOut, RevisionMode.Append, "(mouseout-append:)");
  }

  /// <summary><c>(mouseout-prepend: ?target)[deferred]</c>. On hover-leave, prepends the deferred hook to the target.</summary>
  public class MouseOutPrependMacro : IMacro
  {
    public string Name => "mouseout-prepend";
    public int MinArgs => 1; public int MaxArgs => 1;
    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], InteractionKind.MouseOut, RevisionMode.Prepend, "(mouseout-prepend:)");
  }
}
