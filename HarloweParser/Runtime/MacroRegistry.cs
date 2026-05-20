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

    /// <summary>Add or replace a macro under its declared <see cref="IMacro.Name"/>. Re-registration is allowed so tests can swap implementations.</summary>
    public void Register(IMacro macro)
    {
      _macros[macro.Name] = macro;
    }

    /// <summary>True if a macro with this name is registered. Used by the body renderer to disambiguate command macros from "macro-shaped" prose.</summary>
    public bool Contains(string name) => _macros.ContainsKey(name);

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
      if (!_macros.TryGetValue(name, out var macro))
        return HarloweValue.OfError($"unknown macro '{name}'");

      int got = args != null ? args.Count : 0;
      if (got < macro.MinArgs)
        return HarloweValue.OfError($"macro '{name}' requires at least {macro.MinArgs} argument(s); got {got}");
      if (macro.MaxArgs >= 0 && got > macro.MaxArgs)
        return HarloweValue.OfError($"macro '{name}' accepts at most {macro.MaxArgs} argument(s); got {got}");

      var result = macro.Invoke(args ?? new List<HarloweValue>(), context);
      return result ?? HarloweValue.OfString(string.Empty);
    }
  }
}
