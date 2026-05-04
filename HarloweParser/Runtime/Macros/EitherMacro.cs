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
      var rng = context.Rng ?? new Random();
      return args[rng.Next(args.Count)];
    }
  }
}
