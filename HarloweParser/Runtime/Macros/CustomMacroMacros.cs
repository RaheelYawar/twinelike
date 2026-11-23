using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(macro: [...TypedVar], CodeHook)</c>: builds a custom macro. The checks
  /// and their wording are reference's <c>ts/macrolib/custommacros.ts</c>.
  /// </summary>
  public class MacroMacro : IMacro
  {
    public string Name => "macro";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      for (int i = 0; i < args.Count; i++)
      {
        if (args[i].Kind != HarloweValueKind.TypedVar && args[i].Kind != HarloweValueKind.CodeHook)
          return HarloweValue.OfError(
            $"The (macro:) macro's {Ordinal(i + 1)} value is {args[i].ObjectName}, but should be a TypedVar (typed variable name) or a code hook.");
      }

      var parameters = new List<TypedVarValue>(args.Count - 1);
      var names = new HashSet<string>();
      for (int i = 0; i < args.Count; i++)
      {
        bool last = i == args.Count - 1;
        bool isTypedVar = args[i].Kind == HarloweValueKind.TypedVar;
        if (isTypedVar == last)
        {
          string position = last ? string.Empty : Ordinal(args.Count - i + 1) + "-";
          string wanted = last ? "code hook" : "datatyped variable";
          return HarloweValue.OfError($"The {position}last value given to (macro:) should be a {wanted}, not {args[i].ObjectName}");
        }
        if (last) break;

        var parameter = args[i].AsTypedVar;
        if (!parameter.IsTemporary)
          return HarloweValue.OfError("A custom macro's typed variables must be temp variables (with a '_'), not global variables (with a '$').");
        if (parameter.IsRest && i != args.Count - 2)
          return HarloweValue.OfError("A custom macro can only have one spread variable, and it must be its last variable.");
        if (!names.Add(parameter.Name))
          return HarloweValue.OfError($"A custom macro's typed variables can't both be named '{parameter.Name}'.");
        parameters.Add(parameter);
      }
      return HarloweValue.OfCustomMacro(new CustomMacroValue(parameters, args[args.Count - 1].AsCodeHook));
    }

    private static string Ordinal(int n)
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

  /// <summary>
  /// <c>(output-data: Any)</c>, also <c>(out-data:)</c>. Returns a command, as in
  /// reference: it gives the running custom macro its result when it runs in
  /// prose, which also stops the rest of the body.
  /// </summary>
  public class OutputDataMacro : IMacro
  {
    private readonly string _name;

    public OutputDataMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => HarloweValue.OfCommand(CommandValue.ForOutputData(args[0]));
  }

  /// <summary>
  /// <c>(output:)</c>, also <c>(out:)</c>: a changer whose attached hook becomes
  /// the running custom macro's result, as a command that renders the hook. The
  /// renderer does the capturing, because it's the one holding the hook.
  /// </summary>
  public class OutputMacro : IMacro
  {
    private readonly string _name;

    public OutputMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 0;
    public int MaxArgs => 0;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => HarloweValue.OfChanger(Changer.FromPatches(new OutputPatch()));
  }

  /// <summary><c>(error: String)</c>: a command that ends the running custom macro with this message.</summary>
  public class ErrorMacro : IMacro
  {
    public string Name => "error";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (args[0].Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"The (error:) macro's 1st value is {args[0].ObjectName}, but should be a string.");
      if (args[0].AsString.Length == 0)
        return HarloweValue.OfError("This (error:) macro was given an empty string.");
      return HarloweValue.OfCommand(CommandValue.ForError(args[0].AsString));
    }
  }

  /// <summary>
  /// <c>(partial: String or CustomMacro, [...Any])</c>: a custom macro that calls
  /// another macro with these values first. Unlike reference this doesn't copy
  /// the target's type signature, so a call missing a required value is reported
  /// by the target macro when it runs.
  /// </summary>
  public class PartialMacro : IMacro
  {
    /// <summary>Partials of partials call and print recursively, so a story can't be allowed to stack them without limit.</summary>
    private const int MaxChain = 64;

    public string Name => "partial";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var prefilled = args.GetRange(1, args.Count - 1);
      var target = args[0];
      if (target.Kind == HarloweValueKind.CustomMacro)
      {
        int chain = 1;
        for (var m = target.AsCustomMacro; m.PartialTarget != null; m = m.PartialTarget) chain++;
        if (chain > MaxChain)
          return HarloweValue.OfError("(partial:) was given a macro already made from too many other (partial:) macros.");
        return HarloweValue.OfCustomMacro(new CustomMacroValue(null, target.AsCustomMacro, prefilled));
      }
      if (target.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"The (partial:) macro's 1st value is {target.ObjectName}, but should be a string or a custom macro.");

      string name = target.AsString;
      bool builtIn = !MacroNames.IsCustomCall(name)
        && MacroNames.Normalize(name) != MacroNames.Normalize(CustomCommandMacro.MacroName)
        && context.Invoker != null && context.Invoker.Contains(name);
      if (!builtIn)
        return HarloweValue.OfError($"The macro name given to (partial:), \"{name}\", isn't the name of a built-in macro.");
      return HarloweValue.OfCustomMacro(new CustomMacroValue(name, null, prefilled));
    }
  }

  /// <summary>
  /// Engine-only: how a saved custom command is read back, since reference's
  /// source form for one wouldn't restore the variables it captured. Only the
  /// save loader's context can run it, so to a story it doesn't exist.
  /// </summary>
  public class CustomCommandMacro : IMacro
  {
    internal const string MacroName = "custom-command";

    public string Name => MacroName;
    public int MinArgs => 3;
    public int MaxArgs => 3;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      if (context == null || !context.IsRestoringSave)
        return HarloweValue.OfError($"I can't run the macro '{MacroName}' because it doesn't exist.");
      if (args[0].Kind != HarloweValueKind.Changer || args[1].Kind != HarloweValueKind.CodeHook
          || args[2].Kind != HarloweValueKind.Datamap)
        return HarloweValue.OfError("this saved custom command is malformed");
      return HarloweValue.OfCommand(CommandValue.ForHook(args[0].AsChanger, args[1].AsCodeHook, args[2].AsDatamap));
    }
  }
}
