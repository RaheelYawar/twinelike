using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Direct tests for <see cref="StyleSpec"/>: equality, hashing, and the
  /// <see cref="StyleSpec.IsEmpty"/> shortcut. The spec is a plain semantic
  /// container — no rendering behaviour lives on it.
  /// </summary>
  public class StyleSpecTests
  {
    [Fact]
    public void Default_IsEmpty()
    {
      Assert.True(new StyleSpec().IsEmpty);
    }

    [Fact]
    public void AnyFlagSet_NotEmpty()
    {
      Assert.False(new StyleSpec { Bold = true }.IsEmpty);
      Assert.False(new StyleSpec { Italic = true }.IsEmpty);
      Assert.False(new StyleSpec { Underline = true }.IsEmpty);
      Assert.False(new StyleSpec { Strikethrough = true }.IsEmpty);
    }

    [Fact]
    public void AnyValueSet_NotEmpty()
    {
      Assert.False(new StyleSpec { Color = "red" }.IsEmpty);
      Assert.False(new StyleSpec { BackgroundColor = "blue" }.IsEmpty);
      Assert.False(new StyleSpec { FontFamily = "serif" }.IsEmpty);
      Assert.False(new StyleSpec { FontSize = "120%" }.IsEmpty);
    }

    [Fact]
    public void Equals_SameFlags_IsEqual()
    {
      Assert.Equal(
        new StyleSpec { Bold = true, Italic = true },
        new StyleSpec { Bold = true, Italic = true });
    }

    [Fact]
    public void Equals_DifferentFlags_NotEqual()
    {
      Assert.NotEqual(
        new StyleSpec { Bold = true },
        new StyleSpec { Italic = true });
    }

    [Fact]
    public void Equals_SameValues_IsEqual()
    {
      Assert.Equal(
        new StyleSpec { Color = "red", FontSize = "120%" },
        new StyleSpec { Color = "red", FontSize = "120%" });
    }

    [Fact]
    public void Equals_DifferentValues_NotEqual()
    {
      Assert.NotEqual(
        new StyleSpec { Color = "red" },
        new StyleSpec { Color = "blue" });
    }

    [Fact]
    public void Equals_NullVsUnset_AreSame()
    {
      // Color = null is the same as unset.
      Assert.Equal(
        new StyleSpec(),
        new StyleSpec { Color = null });
    }

    [Fact]
    public void Equals_NotEqualToNonStyleSpec()
    {
      Assert.False(new StyleSpec().Equals("not a spec"));
      Assert.False(new StyleSpec().Equals(null));
    }

    [Fact]
    public void GetHashCode_StableForEqualSpecs()
    {
      var a = new StyleSpec { Bold = true, Color = "red" };
      var b = new StyleSpec { Bold = true, Color = "red" };
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
  }
}
