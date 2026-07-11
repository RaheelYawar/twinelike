using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Saving
{
  /// <summary>
  /// Round-trip tests for the source-based value serialiser: every
  /// <see cref="HarloweValue"/> kind must satisfy
  /// <c>Deserialise(ToSource(v)) ≈ v</c>, and un-sourceable values
  /// (non-finite numbers, errors, unstamped changers) must report no source.
  /// </summary>
  public class SaveSerializerTests
  {
    private static HarloweValue Deser(string source)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      return SaveSerializer.Deserialise(source, registry, new MacroContext { Store = new HarloweVariableStore() });
    }

    /// <summary>Deserialise the value's own source — the canonical round trip.</summary>
    private static HarloweValue RoundTrip(HarloweValue v) => Deser(v.ToSource());

    // ----- primitives -----

    [Theory]
    [InlineData(42.0)]
    [InlineData(-7.0)]
    [InlineData(3.14)]
    [InlineData(0.0)]
    [InlineData(1e-7)]
    [InlineData(1234567890.0)]
    public void Number_RoundTrips(double n)
    {
      var back = RoundTrip(HarloweValue.OfNumber(n));
      Assert.Equal(HarloweValueKind.Number, back.Kind);
      Assert.Equal(n, back.AsNumber);
    }

    [Fact]
    public void NonFiniteNumber_HasNoSource()
    {
      Assert.Null(HarloweValue.OfNumber(double.NaN).ToSource());
      Assert.Null(HarloweValue.OfNumber(double.PositiveInfinity).ToSource());
      Assert.Null(HarloweValue.OfNumber(double.NegativeInfinity).ToSource());
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("with \"quotes\" and a \\ backslash")]
    [InlineData("line1\nline2\ttab")]
    [InlineData("")]
    [InlineData("close paren ) and comma , inside")]
    public void String_RoundTrips(string s)
    {
      var back = RoundTrip(HarloweValue.OfString(s));
      Assert.Equal(HarloweValueKind.String, back.Kind);
      Assert.Equal(s, back.AsString);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bool_RoundTrips(bool b)
    {
      var back = RoundTrip(HarloweValue.OfBool(b));
      Assert.Equal(HarloweValueKind.Bool, back.Kind);
      Assert.Equal(b, back.AsBool);
    }

    // ----- collections -----

    [Fact]
    public void EmptyArray_RoundTrips()
    {
      var v = HarloweValue.OfArray(new System.Collections.Generic.List<HarloweValue>());
      Assert.Equal("(a:)", v.ToSource());
      Assert.Equal(v, RoundTrip(v));
    }

    [Fact]
    public void NestedArray_RoundTrips()
    {
      var inner = HarloweValue.OfArray(new System.Collections.Generic.List<HarloweValue>
        { HarloweValue.OfBool(true), HarloweValue.OfString("x") });
      var v = HarloweValue.OfArray(new System.Collections.Generic.List<HarloweValue>
        { HarloweValue.OfNumber(1), HarloweValue.OfString("two"), inner });
      Assert.Equal(v, RoundTrip(v));
    }

    [Fact]
    public void Array_WithNonFiniteElement_HasNoSource()
    {
      var v = HarloweValue.OfArray(new System.Collections.Generic.List<HarloweValue>
        { HarloweValue.OfNumber(1), HarloweValue.OfNumber(double.NaN) });
      Assert.Null(v.ToSource());
    }

    [Fact]
    public void Datamap_RoundTrips_KeysSortedAndStructurallyEqual()
    {
      var map = new System.Collections.Generic.Dictionary<string, HarloweValue>
      {
        { "banana", HarloweValue.OfNumber(2) },
        { "apple", HarloweValue.OfNumber(1) },
        { "cherry", HarloweValue.OfString("red") },
      };
      var v = HarloweValue.OfDatamap(map);
      Assert.Equal("(dm:\"apple\",1,\"banana\",2,\"cherry\",\"red\")", v.ToSource());
      Assert.Equal(v, RoundTrip(v));
    }

    [Fact]
    public void EmptyDatamap_RoundTrips()
    {
      var v = HarloweValue.OfDatamap(new System.Collections.Generic.Dictionary<string, HarloweValue>());
      Assert.Equal("(dm:)", v.ToSource());
      Assert.Equal(v, RoundTrip(v));
    }

    // ----- changers -----

    [Fact]
    public void StyleChanger_StampsSourceAndRoundTrips()
    {
      var chgr = Deser("(text-style:\"bold\")");
      Assert.Equal(HarloweValueKind.Changer, chgr.Kind);
      Assert.Equal("(text-style:\"bold\")", chgr.ToSource());
      Assert.Equal(chgr, RoundTrip(chgr));
    }

    [Fact]
    public void ComposedChanger_StampsCompositeSourceAndRoundTrips()
    {
      var chgr = Deser("(text-style:\"bold\")+(text-style:\"italic\")");
      Assert.Equal(HarloweValueKind.Changer, chgr.Kind);
      Assert.Equal("(text-style:\"bold\")+(text-style:\"italic\")", chgr.ToSource());
      Assert.Equal(chgr, RoundTrip(chgr));
    }

    [Theory]
    [InlineData("(for:each _x,(a:1,2,3))")]   // iteration family
    [InlineData("(replace:?cake)")]            // revision family
    [InlineData("(click:?cake)")]              // interaction family
    public void ChangerFamilies_StampSourceAndRoundTrip(string source)
    {
      // Every changer is born at a macro call evaluated by the expression
      // evaluator, so the Visit(MacroCallNode) stamp covers all families uniformly.
      var v = Deser(source);
      Assert.Equal(HarloweValueKind.Changer, v.Kind);
      Assert.Equal(source, v.ToSource());
    }

    [Fact]
    public void UnstampedChanger_HasNoSource()
    {
      // A changer built outside the evaluator (no macro-call stamp) can't be saved.
      var chgr = HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Bold = true }));
      Assert.Null(chgr.ToSource());
    }

    [Fact]
    public void ConditionalChanger_StampsSourceAndRoundTrips()
    {
      var v = Deser("(if:true)");
      Assert.Equal(HarloweValueKind.Changer, v.Kind);
      Assert.Equal("(if:true)", v.ToSource());
      Assert.Equal(v, RoundTrip(v));

      var composed = Deser("(if:false)+(text-style:\"bold\")");
      Assert.Equal("(if:false)+(text-style:\"bold\")", composed.ToSource());
      Assert.Equal(composed, RoundTrip(composed));
    }

    [Fact]
    public void ElseChanger_SerialisesAsBakedIf()
    {
      // (else:) bakes its decision at call time into an If-kind changer
      // pre-stamped with the equivalent (if:) source — a natural "(else:)"
      // stamp would error on load re-evaluation, where no conditional pairing
      // is in scope, and an Else-kind patch would make the reloaded value
      // structurally different from the stored one.
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry, LastConditional = false };
      registry.Context = ctx;
      var v = registry.Invoke("else", new System.Collections.Generic.List<HarloweValue>(), ctx);
      Assert.Equal(HarloweValueKind.Changer, v.Kind);
      Assert.Equal("(if:true)", v.ToSource());
      var reloaded = RoundTrip(v);
      Assert.Equal(v, reloaded);
      Assert.True(reloaded.AsChanger.Apply(new BufferedRenderOutput(), null));
    }

    [Fact]
    public void MacroReturningExistingChanger_KeepsCreationSource()
    {
      // (either:) *returns* the changer it was handed rather than creating one.
      // The stamp is creation-only: restamping the shared object would rewrite
      // the stored value's source to "(either:…)", which re-evaluates
      // (nondeterministically, with more args) on load.
      var picked = Deser("(either: (text-style:\"bold\"))");
      Assert.Equal(HarloweValueKind.Changer, picked.Kind);
      Assert.Equal("(text-style:\"bold\")", picked.ToSource());
    }

    // ----- lambda / hookname (reference-equality values: assert source identity) -----

    [Theory]
    [InlineData("_x where _x > 5")]
    [InlineData("where it > 5")]
    [InlineData("_x via _x * 2")]
    [InlineData("each _x")]
    public void Lambda_SourceRoundTrips(string source)
    {
      var v = Deser(source);
      Assert.Equal(HarloweValueKind.Lambda, v.Kind);
      Assert.Equal(source, v.ToSource());
      Assert.Equal(source, RoundTrip(v).ToSource());
    }

    [Theory]
    [InlineData("?cake")]
    [InlineData("?cake's 1st")]
    [InlineData("?cake's last")]
    public void HookName_SourceRoundTrips(string source)
    {
      var v = Deser(source);
      Assert.Equal(HarloweValueKind.HookName, v.Kind);
      Assert.Equal(source, v.ToSource());
      Assert.Equal(v, RoundTrip(v));
    }

    // ----- un-sourceable -----

    [Fact]
    public void Error_HasNoSource()
    {
      Assert.Null(HarloweValue.OfError("boom").ToSource());
    }

    [Fact]
    public void ToSource_DeeplyNestedValue_ReturnsNullNotStackOverflow()
    {
      // ToSource is public and recursive; past the depth cap a pathologically nested
      // value is un-saveable (null), not an uncatchable StackOverflow.
      var v = HarloweValue.OfNumber(1);
      for (int i = 0; i < 300; i++)
        v = HarloweValue.OfArray(new System.Collections.Generic.List<HarloweValue> { v });
      Assert.Null(v.ToSource());
    }

    [Fact]
    public void Deserialise_ThrowingEvaluation_ReturnsErrorNotThrow()
    {
      // ToSource never emits a var-ref, so "$x" is tampered/invalid source; with no
      // context (null store) the evaluator would NRE. The load boundary must degrade
      // to one in-prose error, honouring Deserialise's non-throwing contract.
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var result = SaveSerializer.Deserialise("$x", registry, null);
      Assert.True(result.IsError);
    }

    [Fact]
    public void Deserialise_MultipleExpressions_ReturnsError()
    {
      // Tampered source holding two comma-separated expressions parses cleanly
      // but isn't a single value — must degrade to an error, not evaluate the
      // first and drop the rest.
      var v = Deser("1,2");
      Assert.True(v.IsError);
      Assert.Contains("single expression", v.ErrorMessage);
    }
  }
}
