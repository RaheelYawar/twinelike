namespace Harlowe.Runtime
{
  /// <summary>
  /// A typed variable name, <c>num-type _a</c>: reference's
  /// <c>ts/datatypes/typedvar.ts</c>. It restricts a variable when given to
  /// <c>(set:)</c>, <c>(put:)</c>, <c>(move:)</c> or <c>(unpack:)</c>, and names
  /// a parameter when given to <c>(macro:)</c>. It can't be stored in a variable.
  /// </summary>
  public sealed class TypedVarValue
  {
    /// <summary>The type: a datatype, or any data used as a pattern, like <c>(a: num, str)</c>.</summary>
    public readonly HarloweValue Datatype;

    /// <summary>The variable's name, without its sigil.</summary>
    public readonly string Name;

    /// <summary>True for a temp variable (<c>_a</c>), false for a story variable (<c>$a</c>).</summary>
    public readonly bool IsTemporary;

    /// <summary>True for a spread variable, <c>...num-type _a</c>, which takes a run of values as an array.</summary>
    public readonly bool IsRest;

    internal TypedVarValue(HarloweValue datatype, string name, bool isTemporary, bool isRest)
    {
      Datatype = datatype;
      Name = name;
      IsTemporary = isTemporary;
      IsRest = isRest;
    }

    /// <summary>The variable as written, like <c>_a</c>.</summary>
    internal string VariableSource => (IsTemporary ? "_" : "$") + Name;

    /// <summary>The value's <c>datatype</c> data name. A spread variable reports the spread form, as in reference.</summary>
    internal HarloweValue DatatypeProperty
      => IsRest && Datatype.Kind == HarloweValueKind.Datatype
        ? HarloweValue.OfDatatype(Datatype.AsDatatype.AsSpread())
        : Datatype;

    /// <summary>Source form, like <c>...num-type _a</c>, or null when the type has none.</summary>
    public string ToSource() => ToSource(0);

    internal string ToSource(int depth)
    {
      string type = Datatype.ToSource(depth);
      return type == null ? null : (IsRest ? "..." : string.Empty) + type + "-type " + VariableSource;
    }

    /// <summary>Reference's <c>objectName</c>: the type is named only when it's short.</summary>
    internal string ObjectName
    {
      get
      {
        string type = (IsRest ? "..." : string.Empty) + (Datatype.ToSource() ?? string.Empty);
        return "the " + (type.Length > 0 && type.Length < 24 ? type + "-" : string.Empty) + "typed variable name, " + VariableSource;
      }
    }
  }
}
