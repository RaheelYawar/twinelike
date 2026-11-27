using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>What a <see cref="CommandValue"/> does when it's run in prose.</summary>
  internal enum CommandKind
  {
    /// <summary><c>(output-data:)</c>: gives the running custom macro its result.</summary>
    OutputData,

    /// <summary><c>(error:)</c>: ends the running custom macro with an error.</summary>
    RaiseError,

    /// <summary>A custom command, reference's <c>CustomCommand</c>: what a custom macro using <c>(output:)</c> returns. Renders its hook.</summary>
    Hook,

    /// <summary><c>(link-goto:)</c>, which <c>[[…]]</c> in an expression is shorthand for. Renders the link.</summary>
    Link,

    /// <summary>Another built-in command made in an expression. Runs the macro with these arguments when it reaches prose.</summary>
    Builtin,
  }

  /// <summary>
  /// A command held as a value. It does nothing until it appears in prose, so
  /// <c>(set: $c to ($m:))$c $c</c> renders the hook twice. As in reference, a
  /// custom command is equal only to itself, and built-in ones compare by the
  /// source that would recreate them.
  /// </summary>
  public sealed class CommandValue
  {
    internal readonly CommandKind Kind;

    /// <summary>The value to output, or the error message as a string.</summary>
    internal readonly HarloweValue Data;

    /// <summary>For <see cref="CommandKind.Hook"/>, the changer that was attached to the hook, <c>(output:)</c> included.</summary>
    internal readonly Changer Changer;

    /// <summary>For <see cref="CommandKind.Hook"/>, the hook to render.</summary>
    internal readonly CodeHookValue Hook;

    /// <summary>For <see cref="CommandKind.Hook"/>, the temp variables the macro's code could see at <c>(output:)</c>.</summary>
    internal readonly Dictionary<string, HarloweValue> Temps;

    /// <summary>For <see cref="CommandKind.Link"/>, the link to render. A variable target resolves when it renders.</summary>
    internal readonly Ast.Body.LinkNode Link;

    /// <summary>For <see cref="CommandKind.Builtin"/>, the macro as the author spelled it.</summary>
    internal readonly string BuiltinName;

    /// <summary>For <see cref="CommandKind.Builtin"/>, the evaluated arguments.</summary>
    internal readonly List<HarloweValue> Arguments;

    private CommandValue(CommandKind kind, HarloweValue data, Changer changer, CodeHookValue hook,
      Dictionary<string, HarloweValue> temps, Ast.Body.LinkNode link = null,
      string builtinName = null, List<HarloweValue> arguments = null)
    {
      Kind = kind;
      Data = data;
      Changer = changer;
      Hook = hook;
      Temps = temps;
      Link = link;
      BuiltinName = builtinName;
      Arguments = arguments;
    }

    internal static CommandValue ForOutputData(HarloweValue value)
      => new CommandValue(CommandKind.OutputData, value, null, null, null);

    internal static CommandValue ForError(string message)
      => new CommandValue(CommandKind.RaiseError, HarloweValue.OfString(message), null, null, null);

    internal static CommandValue ForHook(Changer changer, CodeHookValue hook, Dictionary<string, HarloweValue> temps)
      => new CommandValue(CommandKind.Hook, null, changer, hook, temps);

    internal static CommandValue ForLink(string text, string target)
      => new CommandValue(CommandKind.Link, null, null, null, null, new Ast.Body.LinkNode { Text = text, Target = target });

    internal static CommandValue ForBuiltin(string name, List<HarloweValue> arguments)
      => new CommandValue(CommandKind.Builtin, null, null, null, null, null, name, arguments);

    /// <summary>Reference's <c>objectName</c> for this command.</summary>
    internal string ObjectName
    {
      get
      {
        switch (Kind)
        {
          case CommandKind.OutputData: return "a (output-data:) command";
          case CommandKind.RaiseError: return "a (error:) command";
          case CommandKind.Link: return "a (link-goto:) command";
          case CommandKind.Builtin: return "a (" + BuiltinCommands.CanonicalName(BuiltinName) + ":) command";
          default: return "a custom command";
        }
      }
    }

    /// <summary>What printing this command as data shows.</summary>
    internal string PrintedName
      => Kind == CommandKind.Hook ? "[a custom command]" : "[A" + ObjectName.Substring(1) + "]";

    internal bool EqualsCommand(CommandValue other)
    {
      if (ReferenceEquals(this, other)) return true;
      if (Kind == CommandKind.Hook || other.Kind == CommandKind.Hook) return false;
      string source = ToSource();
      return source != null && source == other.ToSource();
    }

    internal int CommandHash()
    {
      if (Kind == CommandKind.Hook) return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
      return ToSource()?.GetHashCode() ?? (int)Kind;
    }

    /// <summary>
    /// Source that recreates this command, or null if part of it has none. A
    /// custom command saves through an engine-only macro, because reference's
    /// form for it, the call that made it, wouldn't restore the variables it
    /// captured.
    /// </summary>
    public string ToSource() => ToSource(0);

    /// <summary>
    /// <paramref name="depth"/> continues the value walk's depth, because a
    /// command can capture a variable holding another command, turn after turn.
    /// </summary>
    internal string ToSource(int depth)
    {
      switch (Kind)
      {
        case CommandKind.OutputData:
        {
          string value = Data.ToSource(depth);
          return value == null ? null : "(output-data:" + value + ")";
        }
        case CommandKind.RaiseError:
          return "(error:" + Data.ToSource(depth) + ")";
        case CommandKind.Link:
          return "(link-goto:" + Twee.MarkupPrinter.StringLiteral(Link.Text) + ","
            + Twee.MarkupPrinter.StringLiteral(Link.Target) + ")";
        case CommandKind.Builtin:
        {
          var call = new StringBuilder("(" + BuiltinName + ":");
          for (int i = 0; i < Arguments.Count; i++)
          {
            string argument = Arguments[i].ToSource(depth);
            if (argument == null) return null;
            if (i > 0) call.Append(',');
            call.Append(argument);
          }
          return call.Append(')').ToString();
        }
      }

      if (Changer.Source == null) return null;
      var names = new List<string>(Temps.Keys);
      names.Sort(System.StringComparer.Ordinal);
      var sb = new StringBuilder("(" + Macros.CustomCommandMacro.MacroName + ":");
      sb.Append(Changer.Source).Append(',').Append(Hook.Source).Append(",(dm:");
      for (int i = 0; i < names.Count; i++)
      {
        string value = Temps[names[i]].ToSource(depth);
        if (value == null) return null;
        if (i > 0) sb.Append(',');
        sb.Append(Twee.MarkupPrinter.StringLiteral(names[i])).Append(',').Append(value);
      }
      return sb.Append("))").ToString();
    }
  }
}
