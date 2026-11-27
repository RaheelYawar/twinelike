using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(ds: 1, 2, 3)</c>, also <c>(dataset:)</c>. Builds a dataset, keeping the
  /// first of any repeated value. Reference's entry in
  /// <c>ts/macrolib/datastructures.ts</c>.
  /// </summary>
  public class DatasetMacro : IMacro
  {
    private readonly string _name;

    public DatasetMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 0;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => HarloweValue.OfDataset(new List<HarloweValue>(args));
  }
}
