using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Harlowe.Twee
{
  /// <summary>
  /// Minimal JSON reader used by <see cref="TweeReader"/> to parse the JSON body
  /// of the special <c>:: StoryData</c> Twee passage. Twee's JSON usage is
  /// closed: a shallow object with string/number/bool values plus an optional
  /// <c>tag-colors</c> string-to-string sub-object, all machine-emitted by
  /// Twine 2. Hand-rolling avoids dragging Newtonsoft.Json (or
  /// System.Text.Json's netstandard2.0 transitive deps) into every Unity/Godot
  /// consumer's project.
  ///
  /// <para>Returned graph: <see cref="string"/>, <see cref="double"/>,
  /// <see cref="bool"/>, <c>null</c>,
  /// <see cref="Dictionary{TKey, TValue}"/> (string-keyed), and
  /// <see cref="List{T}"/> (of <see cref="object"/>). Numbers always come back
  /// as <see cref="double"/> — sufficient for the integer-shaped values
  /// (<c>zoom</c>, etc.) StoryData uses.</para>
  ///
  /// <para>Errors throw <see cref="HarloweParseException"/> with line/column
  /// tracking relative to the JSON snippet so the caller can attach passage
  /// context.</para>
  /// </summary>
  public class JsonReader
  {
    private string _src;
    private int _pos;
    private int _line;
    private int _col;

    /// <summary>
    /// Parse <paramref name="source"/> as a single JSON value and return the
    /// resulting object graph. Trailing whitespace is allowed; any
    /// non-whitespace content after the value is an error.
    /// </summary>
    public object Read(string source)
    {
      _src = source ?? string.Empty;
      _pos = 0;
      _line = 1;
      _col = 1;

      SkipWhitespace();
      if (_pos >= _src.Length) throw Error("empty JSON input");
      var value = ReadValue();
      SkipWhitespace();
      if (_pos < _src.Length) throw Error("unexpected trailing content");
      return value;
    }

    private object ReadValue()
    {
      SkipWhitespace();
      if (_pos >= _src.Length) throw Error("unexpected end of input");
      char c = _src[_pos];
      switch (c)
      {
        case '{': return ReadObject();
        case '[': return ReadArray();
        case '"': return ReadString();
        case 't': case 'f': return ReadBool();
        case 'n': return ReadNull();
        default:
          if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
          throw Error($"unexpected character '{c}'");
      }
    }

    private Dictionary<string, object> ReadObject()
    {
      Expect('{');
      var dict = new Dictionary<string, object>();
      SkipWhitespace();
      if (_pos < _src.Length && _src[_pos] == '}') { Advance(); return dict; }
      while (true)
      {
        SkipWhitespace();
        if (_pos >= _src.Length || _src[_pos] != '"') throw Error("object key must be a string");
        string key = ReadString();
        SkipWhitespace();
        Expect(':');
        var value = ReadValue();
        dict[key] = value;
        SkipWhitespace();
        if (_pos >= _src.Length) throw Error("unterminated object");
        if (_src[_pos] == ',') { Advance(); continue; }
        if (_src[_pos] == '}') { Advance(); return dict; }
        throw Error($"expected ',' or '}}' in object, got '{_src[_pos]}'");
      }
    }

    private List<object> ReadArray()
    {
      Expect('[');
      var list = new List<object>();
      SkipWhitespace();
      if (_pos < _src.Length && _src[_pos] == ']') { Advance(); return list; }
      while (true)
      {
        var value = ReadValue();
        list.Add(value);
        SkipWhitespace();
        if (_pos >= _src.Length) throw Error("unterminated array");
        if (_src[_pos] == ',') { Advance(); continue; }
        if (_src[_pos] == ']') { Advance(); return list; }
        throw Error($"expected ',' or ']' in array, got '{_src[_pos]}'");
      }
    }

    private string ReadString()
    {
      Expect('"');
      var sb = new StringBuilder();
      while (_pos < _src.Length)
      {
        char c = _src[_pos];
        if (c == '"') { Advance(); return sb.ToString(); }
        if (c == '\\')
        {
          Advance();
          if (_pos >= _src.Length) throw Error("unterminated escape sequence");
          char esc = _src[_pos];
          Advance();
          switch (esc)
          {
            case '"': sb.Append('"'); break;
            case '\\': sb.Append('\\'); break;
            case '/': sb.Append('/'); break;
            case 'b': sb.Append('\b'); break;
            case 'f': sb.Append('\f'); break;
            case 'n': sb.Append('\n'); break;
            case 'r': sb.Append('\r'); break;
            case 't': sb.Append('\t'); break;
            case 'u':
              if (_pos + 4 > _src.Length) throw Error("incomplete \\u escape");
              string hex = _src.Substring(_pos, 4);
              if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                throw Error($"invalid \\u escape '\\u{hex}'");
              sb.Append((char)code);
              _pos += 4;
              _col += 4;
              break;
            default:
              throw Error($"invalid escape sequence '\\{esc}'");
          }
          continue;
        }
        if (c < 0x20) throw Error($"unescaped control character U+{(int)c:X4}");
        sb.Append(c);
        Advance();
      }
      throw Error("unterminated string");
    }

    private double ReadNumber()
    {
      int start = _pos;
      if (_src[_pos] == '-') Advance();
      while (_pos < _src.Length && _src[_pos] >= '0' && _src[_pos] <= '9') Advance();
      if (_pos < _src.Length && _src[_pos] == '.')
      {
        Advance();
        while (_pos < _src.Length && _src[_pos] >= '0' && _src[_pos] <= '9') Advance();
      }
      if (_pos < _src.Length && (_src[_pos] == 'e' || _src[_pos] == 'E'))
      {
        Advance();
        if (_pos < _src.Length && (_src[_pos] == '+' || _src[_pos] == '-')) Advance();
        while (_pos < _src.Length && _src[_pos] >= '0' && _src[_pos] <= '9') Advance();
      }
      string text = _src.Substring(start, _pos - start);
      if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        throw Error($"invalid number '{text}'");
      return n;
    }

    private bool ReadBool()
    {
      if (Match("true")) return true;
      if (Match("false")) return false;
      throw Error("invalid literal");
    }

    private object ReadNull()
    {
      if (Match("null")) return null;
      throw Error("invalid literal");
    }

    private bool Match(string keyword)
    {
      if (_pos + keyword.Length > _src.Length) return false;
      for (int i = 0; i < keyword.Length; i++)
        if (_src[_pos + i] != keyword[i]) return false;
      for (int i = 0; i < keyword.Length; i++) Advance();
      return true;
    }

    private void Expect(char c)
    {
      if (_pos >= _src.Length || _src[_pos] != c)
        throw Error(_pos >= _src.Length ? $"expected '{c}', got end of input" : $"expected '{c}', got '{_src[_pos]}'");
      Advance();
    }

    private void SkipWhitespace()
    {
      while (_pos < _src.Length)
      {
        char c = _src[_pos];
        if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
        Advance();
      }
    }

    private void Advance()
    {
      char c = _src[_pos];
      _pos++;
      if (c == '\n') { _line++; _col = 1; }
      else { _col++; }
    }

    private HarloweParseException Error(string message)
      => new HarloweParseException("invalid JSON: " + message, _line, _col);
  }
}
