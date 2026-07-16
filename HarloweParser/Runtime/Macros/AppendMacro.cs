using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(append: ?target, …)[extra content]</c>. A revision changer: the
  /// attached hook is rendered into a detached subtree and spliced in
  /// <em>after</em> the existing content of every node each target resolves
  /// to. Variadic; each target is a hook name or a literal string to find
  /// among rendered prose. See <see cref="ReplaceMacro"/> for the shared
  /// revision semantics.
  /// </summary>
  public class AppendMacro : IMacro
  {
    public string Name => "append";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => RevisionChangers.Build(args, RevisionMode.Append, "(append:)");
  }
}
