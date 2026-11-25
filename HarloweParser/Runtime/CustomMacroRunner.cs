using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Calls custom macros: reference's <c>Macros.runCustom</c> type check plus
  /// <c>macroEntryFn</c> in <c>ts/datatypes/custommacro.ts</c>. The body runs in
  /// its own temp scope, which can't see the caller's temps, into a render tree
  /// that's thrown away, so its prose never shows. Whatever it passes to
  /// <c>(output:)</c>, <c>(output-data:)</c> or <c>(error:)</c> is the result.
  /// </summary>
  internal static class CustomMacroRunner
  {
    private const string TooDeepMessage = "Printing this expression may have trapped me in an infinite loop.";

    private const string FirstErrorLabel = " The first was: ";

    /// <summary>
    /// The most expression levels that may be open when a custom macro starts.
    /// Each call can nest a whole expression inside the last, so limiting calls
    /// alone wouldn't bound the stack. This one holds on any runtime. Measured
    /// on .NET 8 with the stack check removed, 600 fits a 512 KB stack and 800
    /// doesn't, so this leaves margin for runtimes with larger frames.
    /// </summary>
    private const int MaxEvaluationDepth = 400;

    /// <summary>
    /// False when the thread is nearly out of stack, which a recursive macro
    /// would otherwise turn into a crash no host can catch. Where the runtime
    /// doesn't implement the check, <see cref="MaxEvaluationDepth"/> and the
    /// render nesting limit still apply.
    /// </summary>
    internal static bool HasStackRoom()
    {
      try
      {
        System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
        return true;
      }
      catch (System.InsufficientExecutionStackException)
      {
        return false;
      }
    }

    /// <summary>
    /// Calls <paramref name="target"/> with <paramref name="args"/>, already
    /// spread. Returns the macro's result or an error, never null.
    /// </summary>
    public static HarloweValue Call(HarloweValue target, List<HarloweValue> args, MacroRegistry registry, MacroContext context)
    {
      if (target.IsError) return target;
      if (target.Kind != HarloweValueKind.CustomMacro)
        return HarloweValue.OfError($"I can't call {target.ObjectName} because it isn't a custom macro.");
      var macro = target.AsCustomMacro;
      return macro.IsPartial
        ? RunPartial(macro, args, registry, context)
        : RunBody(macro, args, registry, context);
    }

    private static HarloweValue RunPartial(CustomMacroValue macro, List<HarloweValue> args, MacroRegistry registry, MacroContext context)
    {
      var all = new List<HarloweValue>(macro.PrefilledArgs.Count + args.Count);
      all.AddRange(macro.PrefilledArgs);
      all.AddRange(args);

      HarloweValue result;
      if (macro.PartialTarget != null)
      {
        result = Call(HarloweValue.OfCustomMacro(macro.PartialTarget), all, registry, context);
      }
      else if (HarloweExpressionParser.IsAssignmentMacro(macro.PartialName))
      {
        // Their values have to be `to`/`into` expressions, which can't be passed as data.
        result = HarloweValue.OfError($"The ({macro.PartialName}:) macro can't be called through (partial:).");
      }
      else
      {
        result = registry.Invoke(macro.PartialName, all, context);
        ExpressionEvaluator.StampChangerSource(result, registry.RegisteredName(macro.PartialName), all);
      }
      return result.IsError
        ? HarloweValue.OfError($"An error occurred while running the (partial:)-created macro, {macro.ObjectName}:\n{result.ErrorMessage}")
        : result;
    }

    private static HarloweValue RunBody(CustomMacroValue macro, List<HarloweValue> args, MacroRegistry registry, MacroContext context)
    {
      var typeError = CheckArguments(macro, args, context);
      if (typeError != null) return typeError;

      if (registry.EvaluationDepth > MaxEvaluationDepth || !HasStackRoom())
        return HarloweValue.OfError(TooDeepMessage);
      var refusal = context.EnterCustomMacro(macro.Body.Source.Length);
      if (refusal == NestedRenderRefusal.TooDeep)
        return HarloweValue.OfError(TooDeepMessage);
      if (refusal == NestedRenderRefusal.OverBudget)
        return HarloweValue.OfError("This custom macro stopped: " + MacroContext.OverBudgetMessage);

      var frame = new CustomMacroFrame();
      var priorFrame = context.CurrentCustomMacro;
      var priorConditional = context.LastConditional;
      var priorOutput = context.Output;
      var sealedOff = new RenderTreeBuilder();
      try
      {
        context.CurrentCustomMacro = frame;
        context.LastConditional = null;
        // Otherwise a (display:) in an argument here would write to the caller's page.
        context.Output = null;
        using (context.Store.PushIsolatedTempScope())
        {
          BindParameters(macro, args, context.Store);
          new BodyRenderer(sealedOff, registry, context)
            .Render(new PassageBody { Children = macro.Body.ChildrenFor(context.Profile) });
        }
      }
      finally
      {
        context.CurrentCustomMacro = priorFrame;
        context.LastConditional = priorConditional;
        context.Output = priorOutput;
        context.ExitCustomMacro();
      }

      // Reference shows these behind an Open button. A host has no such thing,
      // so the first one's text is added to the summary. Through recursion that
      // would repeat once per level, so a nested summary passes on its cause.
      var errors = new List<RenderNode>();
      RenderNodes.CollectWhere(sealedOff.Root, n => n is RenderErrorNode, errors);
      if (errors.Count > 0)
      {
        string cause = ((RenderErrorNode)errors[0]).Message;
        int nested = cause.IndexOf(FirstErrorLabel, System.StringComparison.Ordinal);
        if (nested >= 0) cause = cause.Substring(nested + FirstErrorLabel.Length);
        return HarloweValue.OfError(
          $"{errors.Count} error{(errors.Count > 1 ? "s" : "")} occurred when running {macro.ObjectName}.{FirstErrorLabel}{cause}");
      }

      if (!frame.Done)
        return HarloweValue.OfError($"{Capitalised(macro.ObjectName)} didn't output any data or hooks using (output:) or (output-data:).");
      return frame.Output;
    }

    /// <summary>
    /// Reference's <c>typeCheckAndRun</c> against the macro's parameters. A spread
    /// parameter takes zero or more values, each of its type. Null when the
    /// arguments fit.
    /// </summary>
    private static HarloweValue CheckArguments(CustomMacroValue macro, List<HarloweValue> args, MacroContext context)
    {
      var parameters = macro.Parameters;
      bool hasRest = parameters.Count > 0 && parameters[parameters.Count - 1].IsRest;
      int required = hasRest ? parameters.Count - 1 : parameters.Count;

      if (!hasRest && args.Count > parameters.Count)
        return HarloweValue.OfError($"{args.Count - parameters.Count} too many values were given to {macro.CallName}.");

      for (int i = 0; i < args.Count; i++)
      {
        var parameter = parameters[i < required ? i : required];
        if (!ExpressionEvaluator.Matches(args[i], parameter.Datatype, context))
          return HarloweValue.OfError(
            $"{Capitalised(macro.CallName)}'s {Nth(i + 1)} value is {args[i].ObjectName}, but should be {parameter.Datatype.ObjectName}.");
      }

      if (args.Count < required)
      {
        int missing = required - args.Count;
        return HarloweValue.OfError(
          $"{Capitalised(macro.CallName)} was given {DescribeGiven(args)}, but needs {missing} more value{(missing > 1 ? "s" : "")}.");
      }
      return null;
    }

    private static void BindParameters(CustomMacroValue macro, List<HarloweValue> args, IVariableStore store)
    {
      for (int i = 0; i < macro.Parameters.Count; i++)
      {
        var parameter = macro.Parameters[i];
        if (parameter.IsRest)
        {
          var rest = i < args.Count ? args.GetRange(i, args.Count - i) : new List<HarloweValue>();
          store.DefineType(parameter.Name, true, HarloweValue.OfDatatype(DatatypeValue.FromLexeme("array")));
          store.SetInCurrentScope(parameter.Name, true, HarloweValue.OfArray(rest));
        }
        else
        {
          store.DefineType(parameter.Name, true, parameter.Datatype);
          store.SetInCurrentScope(parameter.Name, true, args[i]);
        }
      }
    }

    private static string DescribeGiven(List<HarloweValue> args)
    {
      if (args.Count == 0) return "nothing";
      var parts = new string[args.Count];
      for (int i = 0; i < args.Count; i++) parts[i] = args[i].ObjectName;
      return string.Join(", ", parts);
    }

    private static string Capitalised(string text)
      => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    private static string Nth(int n)
    {
      int hundreds = n % 100;
      if (hundreds >= 11 && hundreds <= 13) return n + "th";
      switch (n % 10)
      {
        case 1: return n + "st";
        case 2: return n + "nd";
        case 3: return n + "rd";
        default: return n + "th";
      }
    }
  }
}
