using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(passage:)</c> or <c>(passage: "Cellar")</c> → a datamap of that
  /// passage's <c>name</c>, <c>source</c> and <c>tags</c>. With no name, or an
  /// empty one, it describes the current passage, which inside a
  /// <c>(display:)</c> is still the one being visited. Reference's entry in
  /// <c>ts/macrolib/datastructures.ts</c>.
  ///
  /// <para>A passage with no tags has one empty tag, because reference splits
  /// the HTML attribute on whitespace, and an empty attribute splits into one
  /// empty string. A run of spaces in a hand-edited attribute would add more
  /// empty tags there, which this doesn't. The metadata keys from
  /// <c>(storylet:)</c> and <c>(metadata:)</c> aren't here, as those macros
  /// aren't implemented.</para>
  /// </summary>
  public class PassageMacro : IMacro
  {
    public string Name => "passage";
    public int MinArgs => 0;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      string name = null;
      if (args.Count == 1)
      {
        if (args[0].Kind != HarloweValueKind.String)
          return HarloweValue.OfError($"The (passage:) macro's 1st value should be a string, not {args[0].ObjectName}.");
        name = args[0].AsString;
      }
      if (string.IsNullOrEmpty(name))
      {
        // The session describes the current passage as a datamap with its name.
        var current = context.EvaluationContext?.Passage;
        name = current != null && current.Kind == HarloweValueKind.Datamap
            && current.AsDatamap.TryGetValue("name", out var currentName)
          ? currentName.AsString
          : string.Empty;
      }

      var passage = context.FindPassage?.Invoke(name);
      if (passage == null)
        return HarloweValue.OfError($"There's no passage named '{name}' in this story.");

      var tags = new List<HarloweValue>();
      if (passage.Tags != null)
        for (int i = 0; i < passage.Tags.Count; i++) tags.Add(HarloweValue.OfString(passage.Tags[i]));
      if (tags.Count == 0) tags.Add(HarloweValue.OfString(string.Empty));

      return HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        { "name", HarloweValue.OfString(passage.Name) },
        { "source", HarloweValue.OfString(passage.RawBody) },
        { "tags", HarloweValue.OfArray(tags) },
      });
    }
  }
}
