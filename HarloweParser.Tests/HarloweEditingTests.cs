using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Coverage for the editing API surface (public ctor, AddPassage,
  /// RemovePassage, RenamePassage, public metadata setters). Until the v1.3
  /// polish slice these were all internal — the tests in this file are
  /// verifying that an external consumer (in another assembly) can build
  /// and mutate stories.
  /// </summary>
  public class HarloweEditingTests
  {
    private static HarlowePassage MakePassage(string name, string body = "")
    {
      var ast = new PassageBody { Children = new List<IBodyNode>() };
      if (!string.IsNullOrEmpty(body))
        ast.Children.Add(new TextNode { Content = body });
      return new HarlowePassage
      {
        Name = name,
        Ast = ast,
        RawBody = body,
        Tags = new List<string>(),
        Branches = new List<Branch>(),
        IsDirty = true,
      };
    }

    [Fact]
    public void PublicConstructor_BuildsEmptyStory()
    {
      var story = new Harlowe();
      Assert.Equal(0, story.PassageCount);
      Assert.Equal(string.Empty, story.StoryName);
      Assert.Equal("0", story.StartNode);
    }

    [Fact]
    public void AddPassage_PublicAndIndexedByName()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First", "hello"));
      Assert.Equal(1, story.PassageCount);
      Assert.NotNull(story.GetPassage("First"));
    }

    [Fact]
    public void AddPassage_AutoSynthesizesPid_WhenNullOrEmpty()
    {
      var story = new Harlowe();
      var p = MakePassage("First");
      p.Pid = null;
      story.AddPassage(p);
      Assert.Equal("1", p.Pid);

      var q = MakePassage("Second");
      q.Pid = "";
      story.AddPassage(q);
      Assert.Equal("2", q.Pid);
    }

    [Fact]
    public void AddPassage_PreservesExplicitPid()
    {
      var story = new Harlowe();
      var p = MakePassage("First");
      p.Pid = "42";
      story.AddPassage(p);
      Assert.Equal("42", p.Pid);
    }

    [Fact]
    public void AddPassage_DuplicateName_Throws()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      Assert.Throws<System.ArgumentException>(() => story.AddPassage(MakePassage("First")));
    }

    [Fact]
    public void AddPassage_Null_Throws()
    {
      var story = new Harlowe();
      Assert.Throws<System.ArgumentNullException>(() => story.AddPassage(null));
    }

    [Fact]
    public void RemovePassage_ExistingPassage_ReturnsTrue()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      Assert.True(story.RemovePassage("First"));
      Assert.Equal(0, story.PassageCount);
      Assert.Null(story.GetPassage("First"));
    }

    [Fact]
    public void RemovePassage_NonExistent_ReturnsFalse()
    {
      var story = new Harlowe();
      Assert.False(story.RemovePassage("Nope"));
    }

    [Fact]
    public void RemovePassage_Null_ReturnsFalse()
    {
      var story = new Harlowe();
      Assert.False(story.RemovePassage(null));
    }

    [Fact]
    public void RemovePassage_StartPassage_LeavesGetStartPassageNull()
    {
      // Removing the start passage breaks GetStartPassage cleanly — it's the
      // same dangling-pid behaviour HTML stories already exhibit when the
      // export's startnode attribute names a missing passage.
      var story = new Harlowe();
      var start = MakePassage("First");
      start.Pid = "1";
      story.AddPassage(start);
      story.StartNode = "1";

      Assert.NotNull(story.GetStartPassage());
      Assert.True(story.RemovePassage("First"));
      Assert.Null(story.GetStartPassage());
    }

    [Fact]
    public void RenamePassage_RekeysLookup()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("Old"));
      Assert.True(story.RenamePassage("Old", "New"));
      Assert.Null(story.GetPassage("Old"));
      Assert.NotNull(story.GetPassage("New"));
      Assert.Equal("New", story.GetPassage("New").Name);
    }

    [Fact]
    public void RenamePassage_PreservesPassageInstance()
    {
      // Identity check: the renamed entry should be the same object, not a
      // copy. Catches an implementation that re-creates the passage instead
      // of re-keying.
      var story = new Harlowe();
      var p = MakePassage("Old");
      story.AddPassage(p);
      story.RenamePassage("Old", "New");
      Assert.Same(p, story.GetPassage("New"));
    }

    [Fact]
    public void RenamePassage_NonExistent_ReturnsFalse()
    {
      var story = new Harlowe();
      Assert.False(story.RenamePassage("Nope", "Anything"));
    }

    [Fact]
    public void RenamePassage_Collision_LeavesStoryUnchanged()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      story.AddPassage(MakePassage("Second"));
      Assert.False(story.RenamePassage("First", "Second"));
      // Both should still resolve to their original entries.
      Assert.Equal("First", story.GetPassage("First").Name);
      Assert.Equal("Second", story.GetPassage("Second").Name);
    }

    [Fact]
    public void RenamePassage_SameName_ReturnsTrueOrFalseBasedOnExistence()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      Assert.True(story.RenamePassage("First", "First"));
      Assert.False(story.RenamePassage("Nope", "Nope"));
    }

    [Fact]
    public void Passages_EnumeratesInLoadOrder()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      story.AddPassage(MakePassage("Second"));
      story.AddPassage(MakePassage("Third"));
      var names = new List<string>();
      foreach (var p in story.Passages) names.Add(p.Name);
      Assert.Equal(new[] { "First", "Second", "Third" }, names);
    }

    [Fact]
    public void StoryMetadata_PublicallyWritable_ReflectsInWriterOutput()
    {
      var story = new Harlowe();
      story.StoryName = "MyStory";
      story.Ifid = "ABC-123";
      story.Format = "Harlowe";
      story.FormatVersion = "3.3.9";
      var first = MakePassage("First", "body");
      story.AddPassage(first);
      story.StartNode = first.Pid;

      string output = new TweeWriter().Write(story);
      Assert.Contains(":: StoryTitle\nMyStory", output);
      Assert.Contains("\"ifid\": \"ABC-123\"", output);
      Assert.Contains("\"format\": \"Harlowe\"", output);
      Assert.Contains("\"start\": \"First\"", output);
    }

    [Fact]
    public void StoryDataExtras_PublicallyWritable_PreservedThroughWriter()
    {
      var story = new Harlowe();
      story.StoryDataExtras = new Dictionary<string, object>
      {
        { "tag-colors", new Dictionary<string, object> { { "important", "red" } } },
        { "zoom", 2.0 },
      };
      story.AddPassage(MakePassage("First", "body"));

      string output = new TweeWriter().Write(story);
      Assert.Contains("\"tag-colors\":", output);
      Assert.Contains("\"important\": \"red\"", output);
      Assert.Contains("\"zoom\": 2", output);
    }

    [Fact]
    public void RoundTrip_AddedPassageShowsUpInOutputAndReread()
    {
      var story = new TweeReader().Read(":: First\nA");
      story.AddPassage(MakePassage("Added", "B"));
      string output = new TweeWriter().Write(story);

      var roundTripped = new TweeReader().Read(output);
      Assert.Equal(2, roundTripped.PassageCount);
      Assert.NotNull(roundTripped.GetPassage("Added"));
      Assert.Equal("B", roundTripped.GetPassage("Added").RawBody);
    }

    [Fact]
    public void RoundTrip_RemovedPassageGoneFromOutput()
    {
      var story = new TweeReader().Read(":: First\nA\n\n:: Second\nB");
      Assert.True(story.RemovePassage("Second"));
      string output = new TweeWriter().Write(story);
      Assert.DoesNotContain(":: Second", output);
    }

    [Fact]
    public void RoundTrip_RenamedPassageReflectedInOutputHeader()
    {
      var story = new TweeReader().Read(":: Old\nbody");
      story.RenamePassage("Old", "New");
      string output = new TweeWriter().Write(story);
      Assert.Contains(":: New", output);
      Assert.DoesNotContain(":: Old", output);
    }
  }
}
