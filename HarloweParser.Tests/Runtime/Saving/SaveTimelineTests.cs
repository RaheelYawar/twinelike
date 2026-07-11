using System.Collections.Generic;
using System.Text;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Saving
{
  /// <summary>
  /// Tests for the timeline ↔ blob serialiser: faithful round-trip of every Moment
  /// field, per-Moment compression, and atomic deserialise with missing-passage /
  /// malformed / version validation.
  /// </summary>
  public class SaveTimelineTests
  {
    private static Harlowe Story(params string[] names)
    {
      var sb = new StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"1\" creator=\"\" creator-version=\"\">");
      for (int i = 0; i < names.Length; i++)
        sb.Append($"<tw-passagedata pid=\"{i + 1}\" name=\"{names[i]}\" tags=\"\">body{i}</tw-passagedata>");
      sb.Append("</tw-storydata></body></html>");
      return new Harlowe(sb.ToString());
    }

    private static (MacroRegistry reg, MacroContext ctx) Env()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      return (reg, new MacroContext { Store = new HarloweVariableStore() });
    }

    [Fact]
    public void Timeline_RoundTrips_AllMomentFields()
    {
      var past = new List<Moment>
      {
        new Moment { PassageName = "P1", Seed = "abc", SeedIter = 0 },
        new Moment
        {
          PassageName = "P2",
          StoreDelta = new Dictionary<string, HarloweValue>
          {
            { "score", HarloweValue.OfNumber(42) },
            { "name", HarloweValue.OfString("Bob") },
          },
          SeedIter = 3,
        },
      };
      var present = new Moment { PassageName = "P3", Visits = new List<string> { "P2", "P3" } };

      var (reg, ctx) = Env();
      string blob = SaveSerializer.SerialiseTimeline(past, present);
      Assert.NotNull(blob);

      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1", "P2", "P3"), reg, ctx);
      Assert.True(r.Ok, r.Error);
      Assert.Equal(2, r.Past.Count);
      Assert.Equal("P1", r.Past[0].PassageName);
      Assert.Equal("abc", r.Past[0].Seed);
      Assert.Equal(0, r.Past[0].SeedIter);
      Assert.Equal(42.0, r.Past[1].StoreDelta["score"].AsNumber);
      Assert.Equal("Bob", r.Past[1].StoreDelta["name"].AsString);
      Assert.Equal(3, r.Past[1].SeedIter);
      Assert.Equal("P3", r.Present.PassageName);
      Assert.Equal(2, r.Present.Visits.Count);
      Assert.Equal("P2", r.Present.Visits[0]);
      Assert.Equal("P3", r.Present.Visits[1]);
    }

    [Fact]
    public void EmptyMoment_CompressesToBareString()
    {
      var blob = SaveSerializer.SerialiseTimeline(
        new List<Moment> { new Moment { PassageName = "P1" } },
        new Moment { PassageName = "P2" });

      Assert.Contains("\"P1\"", blob);
      Assert.DoesNotContain("passage", blob); // no full-object form for empty turns

      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1", "P2"), reg, ctx);
      Assert.True(r.Ok, r.Error);
      Assert.Equal("P1", r.Past[0].PassageName);
      Assert.Equal("P2", r.Present.PassageName);
    }

    [Fact]
    public void ChangerVar_RoundTripsThroughBlob()
    {
      var (reg, ctx) = Env();
      var changer = SaveSerializer.Deserialise("(text-style:\"bold\")", reg, ctx);
      var present = new Moment
      {
        PassageName = "P1",
        StoreDelta = new Dictionary<string, HarloweValue> { { "c", changer } },
      };

      string blob = SaveSerializer.SerialiseTimeline(new List<Moment>(), present);
      Assert.NotNull(blob);

      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1"), reg, ctx);
      Assert.True(r.Ok, r.Error);
      Assert.Equal(HarloweValueKind.Changer, r.Present.StoreDelta["c"].Kind);
      Assert.Equal(changer, r.Present.StoreDelta["c"]);
    }

    [Fact]
    public void NonFiniteVar_FailsSerialise()
    {
      var present = new Moment
      {
        PassageName = "P1",
        StoreDelta = new Dictionary<string, HarloweValue> { { "x", HarloweValue.OfNumber(double.PositiveInfinity) } },
      };
      Assert.Null(SaveSerializer.SerialiseTimeline(new List<Moment>(), present));
    }

    [Fact]
    public void MissingPassage_FailsAtomically()
    {
      var blob = SaveSerializer.SerialiseTimeline(new List<Moment>(), new Moment { PassageName = "GoneAway" });
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1", "P2"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Null(r.Present);
      Assert.Null(r.Past);
      Assert.Contains("GoneAway", r.Error);
    }

    [Fact]
    public void MissingPassageMidTimeline_FailsWholeLoad()
    {
      var past = new List<Moment> { new Moment { PassageName = "P1" }, new Moment { PassageName = "Deleted" } };
      var blob = SaveSerializer.SerialiseTimeline(past, new Moment { PassageName = "P2" });
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1", "P2"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Null(r.Past);
      Assert.Null(r.Present);
    }

    [Fact]
    public void RedirectTrailWithMissingPassage_FailsLoad()
    {
      var present = new Moment { PassageName = "P2", Visits = new List<string> { "P1", "Vanished" } };
      var blob = SaveSerializer.SerialiseTimeline(new List<Moment>(), present);
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1", "P2"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("Vanished", r.Error);
    }

    [Fact]
    public void MalformedBlob_ReturnsError()
    {
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline("{not valid json", Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.NotNull(r.Error);
    }

    [Fact]
    public void NewerVersion_Refused()
    {
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline("{ \"version\": 999, \"moments\": [\"P1\"] }", Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("newer version", r.Error);
    }

    [Fact]
    public void HugeVersion_RefusedNotBypassedByOverflow()
    {
      // (int)1e20 wraps to a value <= Current and would slip past the gate; the
      // refusal must compare the double directly.
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline("{ \"version\": 1e20, \"moments\": [\"P1\"] }", Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("newer version", r.Error);
    }

    [Theory]
    [InlineData("1e20")]
    [InlineData("-5")]
    public void OutOfRangeSeedIter_FailsLoad(string seedIter)
    {
      var (reg, ctx) = Env();
      var blob = "{ \"version\": 1, \"moments\": [ { \"passage\": \"P1\", \"seedIter\": " + seedIter + " } ] }";
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("out of range", r.Error);
    }

    [Fact]
    public void CorruptVarSource_FailsLoad()
    {
      // A tampered blob whose variable source doesn't evaluate cleanly fails the load.
      var (reg, ctx) = Env();
      var blob = "{ \"version\": 1, \"moments\": [ { \"passage\": \"P1\", \"vars\": { \"x\": \"(nonexistent-macro:)\" } } ] }";
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("x", r.Error);
    }

    [Fact]
    public void MissingVersion_FailsLoad()
    {
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline("{ \"moments\": [ \"P1\" ] }", Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("version", r.Error);
    }

    [Fact]
    public void MissingMoments_FailsLoad()
    {
      var (reg, ctx) = Env();
      var r = SaveSerializer.DeserialiseTimeline("{ \"version\": 1 }", Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("moments", r.Error);
    }

    [Fact]
    public void MomentWithoutPassageName_FailsLoad()
    {
      var (reg, ctx) = Env();
      var blob = "{ \"version\": 1, \"moments\": [ { \"seedIter\": 0 } ] }";
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("passage name", r.Error);
    }

    [Fact]
    public void ObjectMomentWithVanishedPassage_FailsLoad()
    {
      // The full-object moment form has its own passage-existence check,
      // separate from the compressed bare-string form's.
      var (reg, ctx) = Env();
      var blob = "{ \"version\": 1, \"moments\": [ { \"passage\": \"Ghost\" } ] }";
      var r = SaveSerializer.DeserialiseTimeline(blob, Story("P1"), reg, ctx);
      Assert.False(r.Ok);
      Assert.Contains("no longer exists", r.Error);
    }
  }
}
