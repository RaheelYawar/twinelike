using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(dm: "name", "Sam", "hp", 10)</c>. Builds a datamap from alternating
  /// <c>String</c> key / value pairs. An odd argument count is rejected; a
  /// non-string in a key position is rejected. Empty argument list is allowed
  /// and produces an empty datamap.
  /// </summary>
  public class DmMacro : IMacro
  {
    public string Name => "dm";
    public int MinArgs => 0;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args.Count % 2 != 0)
        return HarloweValue.OfError("(dm:) requires an even number of arguments (key, value pairs)");

      var map = new Dictionary<string, HarloweValue>(args.Count / 2);
      for (int i = 0; i < args.Count; i += 2)
      {
        if (args[i].IsError) return args[i];
        if (args[i + 1].IsError) return args[i + 1];
        if (args[i].Kind != HarloweValueKind.String)
          return HarloweValue.OfError($"(dm:) keys must be Strings; argument {i} is a {args[i].Kind}");
        map[args[i].AsString] = args[i + 1];
      }
      return HarloweValue.OfDatamap(map);
    }
  }
}
