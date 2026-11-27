using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>Which transition field a transition macro sets.</summary>
  public enum TransitionField
  {
    /// <summary><c>(transition:)</c>: how the hook appears.</summary>
    Transition,
    /// <summary><c>(transition-depart:)</c>: how the passage leaves when a link is followed.</summary>
    Depart,
    /// <summary><c>(transition-arrive:)</c>: how the next passage arrives.</summary>
    Arrive,
    /// <summary><c>(transition-time:)</c>.</summary>
    Time,
    /// <summary><c>(transition-delay:)</c>.</summary>
    Delay,
    /// <summary><c>(transition-skip:)</c>.</summary>
    Skip,
  }

  /// <summary>
  /// The <c>(transition:)</c> family, each also known by its <c>t8n</c> short
  /// name. They return changers whose transition fields reach the host as one
  /// innermost <see cref="StyleSpec"/> layer; making things move is the host's
  /// job. Reference's entries in <c>ts/macrolib/stylechangers.ts</c>: a name
  /// must be one of reference's <c>validT8ns</c>, compared ignoring case,
  /// <c>-</c> and <c>_</c>; a time must be positive, except that a delay may
  /// be zero.
  /// </summary>
  public class TransitionMacro : IMacro
  {
    /// <summary>Reference's <c>validT8ns</c>, in its order, which its error message lists.</summary>
    internal static readonly string[] Names =
    {
      "instant", "dissolve", "fade", "rumble", "shudder", "pulse", "zoom", "flicker", "slideleft", "slideright",
      "slideup", "slidedown", "fadeleft", "faderight", "fadeup", "fadedown", "blur",
    };

    private readonly string _name;
    private readonly TransitionField _field;

    public TransitionMacro(string name, TransitionField field)
    {
      _name = name;
      _field = field;
    }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      var fields = new StyleSpec();
      switch (_field)
      {
        case TransitionField.Transition:
        case TransitionField.Depart:
        case TransitionField.Arrive:
        {
          string name = arg.Kind == HarloweValueKind.String ? MacroNames.Normalize(arg.AsString) : null;
          if (name == null || System.Array.IndexOf(Names, name) < 0)
            return HarloweValue.OfError($"{Capitalised(arg.ObjectName)} is not a valid name string for the ({_name}:) macro. "
              + "Only the following names are recognised (capitalisation and hyphens ignored): " + string.Join(", ", Names) + ".");
          if (_field == TransitionField.Transition) fields.Transition = name;
          else if (_field == TransitionField.Depart) fields.TransitionDepart = name;
          else fields.TransitionArrive = name;
          break;
        }
        default:
        {
          bool zeroAllowed = _field == TransitionField.Delay;
          if (arg.Kind != HarloweValueKind.Number || double.IsNaN(arg.AsNumber)
              || (zeroAllowed ? arg.AsNumber < 0 : arg.AsNumber <= 0))
            return HarloweValue.OfError($"The ({_name}:) macro's 1st value is {arg.ObjectName}, but should be "
              + (zeroAllowed ? "a number between 0 and Infinity." : "a positive number."));
          double ms = arg.AsNumber;
          if (_field == TransitionField.Time) fields.TransitionTime = ms;
          else if (_field == TransitionField.Delay) fields.TransitionDelay = ms;
          else fields.TransitionSkip = ms;
          break;
        }
      }
      return HarloweValue.OfChanger(Changer.FromTransition(fields));
    }

    private static string Capitalised(string s)
      => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
  }
}
