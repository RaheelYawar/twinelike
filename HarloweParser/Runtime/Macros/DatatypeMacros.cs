using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(datatype: Any)</c> → the datatype matching the given value:
  /// <c>(datatype: 2)</c> is <c>num</c>, <c>(datatype: (a: 1))</c> is
  /// <c>array</c>. Reference's entry in <c>ts/macrolib/values.ts</c>, which
  /// delegates to <c>Datatype.from</c>.
  ///
  /// <para>Only "general" types come back, as reference documents: a value is
  /// never described by a subset type, so <c>(datatype: 2)</c> is <c>num</c> and
  /// not <c>even</c>, and a string of digits is <c>str</c> and not
  /// <c>digit</c>.</para>
  ///
  /// <para>A value with no datatype name — a HookName — is an in-prose error,
  /// which is the behaviour reference's macro article promises.</para>
  /// </summary>
  public class DatatypeMacro : IMacro
  {
    public string Name => "datatype";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var datatype = DatatypeValue.From(args[0]);
      if (datatype == null)
        return HarloweValue.OfError($"(datatype:) can't produce a datatype for a {args[0].Kind}");
      return HarloweValue.OfDatatype(datatype);
    }
  }

  /// <summary>
  /// <c>(datapattern: Any)</c> → the same value with every leaf replaced by its
  /// datatype, so <c>(datapattern: (a: 15, 45))</c> is <c>(a: num, num)</c> — a
  /// pattern precise enough to hand to <c>matches</c>, where
  /// <c>(datatype:)</c> would only have said <c>array</c>. Reference's entry in
  /// <c>ts/macrolib/values.ts</c>, whose <c>recur</c> walks arrays and datamaps
  /// and calls <c>Datatype.from</c> on everything else.
  ///
  /// <para>Structure is preserved exactly, never generalised: an array of 26
  /// numbers produces <c>num</c> 26 times, because the spread datatype that
  /// would shorten it is deliberately not produced (and isn't writable here
  /// yet either).</para>
  /// </summary>
  public class DatapatternMacro : IMacro
  {
    public string Name => "datapattern";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    /// <summary>
    /// Matches the recursion caps elsewhere (the parsers, <c>ToSource</c>,
    /// <c>DeepCopyValue</c>): past this depth the walk reports an in-prose
    /// error rather than risking an uncatchable
    /// <see cref="System.StackOverflowException"/>.
    /// </summary>
    private const int MaxDepth = 256;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => Recur(args[0], 0);

    private static HarloweValue Recur(HarloweValue value, int depth)
    {
      if (depth >= MaxDepth)
        return HarloweValue.OfError("(datapattern:) was given a value nested too deeply");

      if (value.Kind == HarloweValueKind.Array)
      {
        var src = value.AsArray;
        var dst = new List<HarloweValue>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
          var item = Recur(src[i], depth + 1);
          if (item.IsError) return item;
          dst.Add(item);
        }
        return HarloweValue.OfArray(dst);
      }

      if (value.Kind == HarloweValueKind.Datamap)
      {
        var src = value.AsDatamap;
        var dst = new Dictionary<string, HarloweValue>(src.Count);
        foreach (var kv in src)
        {
          var item = Recur(kv.Value, depth + 1);
          if (item.IsError) return item;
          dst[kv.Key] = item;
        }
        return HarloweValue.OfDatamap(dst);
      }

      var datatype = DatatypeValue.From(value);
      if (datatype == null)
        return HarloweValue.OfError($"(datapattern:) can't produce a datatype for a {value.Kind}");
      return HarloweValue.OfDatatype(datatype);
    }
  }
}
