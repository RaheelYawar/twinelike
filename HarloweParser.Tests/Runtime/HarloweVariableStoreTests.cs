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
    public void Snapshot_DeeplyNestedValue_DoesNotStackOverflow()
    {
      // A pathologically deep value (the runtime shape of `(set: $a to (a: $a))`
      // re-run thousands of times) must not overflow the stack when Snapshot
      // deep-copies it. The copy depth is bounded; past the ceiling the
      // remaining subtree is aliased rather than crashing the host.
      var store = new HarloweVariableStore();
      var v = HarloweValue.OfNumber(1);
      for (int i = 0; i < 5000; i++)
        v = HarloweValue.OfArray(new List<HarloweValue> { v });
      store.Set("a", isTemporary: false, value: v);

      var snap = store.Snapshot();
      Assert.NotNull(snap);
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

    // --- PushTempScope (hook-scoped temp variables) ---

    [Fact]
    public void PushTempScope_FreshInnerBinding_DiesOnPop()
    {
      // Matches reference Harlowe ts/internaltypes/varscope.ts: a temp
      // variable first declared inside a hook is scope-local — it's gone
      // after the hook exits.
      var store = new HarloweVariableStore();
      using (store.PushTempScope())
      {
        store.Set("y", isTemporary: true, value: HarloweValue.OfNumber(2));
        Assert.Equal(2, store.Get("y", isTemporary: true).AsNumber);
      }
      Assert.Null(store.Get("y", isTemporary: true));
    }

    [Fact]
    public void PushTempScope_InnerSetOfOuterVariable_WritesToOuter()
    {
      // Reference's parent-walking set() loop in ts/internaltypes/varref.ts:
      // "inner hooks can modify outer hooks' values" — a (set:) of a name
      // that exists in any outer scope writes to the OUTERMOST declaration.
      // After the inner scope pops, the change persists.
      var store = new HarloweVariableStore();
      store.Set("x", isTemporary: true, value: HarloweValue.OfNumber(1));
      using (store.PushTempScope())
      {
        store.Set("x", isTemporary: true, value: HarloweValue.OfNumber(2));
        Assert.Equal(2, store.Get("x", isTemporary: true).AsNumber);
      }
      Assert.Equal(2, store.Get("x", isTemporary: true).AsNumber);
    }

    [Fact]
    public void PushTempScope_OuterReadFindsOuterValue_WhenInnerShadowDoesNotExist()
    {
      var store = new HarloweVariableStore();
      store.Set("x", isTemporary: true, value: HarloweValue.OfNumber(1));
      using (store.PushTempScope())
      {
        // Inner scope didn't declare _x locally; Get walks outward.
        Assert.Equal(1, store.Get("x", isTemporary: true).AsNumber);
      }
    }

    [Fact]
    public void PushTempScope_StoryVarsUnaffected()
    {
      var store = new HarloweVariableStore();
      store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(10));
      using (store.PushTempScope())
      {
        store.Set("hp", isTemporary: false, value: HarloweValue.OfNumber(20));
      }
      // Story namespace ignores the scope stack — write persisted.
      Assert.Equal(20, store.Get("hp", isTemporary: false).AsNumber);
    }

    [Fact]
    public void PushTempScope_Nested_InnerSetWritesToOutermostMatch()
    {
      var store = new HarloweVariableStore();
      store.Set("x", isTemporary: true, value: HarloweValue.OfNumber(1));
      using (store.PushTempScope())
      using (store.PushTempScope())
      {
        store.Set("x", isTemporary: true, value: HarloweValue.OfNumber(99));
      }
      // The two-level-deep write should have landed in the root scope.
      Assert.Equal(99, store.Get("x", isTemporary: true).AsNumber);
    }

    [Fact]
    public void PushTempScope_Nested_LocalDeclarationDoesNotLeakUp()
    {
      var store = new HarloweVariableStore();
      using (store.PushTempScope())
      {
        store.Set("a", isTemporary: true, value: HarloweValue.OfNumber(1));
        using (store.PushTempScope())
        {
          // Fresh declaration at depth 2 — lives in the innermost scope.
          store.Set("b", isTemporary: true, value: HarloweValue.OfNumber(2));
          Assert.Equal(2, store.Get("b", isTemporary: true).AsNumber);
        }
        // Depth 2 scope popped. _b is gone.
        Assert.Null(store.Get("b", isTemporary: true));
        // _a (declared at depth 1) is still visible.
        Assert.Equal(1, store.Get("a", isTemporary: true).AsNumber);
      }
    }

    [Fact]
    public void PushTempScope_BeginPassage_ClearsAllScopes()
    {
      var store = new HarloweVariableStore();
      store.Set("x", isTemporary: true, value: HarloweValue.OfNumber(1));
      var inner = store.PushTempScope();
      store.Set("y", isTemporary: true, value: HarloweValue.OfNumber(2));

      store.BeginPassage();
      // Both temp vars cleared; the scope stack reset to a single empty root.
      Assert.Null(store.Get("x", isTemporary: true));
      Assert.Null(store.Get("y", isTemporary: true));

      // Disposing the dangling inner-scope token after BeginPassage must not
      // pop the new root (would leave the stack empty and break later Sets).
      inner.Dispose();
      store.Set("z", isTemporary: true, value: HarloweValue.OfNumber(3));
      Assert.Equal(3, store.Get("z", isTemporary: true).AsNumber);
    }

    [Fact]
    public void PushTempScope_Snapshot_CapturesEntireStack()
    {
      var store = new HarloweVariableStore();
      store.Set("a", isTemporary: true, value: HarloweValue.OfNumber(1));
      using (store.PushTempScope())
      {
        store.Set("b", isTemporary: true, value: HarloweValue.OfNumber(2));
        var snap = store.Snapshot();

        // Mutate after the snapshot.
        store.Set("a", isTemporary: true, value: HarloweValue.OfNumber(99));
        store.Set("b", isTemporary: true, value: HarloweValue.OfNumber(99));

        store.Restore(snap);
        Assert.Equal(1, store.Get("a", isTemporary: true).AsNumber);
        Assert.Equal(2, store.Get("b", isTemporary: true).AsNumber);
      }
    }
  }
}
