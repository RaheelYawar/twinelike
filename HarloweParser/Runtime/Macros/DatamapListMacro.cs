using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(dm-names:)</c>, <c>(dm-values:)</c> and <c>(dm-entries:)</c>: a
  /// datamap's names, its values, or a <c>(dm: "name", …, "value", …)</c> for
  /// each pair, as an array in the natural order of the names
  /// (<see cref="NaturalOrder.DatamapNames"/>). Reference's entries in
  /// <c>ts/macrolib/datastructures.ts</c>. A name keeps its type, so a number
  /// name comes back as a number. Names that sort as equal keep the datamap's
  /// order, except in <c>(dm-entries:)</c>, whose comparator puts them in
  /// reverse.
  /// </summary>
  public class DatamapListMacro : IMacro
  {
    /// <summary>Which part of each pair the array holds.</summary>
    public enum Part
    {
      Names,
      Values,
      Entries
    }

    private readonly string _name;
    private readonly Part _part;

    public DatamapListMacro(string name, Part part)
    {
      _name = name;
      _part = part;
    }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Datamap)
        return HarloweValue.OfError($"The ({_name}:) macro's 1st value is {args[0].ObjectName}, but should be a datamap.");

      var map = args[0].AsDatamap;
      var names = NaturalOrder.DatamapNames(map, equalNamesReversed: _part == Part.Entries);
      var result = new List<HarloweValue>(names.Count);
      foreach (var name in names)
      {
        switch (_part)
        {
          case Part.Names:
            result.Add(name.ToValue());
            break;
          case Part.Values:
            result.Add(map[name]);
            break;
          default:
            var entry = new Dictionary<DataName, HarloweValue>(2);
            entry[DataName.Of("name")] = name.ToValue();
            entry[DataName.Of("value")] = map[name];
            result.Add(HarloweValue.OfDatamap(entry));
            break;
        }
      }
      return HarloweValue.OfArray(result);
    }
  }
}
