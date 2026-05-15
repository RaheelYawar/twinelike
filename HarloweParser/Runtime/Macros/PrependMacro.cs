using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(prepend: ?target)[extra content]</c>. A revision changer: the attached
  /// hook is rendered into a detached subtree and spliced in <em>before</em>
  /// the existing content of every node the target resolves to. The target is
  /// a hook name or a literal string to find among rendered prose. See
  /// <see cref="ReplaceMacro"/> for the shared revision semantics.
  /// </summary>
  public class PrependMacro : IMacro
  {
    public string Name => "prepend";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => RevisionChangers.Build(args[0], RevisionMode.Prepend, "(prepend:)");
  }
}
