using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Directory of <see cref="IMacro"/>s plus the arity-checking dispatcher.
  /// Also implements <see cref="IMacroInvoker"/> so the
  /// <see cref="ExpressionEvaluator"/> can call value macros without depending
  /// on the registry's full surface. The session sets <see cref="Context"/>
  /// before a render; <see cref="Invoke(string, List{HarloweValue})"/> then
  /// uses that context for every dispatch.
  /// </summary>
  public class MacroRegistry : IMacroInvoker
  {
    private readonly Dictionary<string, IMacro> _macros = new Dictionary<string, IMacro>();

    /// <summary>The context handed to every macro the registry dispatches. Set by the session before each render; tests set it directly.</summary>
    public MacroContext Context;

    /// <summary>
    /// How many expressions are being evaluated at once, across every evaluator
    /// sharing this registry. A recursive custom macro multiplies expression
    /// depth by call depth, so <see cref="CustomMacroRunner"/> checks this. The
    /// session zeroes it before each render and dispatch.
    /// </summary>
    internal int EvaluationDepth;

    /// <summary>Add or replace a macro under its declared <see cref="IMacro.Name"/>, normalized so case/dash/underscore variants resolve to it. Re-registration is allowed so tests can swap implementations.</summary>
    public void Register(IMacro macro)
    {
      _macros[MacroNames.Normalize(macro.Name)] = macro;
    }

    /// <summary>True if a macro with this name is registered (case/dash-insensitive). Used by the body renderer to disambiguate command macros from "macro-shaped" prose.</summary>
    public bool Contains(string name) => _macros.ContainsKey(MacroNames.Normalize(name));

    /// <summary>Every registered macro name in <see cref="MacroNames.Normalize"/>d form — the lookup keys.</summary>
    public IEnumerable<string> RegisteredNames => _macros.Keys;

    /// <summary>
    /// Dispatch a macro call using the context most recently assigned to
    /// <see cref="Context"/>. Returns an in-prose error if the macro is
    /// unknown or the arity is wrong; otherwise returns whatever the macro
    /// returned (which may be null for command macros — translated to an empty
    /// String here so the value-macro caller path stays well-typed).
    ///
    /// <para>Throws <see cref="System.InvalidOperationException"/> if
    /// <see cref="Context"/> has not been assigned. The session sets it before
    /// each render, and tests set it after constructing the registry; an NRE
    /// from a macro that touches the context is otherwise the first symptom,
    /// which buries the actual bug. Use the
    /// <see cref="Invoke(string, List{HarloweValue}, MacroContext)"/> overload
    /// when you'd rather pass the context explicitly.</para>
    /// </summary>
    public HarloweValue Invoke(string name, List<HarloweValue> args)
    {
      if (Context == null)
        throw new System.InvalidOperationException(
          "MacroRegistry.Context has not been set. Assign Context before dispatching, or use the (name, args, context) overload.");
      return Invoke(name, args, Context);
    }

    /// <summary>
    /// Dispatch with an explicit context. Used by the body renderer, which
    /// builds a fresh context per render and prefers passing it directly over
    /// mutating the registry's <see cref="Context"/> field.
    /// </summary>
    public HarloweValue Invoke(string name, List<HarloweValue> args, MacroContext context)
    {
      var callError = CheckCall(name, args);
      if (callError != null) return callError;

      var result = _macros[MacroNames.Normalize(name)].Invoke(args ?? new List<HarloweValue>(), context);
      return result ?? HarloweValue.OfString(string.Empty);
    }

    /// <summary>
    /// What would stop this call before the macro runs: an unknown name, the
    /// wrong number of arguments, or an invalid argument. Null when it can run.
    /// </summary>
    internal HarloweValue CheckCall(string name, List<HarloweValue> args)
    {
      if (!_macros.TryGetValue(MacroNames.Normalize(name), out var macro))
        return HarloweValue.OfError($"unknown macro '{name}'");

      int got = args != null ? args.Count : 0;
      if (got < macro.MinArgs)
        return HarloweValue.OfError($"macro '{name}' requires at least {macro.MinArgs} argument(s); got {got}");
      if (macro.MaxArgs >= 0 && got > macro.MaxArgs)
        return HarloweValue.OfError($"macro '{name}' accepts at most {macro.MaxArgs} argument(s); got {got}");

      // Central error-argument propagation, mirroring reference Harlowe's
      // typeCheckAndRun ("where the majority of errors are propagated, freeing
      // up individual macros from the burden of doing so"): an error in any
      // argument short-circuits to that error instead of dispatching. Without
      // this, a macro that type-checks args[0] (the lambda-consuming macros)
      // masks an error in that slot behind its own "must be a lambda" message.
      if (args != null)
        for (int i = 0; i < args.Count; i++)
        {
          if (args[i].IsError) return args[i];
          // A typed variable name is unstorable, which reference's `Any` type
          // rejects. These macros take `Everything` instead, or take one by design.
          bool refused = args[i].Kind == HarloweValueKind.TypedVar
            ? !AcceptsTypedVars(MacroNames.Normalize(name))
            : args[i].Kind == HarloweValueKind.HookName && !(macro is ITakesHookNames);
          if (refused)
            return HarloweValue.OfError($"The ({name}:) macro's {i + 1}{OrdinalSuffix(i + 1)} value, {args[i].ObjectName}, is not valid data for this macro.");
        }
      return null;
    }

    /// <summary>What a built-in command reports when it's made, or null. See <see cref="ICommandCheck"/>.</summary>
    internal HarloweValue CheckCommand(string name, List<HarloweValue> args, MacroContext context)
      => context != null && _macros.TryGetValue(MacroNames.Normalize(name), out var macro) && macro is ICommandCheck command
        ? command.Check(args, context)
        : null;

    private static bool AcceptsTypedVars(string normalizedName)
      => normalizedName == "macro" || normalizedName == "partial" || normalizedName == "a"
        || normalizedName == "array" || normalizedName == "dm" || normalizedName == "datamap";

    private static string OrdinalSuffix(int n)
    {
      int hundreds = n % 100;
      if (hundreds >= 11 && hundreds <= 13) return "th";
      switch (n % 10)
      {
        case 1: return "st";
        case 2: return "nd";
        case 3: return "rd";
        default: return "th";
      }
    }
  }
}
