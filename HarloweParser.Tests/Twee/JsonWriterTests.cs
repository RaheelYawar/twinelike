using System.Collections.Generic;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Twee
{
  public class JsonWriterTests
  {
    private static string Write(object value) => new JsonWriter().Write(value);

    [Fact] public void WritesNull() => Assert.Equal("null", Write(null));
    [Fact] public void WritesTrue() => Assert.Equal("true", Write(true));
    [Fact] public void WritesFalse() => Assert.Equal("false", Write(false));

    [Fact] public void WritesIntegerWhole() => Assert.Equal("1", Write(1.0));
    [Fact] public void WritesNegativeInteger() => Assert.Equal("-42", Write(-42.0));
    [Fact] public void WritesDecimal() => Assert.Equal("3.14", Write(3.14));
    [Fact] public void WritesIntFromIntType() => Assert.Equal("7", Write(7));
    [Fact] public void WritesLong() => Assert.Equal("100", Write(100L));

    [Fact] public void WritesString() => Assert.Equal("\"hello\"", Write("hello"));
    [Fact] public void WritesEmptyString() => Assert.Equal("\"\"", Write(""));

    [Fact]
    public void EscapesQuoteAndBackslash()
    {
      Assert.Equal("\"a\\\"b\"", Write("a\"b"));
      Assert.Equal("\"a\\\\b\"", Write("a\\b"));
    }

    [Fact]
    public void EscapesControlCharacters()
    {
      Assert.Equal("\"a\\nb\"", Write("a\nb"));
      Assert.Equal("\"a\\tb\"", Write("a\tb"));
      Assert.Equal("\"a\\rb\"", Write("a\rb"));
      Assert.Equal("\"a\\bb\"", Write("a\bb"));
      Assert.Equal("\"a\\fb\"", Write("a\fb"));
    }

    [Fact]
    public void EscapesArbitraryControlCharsAsUnicode()
      => Assert.Equal("\"a\\u0001b\"", Write("ab"));

    [Fact]
    public void DoesNotEscapeForwardSlash()
      => Assert.Equal("\"a/b\"", Write("a/b"));

    [Fact]
    public void EmptyObject() => Assert.Equal("{}", Write(new Dictionary<string, object>()));

    [Fact]
    public void EmptyArray() => Assert.Equal("[]", Write(new List<object>()));

    [Fact]
    public void FlatObject_PrettyPrinted()
    {
      var dict = new Dictionary<string, object> { { "a", 1.0 }, { "b", "x" } };
      string expected = "{\n  \"a\": 1,\n  \"b\": \"x\"\n}";
      Assert.Equal(expected, Write(dict));
    }

    [Fact]
    public void NestedObject_IndentsCorrectly()
    {
      var dict = new Dictionary<string, object>
      {
        { "outer", new Dictionary<string, object> { { "inner", 1.0 } } }
      };
      string expected = "{\n  \"outer\": {\n    \"inner\": 1\n  }\n}";
      Assert.Equal(expected, Write(dict));
    }

    [Fact]
    public void Array_PrettyPrinted()
    {
      var list = new List<object> { 1.0, 2.0, "three" };
      string expected = "[\n  1,\n  2,\n  \"three\"\n]";
      Assert.Equal(expected, Write(list));
    }

    [Fact]
    public void StoryDataShape_RoundTripsThroughReader()
    {
      // Practical end-to-end: build a typical StoryData object, write it,
      // read it back, assert the contents are equal. Catches accidental
      // divergence between writer and reader.
      var dict = new Dictionary<string, object>
      {
        { "ifid", "D674C58C-DEFA-4F70-B7A2-27742230C0FC" },
        { "format", "Harlowe" },
        { "format-version", "3.3.9" },
        { "start", "Disclaimer" },
        { "tag-colors", new Dictionary<string, object> { { "important", "red" } } },
        { "zoom", 1.0 }
      };
      string json = Write(dict);
      var roundTripped = (Dictionary<string, object>)new JsonReader().Read(json);
      Assert.Equal("D674C58C-DEFA-4F70-B7A2-27742230C0FC", roundTripped["ifid"]);
      Assert.Equal("Harlowe", roundTripped["format"]);
      Assert.Equal("3.3.9", roundTripped["format-version"]);
      Assert.Equal("Disclaimer", roundTripped["start"]);
      var tagColors = (Dictionary<string, object>)roundTripped["tag-colors"];
      Assert.Equal("red", tagColors["important"]);
      Assert.Equal(1.0, roundTripped["zoom"]);
    }

    [Fact]
    public void UnsupportedType_Throws()
      => Assert.Throws<HarloweParseException>(() => Write(new System.DateTime(2026, 5, 9)));
  }
}
