using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A custom macro: reference's <c>ts/datatypes/custommacro.ts</c>. Either a
  /// parameter list with a code hook body, made by <c>(macro:)</c>, or a macro
  /// with some of its values filled in, made by <c>(partial:)</c>.
  ///
  /// <para>Equality is by identity. Storing a macro that's already in a variable
  /// stores a copy, so <c>(set: $b to $a)(print: $a is $b)</c> is false, as in
  /// reference, where <c>VarRef.set()</c> clones one that has a known name.</para>
  /// </summary>
  public sealed class CustomMacroValue
  {
    /// <summary>The parameters in order. Empty for a <c>(partial:)</c> macro, whose <c>params</c> data name is an empty array.</summary>
    public readonly IReadOnlyList<TypedVarValue> Parameters;

    internal readonly CodeHookValue Body;

    /// <summary>For a partial of a built-in macro, its name as given.</summary>
    internal readonly string PartialName;

    /// <summary>For a partial of another custom macro, that macro.</summary>
    internal readonly CustomMacroValue PartialTarget;

    /// <summary>For a partial, the values it fills in.</summary>
    internal readonly List<HarloweValue> PrefilledArgs;

    /// <summary>The variable this was last stored in, used in error messages. Null until stored.</summary>
    internal string KnownName;

    internal bool IsPartial => PrefilledArgs != null;

    internal CustomMacroValue(List<TypedVarValue> parameters, CodeHookValue body)
    {
      Parameters = parameters;
      Body = body;
    }

    internal CustomMacroValue(string partialName, CustomMacroValue partialTarget, List<HarloweValue> prefilledArgs)
    {
      Parameters = new List<TypedVarValue>();
      PartialName = partialName;
      PartialTarget = partialTarget;
      PrefilledArgs = prefilledArgs;
    }

    private CustomMacroValue(CustomMacroValue other)
    {
      Parameters = other.Parameters;
      Body = other.Body;
      PartialName = other.PartialName;
      PartialTarget = other.PartialTarget;
      PrefilledArgs = other.PrefilledArgs;
    }

    /// <summary>A separate macro with the same definition and no known name.</summary>
    internal CustomMacroValue Copy() => new CustomMacroValue(this);

    /// <summary>The name used in type-check errors, reference's <c>the custom macro, $m</c>.</summary>
    internal string CallName => KnownName != null ? "the custom macro, " + KnownName : "an unnamed custom macro";

    /// <summary>Reference's <c>objectName</c>, also the text a macro prints as.</summary>
    internal string ObjectName
    {
      get
      {
        if (IsPartial)
        {
          if (PartialTarget != null && PartialTarget.KnownName == null)
            return "a (partial:) custom macro of another unnamed custom macro";
          return "a (partial:) custom macro of (" + (PartialName ?? PartialTarget.KnownName) + ":" + JoinSources(PrefilledArgs) + ")";
        }
        if (Parameters.Count == 0) return "a custom macro (with no params)";
        var names = new List<string>(Parameters.Count);
        for (int i = 0; i < Parameters.Count; i++) names.Add(Parameters[i].ToSource() ?? Parameters[i].VariableSource);
        return "a custom macro (with " + AndList(names) + ")";
      }
    }

    /// <summary>Source that recreates this macro, or null if part of it has none.</summary>
    public string ToSource() => ToSource(0);

    internal string ToSource(int depth)
    {
      var sb = new StringBuilder();
      if (IsPartial)
      {
        string target = PartialTarget != null ? PartialTarget.ToSource(depth + 1) : Twee.MarkupPrinter.StringLiteral(PartialName);
        if (target == null) return null;
        sb.Append("(partial:").Append(target);
        for (int i = 0; i < PrefilledArgs.Count; i++)
        {
          string arg = PrefilledArgs[i].ToSource(depth + 1);
          if (arg == null) return null;
          sb.Append(',').Append(arg);
        }
        return sb.Append(')').ToString();
      }

      sb.Append("(macro:");
      for (int i = 0; i < Parameters.Count; i++)
      {
        string parameter = Parameters[i].ToSource(depth + 1);
        if (parameter == null) return null;
        sb.Append(parameter).Append(',');
      }
      return sb.Append(Body.Source).Append(')').ToString();
    }

    private static string JoinSources(List<HarloweValue> values)
    {
      var sb = new StringBuilder();
      for (int i = 0; i < values.Count; i++)
      {
        if (i > 0) sb.Append(',');
        sb.Append(values[i].ToSource() ?? values[i].ToHarloweString());
      }
      return sb.ToString();
    }

    /// <summary>Reference's <c>andList</c>: "a and b", or "a, b and c".</summary>
    private static string AndList(List<string> items)
    {
      if (items.Count == 1) return items[0];
      var sb = new StringBuilder();
      for (int i = 0; i < items.Count - 1; i++)
      {
        if (i > 0) sb.Append(", ");
        sb.Append(items[i]);
      }
      return sb.Append(" and ").Append(items[items.Count - 1]).ToString();
    }
  }
}
