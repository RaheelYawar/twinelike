using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(a: 1, 2, 3)</c>. Constructs an array from its arguments. Empty
  /// argument list produces an empty array. Stock Harlowe also exposes this
  /// as <c>(array:)</c>; only the short name is registered here.
  /// </summary>
  public class AMacro : IMacro
  {
    public string Name => "a";
    public int MinArgs => 0;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var items = new List<HarloweValue>(args.Count);
      for (int i = 0; i < args.Count; i++)
      {
        if (args[i].IsError) return args[i];
        items.Add(args[i]);
      }
      return HarloweValue.OfArray(items);
    }
  }
}
