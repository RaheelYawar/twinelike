using System.Collections.Generic;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Saving
{
  /// <summary>Unit tests for the storage layer: <see cref="InMemorySaveStorage"/> and <see cref="SaveKeys"/>.</summary>
  public class SaveStorageTests
  {
    // ----- InMemorySaveStorage -----

    [Fact]
    public void WriteRead_RoundTrips()
    {
      var s = new InMemorySaveStorage();
      Assert.True(s.TryWrite("k1", "blob1", "File One"));
      Assert.True(s.TryRead("k1", out var blob));
      Assert.Equal("blob1", blob);
    }

    [Fact]
    public void ReadMissing_ReturnsFalseAndNull()
    {
      var s = new InMemorySaveStorage();
      Assert.False(s.TryRead("nope", out var blob));
      Assert.Null(blob);
    }

    [Fact]
    public void Write_OverwritesExisting()
    {
      var s = new InMemorySaveStorage();
      s.TryWrite("k", "old", "Old");
      s.TryWrite("k", "new", "New");
      s.TryRead("k", out var blob);
      Assert.Equal("new", blob);
    }

    [Fact]
    public void Delete_RemovesEntry()
    {
      var s = new InMemorySaveStorage();
      s.TryWrite("k", "blob", "f");
      Assert.True(s.TryDelete("k"));
      Assert.False(s.TryRead("k", out _));
      Assert.False(s.TryDelete("k")); // already gone
    }

    [Fact]
    public void Enumerate_ListsKeysAndFilenames()
    {
      var s = new InMemorySaveStorage();
      s.TryWrite("k1", "b1", "First");
      s.TryWrite("k2", "b2", "Second");
      var infos = new Dictionary<string, string>();
      foreach (var i in s.Enumerate()) infos[i.Key] = i.Filename;
      Assert.Equal(2, infos.Count);
      Assert.Equal("First", infos["k1"]);
      Assert.Equal("Second", infos["k2"]);
    }

    // ----- SaveKeys (IFID prefixing) -----

    [Theory]
    [InlineData("IFID-123", "slot1", "(Saved Game IFID-123) slot1")]
    [InlineData("", "slot1", "slot1")]
    [InlineData(null, "slot1", "slot1")]
    public void ToStorageKey_PrefixesWithIfid(string ifid, string slot, string expected)
    {
      Assert.Equal(expected, SaveKeys.ToStorageKey(ifid, slot));
    }

    [Fact]
    public void TryGetSlot_RoundTripsThePrefix()
    {
      var key = SaveKeys.ToStorageKey("ABC", "mySlot");
      Assert.True(SaveKeys.TryGetSlot("ABC", key, out var slot));
      Assert.Equal("mySlot", slot);
    }

    [Fact]
    public void TryGetSlot_RejectsForeignStoryKey()
    {
      var key = SaveKeys.ToStorageKey("STORY-A", "slot");
      Assert.False(SaveKeys.TryGetSlot("STORY-B", key, out _));
    }

    [Fact]
    public void TryGetSlot_EmptyIfid_TreatsEveryKeyAsOurs()
    {
      Assert.True(SaveKeys.TryGetSlot("", "anything", out var slot));
      Assert.Equal("anything", slot);
    }
  }
}
