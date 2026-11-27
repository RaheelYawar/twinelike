using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Tokens;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A code hook held as a value: reference's <c>ts/datatypes/codehook.ts</c>.
  /// Immutable. Two code hooks are equal when their source is, as in reference's
  /// <c>is()</c>.
  ///
  /// <para>A hook read back from a save has source but no parsed body, because
  /// save values are lexed under <see cref="HarloweProfile.SaveFormat"/> while a
  /// hook's contents are the author's markup and belong to the story's profile.
  /// It parses on first use, under the profile of the story running it.</para>
  /// </summary>
  public sealed class CodeHookValue
  {
    private string _source;
    private List<IBodyNode> _children;

    internal CodeHookValue(string source, List<IBodyNode> children)
    {
      _source = source;
      _children = children;
    }

    /// <summary>The hook as markup, brackets included.</summary>
    public string Source
    {
      get
      {
        if (_source == null)
          _source = "[" + new Twee.MarkupPrinter().Print(new PassageBody { Children = _children }) + "]";
        return _source;
      }
    }

    /// <summary>The markup between the brackets.</summary>
    public string InnerSource
    {
      get
      {
        string source = Source;
        return source.Length >= 2 ? source.Substring(1, source.Length - 2) : string.Empty;
      }
    }

    /// <summary>The hook's parsed contents, parsing them under <paramref name="profile"/> if this hook came from a save.</summary>
    internal List<IBodyNode> ChildrenFor(HarloweProfile profile)
    {
      if (_children != null) return _children;
      string inner = InnerSource;
      try
      {
        var tokens = new HarloweTokenizer(profile).Tokenize(inner);
        _children = new HarloweBodyParser().Parse(tokens, inner).Children;
      }
      catch (HarloweParseException ex)
      {
        _children = new List<IBodyNode>
        {
          new ParseErrorNode { Message = ex.Message, Detail = ex.RawMessage ?? ex.Message, OriginalSource = inner }
        };
      }
      return _children;
    }

    public bool EqualsHook(CodeHookValue other) => other != null && other.Source == Source;

    public override bool Equals(object obj) => obj is CodeHookValue other && EqualsHook(other);

    public override int GetHashCode() => Source.GetHashCode();
  }
}
