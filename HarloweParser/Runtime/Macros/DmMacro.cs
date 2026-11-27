using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(dm: "name", "Sam", "hp", 10)</c>. Builds a datamap from alternating
  /// name / value pairs. A name is a string or a number, and reference's
  /// <c>isValidDatamapName</c> also refuses a string beside the number it
  /// reads as, so <c>(dm: 1, "a", "1", "b")</c> is an error. Using the same
  /// name twice in one call is an error too (reference's <c>map.has(key)</c>
  /// check), as is an odd argument count. An empty argument list makes an
  /// empty datamap.
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

      var map = new Dictionary<DataName, HarloweValue>(args.Count / 2);
      var value = HarloweValue.OfDatamap(map);
      for (int i = 0; i < args.Count; i += 2)
      {
        if (args[i].IsError) return args[i];
        if (args[i + 1].IsError) return args[i + 1];
        var nameError = ExpressionEvaluator.CheckDataName(value, args[i], out var key);
        if (nameError != null) return nameError;
        if (map.ContainsKey(key))
          return HarloweValue.OfError($"You used the same data name ({key.ObjectName}) twice in the same (dm:) call.");
        map[key] = args[i + 1];
      }
      return value;
    }
  }
}
