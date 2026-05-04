using System;
using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  public class HarloweVariableStoreTests
  {
    [Fact]
    public void Get_UnsetName_ReturnsNull()
    {
      var store = new HarloweVariableStore();
      Assert.Null(store.Get("hp", isTemporary: false));
      Assert.Null(store.Get("loop", isTemporary: true));
    }

    [Fact]
    public void Set_StoresInStoryNamespace()
    {
      var store = new HarloweVariableStore();
      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(10));

      Assert.Equal(10, store.Get("hp", isTemporary: false).AsNumber);
    }

    [Fact]
    public void Set_StoresInTempNamespace()
    {
      var store = new HarloweVariableStore();
      store.Set("loop", isTemporary: true, value: HarloweValue.OfNumber(3));

      Assert.Equal(3, store.Get("loop", isTemporary: true).AsNumber);
    }

    [Fact]
    public void StoryAndTempNamespaces_AreSeparate()
    {
      var store = new HarloweVariableStore();
      store.Set("x", isTemporary: false, value: HarloweValue.OfNumber(1));
      store.Set("x", isTemporary: true, value: HarloweValue.OfNumber(2));

      Assert.Equal(1, store.Get("x", isTemporary: false).AsNumber);
      Assert.Equal(2, store.Get("x", isTemporary: true).AsNumber);
    }

    [Fact]
    public void It_NullBeforeAnySet()
    {
      var store = new HarloweVariableStore();
      Assert.Null(store.It);
    }

    [Fact]
    public void It_TracksMostRecentSet()
    {
      var store = new HarloweVariableStore();
      store.Set("a", isTemporary: false, value: HarloweValue.OfNumber(1));
      Assert.Equal(1, store.It.AsNumber);

      store.Set("b", isTemporary: true, value: HarloweValue.OfString("hi"));
      Assert.Equal("hi", store.It.AsString);
    }

    [Fact]
    public void BeginPassage_ClearsTempNamespace()
    {
      var store = new HarloweVariableStore();
      store.Set("loop", isTemporary: true, value: HarloweValue.OfNumber(3));

      store.BeginPassage();

      Assert.Null(store.Get("loop", isTemporary: true));
    }

    [Fact]
    public void BeginPassage_DoesNotTouchStoryNamespace()
    {
      var store = new HarloweVariableStore();
      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(10));

      store.BeginPassage();

      Assert.Equal(10, store.Get("hp", isTemporary: false).AsNumber);
    }

    [Fact]
    public void BeginPassage_DoesNotResetIt()
    {
      var store = new HarloweVariableStore();
      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(10));

      store.BeginPassage();

      Assert.Equal(10, store.It.AsNumber);
    }

    [Fact]
    public void Snapshot_Restore_RoundTripsAllNamespaces()
    {
      var store = new HarloweVariableStore();
      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(10));
      store.Set("loop", isTemporary: true, value: HarloweValue.OfNumber(3));

      var snap = store.Snapshot();

      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(99));
      store.Set("loop", isTemporary: true, value: HarloweValue.OfNumber(7));

      store.Restore(snap);

      Assert.Equal(10, store.Get("hp", isTemporary: false).AsNumber);
      Assert.Equal(3, store.Get("loop", isTemporary: true).AsNumber);
    }

    [Fact]
    public void Snapshot_Restore_RoundTripsItSlot()
    {
      var store = new HarloweVariableStore();
      store.Set("a", isTemporary: false, value: HarloweValue.OfNumber(1));
      var snap = store.Snapshot();

      store.Set("b", isTemporary: false, value: HarloweValue.OfNumber(2));
      Assert.Equal(2, store.It.AsNumber);

      store.Restore(snap);
      Assert.Equal(1, store.It.AsNumber);
    }

    [Fact]
    public void Snapshot_DeepCopiesArrays_SoMutationDoesNotLeakIntoSnapshot()
    {
      var store = new HarloweVariableStore();
      var inner = new List<HarloweValue> { HarloweValue.OfNumber(1) };
      store.Set("xs", isTemporary: false, value: HarloweValue.OfArray(inner));

      var snap = store.Snapshot();

      // Mutate the live array in place.
      inner.Add(HarloweValue.OfNumber(2));

      store.Restore(snap);
      var restored = store.Get("xs", isTemporary: false).AsArray;
      Assert.Single(restored);
      Assert.Equal(1, restored[0].AsNumber);
    }

    [Fact]
    public void Snapshot_DeepCopiesDatamaps()
    {
      var store = new HarloweVariableStore();
      var map = new Dictionary<string, HarloweValue> { ["x"] = HarloweValue.OfNumber(1) };
      store.Set("m", isTemporary: false, value: HarloweValue.OfDatamap(map));

      var snap = store.Snapshot();

      map["x"] = HarloweValue.OfNumber(99);
      map["y"] = HarloweValue.OfNumber(7);

      store.Restore(snap);
      var restored = store.Get("m", isTemporary: false).AsDatamap;
      Assert.Single(restored);
      Assert.Equal(1, restored["x"].AsNumber);
    }

    [Fact]
    public void Snapshot_DeepCopiesNestedArraysInsideDatamaps()
    {
      var store = new HarloweVariableStore();
      var nested = new List<HarloweValue> { HarloweValue.OfNumber(1) };
      var map = new Dictionary<string, HarloweValue> { ["xs"] = HarloweValue.OfArray(nested) };
      store.Set("m", isTemporary: false, value: HarloweValue.OfDatamap(map));

      var snap = store.Snapshot();
      nested.Add(HarloweValue.OfNumber(2));

      store.Restore(snap);
      var restored = store.Get("m", isTemporary: false).AsDatamap["xs"].AsArray;
      Assert.Single(restored);
    }

    [Fact]
    public void Restore_RejectsForeignSnapshotObject()
    {
      var store = new HarloweVariableStore();
      Assert.Throws<ArgumentException>(() => store.Restore(new object()));
    }

    [Fact]
    public void RestoreThenContinue_PreservesIndependenceOfLiveAndSnapshotState()
    {
      var store = new HarloweVariableStore();
      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(10));
      var snap = store.Snapshot();

      store.Restore(snap);
      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(99));

      // Restore from the same snap a second time — should still see 10, not 99.
      store.Restore(snap);
      Assert.Equal(10, store.Get("hp", isTemporary: false).AsNumber);
    }
  }
}
