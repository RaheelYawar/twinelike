using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(replace: ?target)[new content]</c>. A revision changer: the attached
  /// hook is not rendered inline — instead it replaces the content of every
  /// node the target resolves to in the render tree built so far. The target
  /// is a hook name (<c>?cake</c>) or a literal string to find among rendered
  /// prose. A target that hasn't been rendered yet (declared after this macro)
  /// matches nothing, so nothing happens — Harlowe behaves the same way.
  /// </summary>
  public class ReplaceMacro : IMacro
  {
    public string Name => "replace";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => RevisionChangers.Build(args[0], RevisionMode.Replace, "(replace:)");
  }
}
