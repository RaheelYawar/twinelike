using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(live: 2s)[…]</c>. A changer whose hook doesn't show when it's written,
  /// then renders once the delay has passed and again after every delay,
  /// replacing what it showed, until <c>(stop:)</c> runs in it. Time passes only
  /// when the host calls <see cref="StorySession.Tick(double)"/>. Reference's entry in
  /// <c>ts/macrolib/stylechangers.ts</c>, with <c>optional(Number)</c>: no
  /// delay, or 0, renders every frame, as reference's missing delay does.
  /// </summary>
  public class LiveMacro : IMacro
  {
    public string Name => "live";
    public int MinArgs => 0;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      double delay = 0;
      if (args.Count == 1)
      {
        if (args[0].Kind != HarloweValueKind.Number)
          return HarloweValue.OfError($"The (live:) macro's 1st value is {args[0].ObjectName}, but should be (optional) a number.");
        delay = double.IsNaN(args[0].AsNumber) ? 0 : args[0].AsNumber;
      }
      return HarloweValue.OfChanger(Changer.FromPatches(new LivePatch { Delay = delay }));
    }
  }

  /// <summary>
  /// <c>(after: 2s)[…]</c> or <c>(after: 2s, 200ms)[…]</c>. A changer whose hook
  /// doesn't show when it's written, then renders once, on the first frame
  /// that <c>time</c> is past the delay, so it counts from when the passage was
  /// entered, not from where the macro runs. On each frame the player holds a
  /// key or button, the second number comes off the delay. Reference's entry
  /// in <c>ts/macrolib/stylechangers.ts</c>, with
  /// <c>[positiveNumber, optional(nonNegativeNumber)]</c>.
  /// </summary>
  public class AfterMacro : IMacro
  {
    /// <summary>JavaScript's <c>Number.EPSILON</c>, the least value reference's <c>positiveNumber</c> range admits.</summary>
    private const double NumberEpsilon = 2.220446049250313e-16;

    public string Name => "after";
    public int MinArgs => 1;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Number || !(args[0].AsNumber >= NumberEpsilon))
        return HarloweValue.OfError($"The (after:) macro's 1st value is {args[0].ObjectName}, but should be a positive number.");
      double skip = 0;
      if (args.Count == 2)
      {
        if (args[1].Kind != HarloweValueKind.Number || !(args[1].AsNumber >= 0))
          return HarloweValue.OfError($"The (after:) macro's 2nd value is {args[1].ObjectName}, but should be (optional) a number between 0 and Infinity.");
        skip = args[1].AsNumber;
      }
      return HarloweValue.OfChanger(Changer.FromPatches(new AfterPatch { Delay = args[0].AsNumber, Skip = skip }));
    }
  }

  /// <summary>
  /// <c>(stop:)</c>. A command that ends the <c>(live:)</c> hook it renders in,
  /// and does nothing anywhere else. Reference's entry in
  /// <c>ts/macrolib/commands.ts</c> looks for the command among what the live
  /// hook rendered; here running it is counted. It can't have changers attached.
  /// </summary>
  public class StopMacro : IMacro
  {
    public string Name => "stop";
    public int MinArgs => 0;
    public int MaxArgs => 0;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (context != null) context.StopsRun++;
      return null;
    }
  }
}
