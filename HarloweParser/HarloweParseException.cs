using System;
using System.Text;

namespace Harlowe
{
  /// <summary>
  /// Thrown when a Harlowe HTML export or its embedded passage markup cannot be
  /// parsed. Carries structured location information (<see cref="Line"/>,
  /// <see cref="Column"/>, <see cref="PassageName"/>) so engine integrations can
  /// surface the error to authors without regex-parsing the message.
  ///
  /// <para>
  /// Top-level errors that fire before any passage is entered (e.g. a missing
  /// <c>&lt;tw-storydata&gt;</c> element) leave <see cref="Line"/>/<see cref="Column"/>
  /// at <c>-1</c> and <see cref="PassageName"/> at <c>null</c>. Per-passage
  /// errors carry a populated <see cref="PassageName"/>, plus
  /// <see cref="Line"/>/<see cref="Column"/> when the underlying tokenizer or
  /// expression parser knew the source position.
  /// </para>
  /// </summary>
  public class HarloweParseException : Exception
  {
    /// <summary>1-based source line where the error was detected, or <c>-1</c> when not applicable.</summary>
    public int Line { get; }

    /// <summary>1-based source column where the error was detected, or <c>-1</c> when not applicable.</summary>
    public int Column { get; }

    /// <summary>Name of the passage being parsed when the error occurred, or <c>null</c> for top-level errors.</summary>
    public string PassageName { get; }

    /// <summary>The base error description, without the location suffix that <see cref="Exception.Message"/> appends. Useful when re-throwing with additional context (e.g. attaching a passage name).</summary>
    public string RawMessage { get; }

    public HarloweParseException(string message)
      : this(message, -1, -1, null, null) { }

    public HarloweParseException(string message, int line, int column)
      : this(message, line, column, null, null) { }

    public HarloweParseException(string message, int line, int column, string passageName)
      : this(message, line, column, passageName, null) { }

    public HarloweParseException(string message, int line, int column, string passageName, Exception inner)
      : base(BuildMessage(message, line, column, passageName), inner)
    {
      RawMessage = message;
      Line = line;
      Column = column;
      PassageName = passageName;
    }

    private static string BuildMessage(string message, int line, int column, string passageName)
    {
      bool hasPos = line >= 0;
      bool hasName = !string.IsNullOrEmpty(passageName);
      if (!hasPos && !hasName) return message;

      var sb = new StringBuilder(message);
      sb.Append(" [");
      if (hasName) sb.Append("passage '").Append(passageName).Append("'");
      if (hasPos)
      {
        if (hasName) sb.Append(", ");
        sb.Append("line ").Append(line);
        if (column >= 0) sb.Append(", col ").Append(column);
      }
      sb.Append("]");
      return sb.ToString();
    }
  }
}
