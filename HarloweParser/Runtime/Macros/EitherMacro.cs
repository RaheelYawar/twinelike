using System;
using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(either: "rock", "paper", "scissors")</c>. Returns one of its
  /// arguments uniformly at random. Uses <see cref="MacroContext.Rng"/>.
  /// </summary>
  public class EitherMacro : IMacro
  {
    public string Name => "either";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      for (int i = 0; i < args.Count; i++)
      {
        if (args[i].IsError) return args[i];
      }
      // Index = floor(draw * count), matching reference's args[~~(State.random()
      // * args.length)] (ts/macrolib/values.ts). count is small, so the (int)
      // cast can't overflow.
      var rng = context.Rng ?? new MulberryRng();
      return args[(int)(rng.NextDouble() * args.Count)];
    }
  }
}
