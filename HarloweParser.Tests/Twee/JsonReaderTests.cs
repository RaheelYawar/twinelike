using System.Collections.Generic;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Twee
{
  public class JsonReaderTests
  {
    private static object Read(string s) => new JsonReader().Read(s);

    [Fact]
    public void ReadsString()
      => Assert.Equal("hello", Read("\"hello\""));

    [Fact]
    public void ReadsEmptyString()
      => Assert.Equal("", Read("\"\""));

    [Fact]
    public void ReadsStringEscapes()
    {
      Assert.Equal("a\"b", Read("\"a\\\"b\""));
      Assert.Equal("a\\b", Read("\"a\\\\b\""));
      Assert.Equal("a/b", Read("\"a\\/b\""));
      Assert.Equal("a\nb", Read("\"a\\nb\""));
      Assert.Equal("a\tb", Read("\"a\\tb\""));
      Assert.Equal("a\rb", Read("\"a\\rb\""));
      Assert.Equal("a\bb", Read("\"a\\bb\""));
      Assert.Equal("a\fb", Read("\"a\\fb\""));
    }

    [Fact]
    public void ReadsUnicodeEscape()
      => Assert.Equal("é", Read("\"\\u00e9\""));

    [Fact]
    public void ReadsInteger()
      => Assert.Equal(42.0, (double)Read("42"));

    [Fact]
    public void ReadsNegativeInteger()
      => Assert.Equal(-42.0, (double)Read("-42"));

    [Fact]
    public void ReadsDecimal()
      => Assert.Equal(3.14, (double)Read("3.14"));

    [Fact]
    public void ReadsScientific()
      => Assert.Equal(1500.0, (double)Read("1.5e3"));

    [Fact]
    public void ReadsScientificWithSign()
      => Assert.Equal(0.015, (double)Read("1.5e-2"), 10);

    [Fact]
    public void ReadsTrue() => Assert.Equal(true, Read("true"));

    [Fact]
    public void ReadsFalse() => Assert.Equal(false, Read("false"));

    [Fact]
    public void ReadsNull() => Assert.Null(Read("null"));

    [Fact]
    public void ReadsEmptyObject()
    {
      var v = Read("{}");
      var dict = Assert.IsType<Dictionary<string, object>>(v);
      Assert.Empty(dict);
    }

    [Fact]
    public void ReadsFlatObject()
    {
      var dict = (Dictionary<string, object>)Read("{\"a\":1,\"b\":\"x\"}");
      Assert.Equal(2, dict.Count);
      Assert.Equal(1.0, dict["a"]);
      Assert.Equal("x", dict["b"]);
    }

    [Fact]
    public void ReadsObjectWithWhitespace()
    {
      var dict = (Dictionary<string, object>)Read("{ \"a\" : 1 , \"b\" : 2 }");
      Assert.Equal(1.0, dict["a"]);
      Assert.Equal(2.0, dict["b"]);
    }

    [Fact]
    public void ReadsNestedObject()
    {
      var dict = (Dictionary<string, object>)Read("{\"outer\":{\"inner\":1}}");
      var inner = Assert.IsType<Dictionary<string, object>>(dict["outer"]);
      Assert.Equal(1.0, inner["inner"]);
    }

    [Fact]
    public void ReadsEmptyArray()
    {
      var list = (List<object>)Read("[]");
      Assert.Empty(list);
    }

    [Fact]
    public void ReadsArray()
    {
      var list = (List<object>)Read("[1, 2, 3]");
      Assert.Equal(3, list.Count);
      Assert.Equal(1.0, list[0]);
      Assert.Equal(2.0, list[1]);
      Assert.Equal(3.0, list[2]);
    }

    [Fact]
    public void ReadsMixedArray()
    {
      var list = (List<object>)Read("[\"a\", 1, true, null]");
      Assert.Equal("a", list[0]);
      Assert.Equal(1.0, list[1]);
      Assert.Equal(true, list[2]);
      Assert.Null(list[3]);
    }

    [Fact]
    public void ReadsStoryDataShape()
    {
      // Realistic Twee 3 StoryData payload.
      string src = "{\"ifid\":\"D674C58C-DEFA-4F70-B7A2-27742230C0FC\",\"format\":\"Harlowe\",\"format-version\":\"3.3.9\",\"start\":\"Disclaimer\",\"tag-colors\":{\"foo\":\"red\"},\"zoom\":1}";
      var dict = (Dictionary<string, object>)Read(src);
      Assert.Equal("D674C58C-DEFA-4F70-B7A2-27742230C0FC", dict["ifid"]);
      Assert.Equal("Harlowe", dict["format"]);
      Assert.Equal("3.3.9", dict["format-version"]);
      Assert.Equal("Disclaimer", dict["start"]);
      var tagColors = Assert.IsType<Dictionary<string, object>>(dict["tag-colors"]);
      Assert.Equal("red", tagColors["foo"]);
      Assert.Equal(1.0, dict["zoom"]);
    }

    [Fact]
    public void EmptyInput_Throws()
      => Assert.Throws<HarloweParseException>(() => Read(""));

    [Fact]
    public void TrailingContent_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("1 2"));

    [Fact]
    public void UnterminatedString_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("\"abc"));

    [Fact]
    public void UnterminatedObject_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("{\"a\":1"));

    [Fact]
    public void UnterminatedArray_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("[1,2"));

    [Fact]
    public void TrailingCommaInObject_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("{\"a\":1,}"));

    [Fact]
    public void UnknownKeyword_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("nope"));

    [Fact]
    public void InvalidEscape_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("\"\\x\""));

    [Fact]
    public void IncompleteUnicodeEscape_Throws()
      => Assert.Throws<HarloweParseException>(() => Read("\"\\u00\""));

    [Fact]
    public void Error_TracksLineAndColumn()
    {
      var ex = Assert.Throws<HarloweParseException>(() => Read("{\n  \"a\":\n  bad\n}"));
      // The bad token starts on line 3.
      Assert.Equal(3, ex.Line);
    }
  }
}
