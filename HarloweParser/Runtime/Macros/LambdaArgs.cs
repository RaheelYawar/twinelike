using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Shared helpers for lambda-consuming macros. Centralises the
  /// "is this one array or many items" decision so every collection macro
  /// behaves the same way.
  /// </summary>
  internal static class LambdaArgs
  {
    /// <summary>
    /// Turn the tail of an argument list (from <paramref name="startIndex"/>
    /// onwards) into a flat sequence of items. The convention: if the tail is
    /// exactly one Array, unpack it; otherwise treat each arg as an item.
    /// This lets <c>(find: _x where _x &gt; 5, (a: 1, 2, 6, 7))</c> work
    /// without the spread operator (deferred to a later slice).
    /// </summary>
    public static List<HarloweValue> ExpandItems(List<HarloweValue> args, int startIndex)
    {
      int tailCount = args.Count - startIndex;
      if (tailCount == 1 && args[startIndex].Kind == HarloweValueKind.Array)
        return args[startIndex].AsArray;

      var items = new List<HarloweValue>(tailCount);
      for (int i = startIndex; i < args.Count; i++) items.Add(args[i]);
      return items;
    }
  }
}
