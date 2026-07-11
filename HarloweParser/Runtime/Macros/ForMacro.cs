using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(for: each _x, items...)</c>. Body-position iteration changer:
  /// produced as a Changer carrying an <see cref="IterationSpec"/> patch.
  /// When the renderer applies this changer to an attached hook, the hook
  /// contents are rendered once per item, with the lambda's parameter (and
  /// the <c>it</c> slot) bound to that item.
  ///
  /// <para>
  /// Requires an <c>each</c>-form lambda — <c>where</c>/<c>via</c>/<c>making</c>
  /// lambdas error in-prose. Accepts zero or more items; with no items the hook
  /// is simply not printed (matching reference: "if no extra values are given …
  /// nothing will happen and the attached hook will not be printed at all"),
  /// which lets authors loop over a possibly-empty array without guarding.
  /// Auto-iterates a single trailing array arg the same way the other
  /// lambda-consuming macros do (see <see cref="FindMacro"/>).
  /// </para>
  ///
  /// <para>Registered under both <c>for</c> and <c>loop</c>.</para>
  /// </summary>
  public class ForMacro : IMacro
  {
    private readonly string _name;

    public ForMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.Lambda)
        return HarloweValue.OfError($"({_name}:) first argument must be a lambda");

      var lambda = args[0].AsLambda;
      if (lambda.Node == null || !lambda.Node.IsEach)
        return HarloweValue.OfError($"({_name}:) requires an 'each _x' lambda");
      if (string.IsNullOrEmpty(lambda.Node.ParameterName))
        return HarloweValue.OfError($"({_name}:) lambda must name its iteration variable");

      var items = LambdaArgs.ExpandItems(args, startIndex: 1);

      return HarloweValue.OfChanger(Changer.FromIteration(new IterationSpec
      {
        Lambda = lambda,
        Items = items,
        ParamName = lambda.Node.ParameterName,
        ParamIsTemporary = lambda.Node.ParameterIsTemporary
      }));
    }
  }
}
