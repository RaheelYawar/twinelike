using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Aggregated description of how a single hook should render. Each
  /// <see cref="Changer"/> in the composition pipeline mutates fields here via
  /// its patches; the renderer executes against the finished descriptor.
  ///
  /// <para>
  /// Modelled after the descriptor object in the reference Harlowe JS
  /// implementation, where every changer macro is a function that takes a
  /// descriptor and updates it (styles, source content, transitions,
  /// loopVars, etc.). Keeping that shape here lets future changer kinds —
  /// transitions, source-rewriting <c>(replace:)</c>, hook-name targeting —
  /// drop in by adding fields here and a new patch type, without re-breaking
  /// <see cref="Changer.Apply"/>.
  /// </para>
  /// </summary>
  public class HookDescriptor
  {
    /// <summary>Styling layers in apply order — outermost first.</summary>
    public List<StyleSpec> Styles = new List<StyleSpec>();

    /// <summary>
    /// When set, the renderer iterates the hook contents once per item,
    /// binding <see cref="IterationParamName"/> (and the <c>it</c> slot) to
    /// each item in turn. Null for non-loop changers.
    /// </summary>
    public IterationSpec Iteration;
  }

  /// <summary>
  /// The loop instruction a <c>(for:)</c> changer leaves on a descriptor.
  /// Names the parameter to bind (and its sigil) plus the items to iterate.
  /// </summary>
  public class IterationSpec
  {
    public LambdaValue Lambda;
    public List<HarloweValue> Items;
    public string ParamName;
    public bool ParamIsTemporary;
  }
}
