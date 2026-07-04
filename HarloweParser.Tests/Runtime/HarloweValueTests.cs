using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  public class HarloweValueTests
  {
    [Fact]
    public void OfNumber_StoresKindAndPayload()
    {
      var v = HarloweValue.OfNumber(1.5);
      Assert.Equal(HarloweValueKind.Number, v.Kind);
      Assert.Equal(1.5, v.AsNumber);
    }

    [Fact]
    public void OfString_StoresPayload()
    {
      var v = HarloweValue.OfString("hi");
      Assert.Equal(HarloweValueKind.String, v.Kind);
      Assert.Equal("hi", v.AsString);
    }

    [Fact]
    public void OfString_CoalescesNullToEmpty()
    {
      var v = HarloweValue.OfString(null);
      Assert.Equal(string.Empty, v.AsString);
    }

    [Fact]
    public void OfBool_StoresPayload()
    {
      var v = HarloweValue.OfBool(true);
      Assert.Equal(HarloweValueKind.Bool, v.Kind);
      Assert.True(v.AsBool);
    }

    [Fact]
    public void OfArray_StoresPayload()
    {
      var items = new List<HarloweValue> { HarloweValue.OfNumber(1), HarloweValue.OfNumber(2) };
      var v = HarloweValue.OfArray(items);
      Assert.Equal(HarloweValueKind.Array, v.Kind);
      Assert.Same(items, v.AsArray);
    }

    [Fact]
    public void OfArray_CoalescesNullToEmptyList()
    {
      var v = HarloweValue.OfArray(null);
      Assert.NotNull(v.AsArray);
      Assert.Empty(v.AsArray);
    }

    [Fact]
    public void OfDatamap_StoresPayload()
    {
      var map = new Dictionary<string, HarloweValue> { ["a"] = HarloweValue.OfNumber(1) };
      var v = HarloweValue.OfDatamap(map);
      Assert.Equal(HarloweValueKind.Datamap, v.Kind);
      Assert.Same(map, v.AsDatamap);
    }

    [Fact]
    public void OfError_FlagsKindAndCarriesMessage()
    {
      var v = HarloweValue.OfError("boom");
      Assert.Equal(HarloweValueKind.Error, v.Kind);
      Assert.True(v.IsError);
      Assert.Equal("boom", v.ErrorMessage);
    }

    [Fact]
    public void IsTruthy_OnlyTrueForBoolTrue()
    {
      Assert.True(HarloweValue.OfBool(true).IsTruthy);
      Assert.False(HarloweValue.OfBool(false).IsTruthy);
      Assert.False(HarloweValue.OfNumber(1).IsTruthy);
      Assert.False(HarloweValue.OfNumber(0).IsTruthy);
      Assert.False(HarloweValue.OfString("non-empty").IsTruthy);
      Assert.False(HarloweValue.OfString("").IsTruthy);
      Assert.False(HarloweValue.OfArray(new List<HarloweValue>()).IsTruthy);
      Assert.False(HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>()).IsTruthy);
      Assert.False(HarloweValue.OfError("x").IsTruthy);
    }

    [Fact]
    public void Equals_NumbersByValue()
    {
      Assert.True(HarloweValue.OfNumber(1.5).Equals(HarloweValue.OfNumber(1.5)));
      Assert.False(HarloweValue.OfNumber(1.5).Equals(HarloweValue.OfNumber(1.6)));
    }

    [Fact]
    public void Equals_StringsByValue()
    {
      Assert.True(HarloweValue.OfString("hi").Equals(HarloweValue.OfString("hi")));
      Assert.False(HarloweValue.OfString("hi").Equals(HarloweValue.OfString("HI")));
    }

    [Fact]
    public void Equals_BoolsByValue()
    {
      Assert.True(HarloweValue.OfBool(true).Equals(HarloweValue.OfBool(true)));
      Assert.False(HarloweValue.OfBool(true).Equals(HarloweValue.OfBool(false)));
    }

    [Fact]
    public void Equals_DifferentKindsAlwaysFalse()
    {
      Assert.False(HarloweValue.OfNumber(1).Equals(HarloweValue.OfString("1")));
      Assert.False(HarloweValue.OfBool(true).Equals(HarloweValue.OfNumber(1)));
    }

    [Fact]
    public void Equals_ArraysStructurally()
    {
      var a = HarloweValue.OfArray(new List<HarloweValue>
      {
        HarloweValue.OfNumber(1),
        HarloweValue.OfString("x")
      });
      var b = HarloweValue.OfArray(new List<HarloweValue>
      {
        HarloweValue.OfNumber(1),
        HarloweValue.OfString("x")
      });
      var c = HarloweValue.OfArray(new List<HarloweValue>
      {
        HarloweValue.OfNumber(1),
        HarloweValue.OfString("y")
      });
      Assert.True(a.Equals(b));
      Assert.False(a.Equals(c));
    }

    [Fact]
    public void Equals_DatamapsStructurallyOrderInsensitive()
    {
      var a = HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["x"] = HarloweValue.OfNumber(1),
        ["y"] = HarloweValue.OfNumber(2)
      });
      var b = HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["y"] = HarloweValue.OfNumber(2),
        ["x"] = HarloweValue.OfNumber(1)
      });
      var c = HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["x"] = HarloweValue.OfNumber(1),
        ["y"] = HarloweValue.OfNumber(3)
      });
      Assert.True(a.Equals(b));
      Assert.False(a.Equals(c));
    }

    [Fact]
    public void Equals_ErrorsByMessage()
    {
      Assert.True(HarloweValue.OfError("oops").Equals(HarloweValue.OfError("oops")));
      Assert.False(HarloweValue.OfError("oops").Equals(HarloweValue.OfError("nope")));
    }

    [Fact]
    public void GetHashCode_EqualValuesProduceEqualHashes()
    {
      Assert.Equal(HarloweValue.OfNumber(1).GetHashCode(), HarloweValue.OfNumber(1).GetHashCode());
      Assert.Equal(HarloweValue.OfString("x").GetHashCode(), HarloweValue.OfString("x").GetHashCode());
      Assert.Equal(HarloweValue.OfBool(true).GetHashCode(), HarloweValue.OfBool(true).GetHashCode());
    }

    [Fact]
    public void ToHarloweString_NumberUsesInvariantCulture()
    {
      var prior = Thread.CurrentThread.CurrentCulture;
      try
      {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        Assert.Equal("1.5", HarloweValue.OfNumber(1.5).ToHarloweString());
      }
      finally
      {
        Thread.CurrentThread.CurrentCulture = prior;
      }
    }

    [Fact]
    public void ToHarloweString_BoolLowercase()
    {
      Assert.Equal("true", HarloweValue.OfBool(true).ToHarloweString());
      Assert.Equal("false", HarloweValue.OfBool(false).ToHarloweString());
    }

    [Fact]
    public void ToHarloweString_StringPassthrough()
    {
      Assert.Equal("hello", HarloweValue.OfString("hello").ToHarloweString());
    }

    [Fact]
    public void ToHarloweString_ArrayCommaJoined()
    {
      var arr = HarloweValue.OfArray(new List<HarloweValue>
      {
        HarloweValue.OfNumber(1),
        HarloweValue.OfString("x"),
        HarloweValue.OfBool(false)
      });
      Assert.Equal("1,x,false", arr.ToHarloweString());
    }

    [Fact]
    public void ToHarloweString_DatamapKeyValuePairs()
    {
      var map = HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["a"] = HarloweValue.OfNumber(1),
        ["b"] = HarloweValue.OfString("z")
      });
      var s = map.ToHarloweString();
      Assert.Contains("a:1", s);
      Assert.Contains("b:z", s);
    }

    [Fact]
    public void ToHarloweString_DatamapKeysSortedAlphabetically()
    {
      // Dictionary enumeration order is "insertion-order in practice but not
      // contractually guaranteed" — Mono/IL2CPP have been observed to diverge.
      // Insert in reverse-alphabetical order; expect alphabetical output so
      // the rendered string is stable across runtimes.
      var map = HarloweValue.OfDatamap(new Dictionary<string, HarloweValue>
      {
        ["zebra"] = HarloweValue.OfNumber(1),
        ["apple"] = HarloweValue.OfNumber(2),
        ["mango"] = HarloweValue.OfNumber(3)
      });
      Assert.Equal("apple:2,mango:3,zebra:1", map.ToHarloweString());
    }

    [Fact]
    public void ToHarloweString_ErrorReturnsMessage()
    {
      Assert.Equal("bad thing", HarloweValue.OfError("bad thing").ToHarloweString());
    }

    // Changer kind ----------------------------------------------------------

    [Fact]
    public void OfChanger_StoresKindAndPayload()
    {
      var c = Changer.FromStyle(new StyleSpec { Bold = true });
      var v = HarloweValue.OfChanger(c);
      Assert.Equal(HarloweValueKind.Changer, v.Kind);
      Assert.Same(c, v.AsChanger);
    }

    [Fact]
    public void OfChanger_NullThrows()
      => Assert.Throws<System.ArgumentNullException>(() => HarloweValue.OfChanger(null));

    [Fact]
    public void Changer_IsNotTruthy()
    {
      // Harlowe truthiness: only Bool(true) is truthy. Changers are values
      // but never act as conditions.
      var v = HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Bold = true }));
      Assert.False(v.IsTruthy);
    }

    [Fact]
    public void Changer_EqualsStructurally()
    {
      var x = HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Bold = true }));
      var y = HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Bold = true }));
      Assert.Equal(x, y);
      Assert.Equal(x.GetHashCode(), y.GetHashCode());
    }

    [Fact]
    public void Changer_DifferentWrappers_NotEqual()
    {
      Assert.NotEqual(
        HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Bold = true })),
        HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Italic = true })));
    }

    [Fact]
    public void ToHarloweString_ChangerPrintsReferenceForm()
    {
      // Reference prints changers as "[A (text-style:) changer]" (changer.ts's
      // print()); an unstamped changer has no creation source to name.
      var unstamped = HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Bold = true }));
      Assert.Equal("[A changer]", unstamped.ToHarloweString());

      var stamped = Changer.FromStyle(new StyleSpec { Bold = true });
      stamped.Source = "(text-style:\"bold\")";
      Assert.Equal("[A (text-style:) changer]", HarloweValue.OfChanger(stamped).ToHarloweString());
    }
  }
}
