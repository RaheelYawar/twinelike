using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>How a built-in macro treats a changer written just before it, as measured on 3.3.9.</summary>
  internal enum CommandRole
  {
    /// <summary>Not a command: it has a value, which can't take a changer.</summary>
    None,

    /// <summary>The changer applies to what the command prints.</summary>
    Takes,

    /// <summary>The changer applies to the hooks the command reveals.</summary>
    Reveals,

    /// <summary>Attaching is allowed, but the command runs as if it weren't there, even under <c>(if: false)</c>.</summary>
    Ignores,

    /// <summary>A command that can't have changers attached.</summary>
    Refuses,

    /// <summary>An assignment, which runs when it's evaluated and can't be stored or take a changer.</summary>
    Operation,
  }

  /// <summary>
  /// The built-in macros reference defines with <c>addCommand</c>. In an
  /// expression they make a <see cref="CommandValue"/> instead of running, so
  /// <c>(set: $c to (go-to: "x"))</c> stores the navigation for later.
  /// </summary>
  internal static class BuiltinCommands
  {
    private sealed class Entry
    {
      public string Name;
      public CommandRole Role;
    }

    private static readonly Dictionary<string, Entry> Table = BuildTable();

    private static Dictionary<string, Entry> BuildTable()
    {
      var table = new Dictionary<string, Entry>();
      Add(table, CommandRole.Takes, "print", "display", "link-undo");
      Add(table, CommandRole.Reveals, "show");
      Add(table, CommandRole.Ignores, "go-to", "click-goto", "click-undo",
        "mouseover-goto", "mouseover-undo", "mouseout-goto", "mouseout-undo");
      Add(table, CommandRole.Refuses, "load-game", "change", "enchant");
      Add(table, CommandRole.Operation, "set", "put", "move", "unpack");
      return table;
    }

    private static void Add(Dictionary<string, Entry> table, CommandRole role, params string[] names)
    {
      for (int i = 0; i < names.Length; i++)
        table[MacroNames.Normalize(names[i])] = new Entry { Name = names[i], Role = role };
    }

    internal static CommandRole RoleOf(string macroName)
      => Table.TryGetValue(MacroNames.Normalize(macroName), out var entry) ? entry.Role : CommandRole.None;

    /// <summary>The spelling reference's messages use, whichever one the author wrote.</summary>
    internal static string CanonicalName(string macroName)
      => Table.TryGetValue(MacroNames.Normalize(macroName), out var entry) ? entry.Name : macroName;
  }

  /// <summary>
  /// A macro that takes hook names. Reference treats a hook name as unstorable,
  /// so its <c>Any</c> signature refuses one everywhere else.
  /// </summary>
  internal interface ITakesHookNames
  {
  }

  /// <summary>
  /// A built-in command's own checks, which reference runs when the command is
  /// made. A command that's hidden by a changer or only stored still reports them.
  /// </summary>
  internal interface ICommandCheck
  {
    /// <summary>The error, or null when the call is valid. The argument count and argument errors have already been checked.</summary>
    HarloweValue Check(List<HarloweValue> args, MacroContext context);
  }
}
