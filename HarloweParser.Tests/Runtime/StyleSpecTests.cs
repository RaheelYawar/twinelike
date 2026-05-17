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
      Assert.False(new StyleSpec { BackgroundImage = "sky.png" }.IsEmpty);
      Assert.False(new StyleSpec { FontFamily = "serif" }.IsEmpty);
      Assert.False(new StyleSpec { FontSize = "120%" }.IsEmpty);
      Assert.False(new StyleSpec { Opacity = 0.5 }.IsEmpty);
      Assert.False(new StyleSpec { Alignment = TextAlignment.Center }.IsEmpty);
    }

    [Fact]
    public void EffectsSet_NotEmpty()
    {
      var s = new StyleSpec();
      s.Effects.Add(TextEffect.Blur);
      Assert.False(s.IsEmpty);
    }

    [Fact]
    public void Equals_SameEffects_IsEqual()
    {
      var a = new StyleSpec();
      a.Effects.Add(TextEffect.Mark);
      a.Effects.Add(TextEffect.Shudder);
      var b = new StyleSpec();
      b.Effects.Add(TextEffect.Mark);
      b.Effects.Add(TextEffect.Shudder);
      Assert.Equal(a, b);
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentEffectOrder_NotEqual()
    {
      // Order is preserved and matters — two specs with the same effects in
      // different order are NOT equal. Composition order would alter render.
      var a = new StyleSpec();
      a.Effects.Add(TextEffect.Mark);
      a.Effects.Add(TextEffect.Shudder);
      var b = new StyleSpec();
      b.Effects.Add(TextEffect.Shudder);
      b.Effects.Add(TextEffect.Mark);
      Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_DifferentOpacity_NotEqual()
    {
      Assert.NotEqual(
        new StyleSpec { Opacity = 0.5 },
        new StyleSpec { Opacity = 0.8 });
    }

    [Fact]
    public void Equals_DifferentAlignment_NotEqual()
    {
      Assert.NotEqual(
        new StyleSpec { Alignment = TextAlignment.Center },
        new StyleSpec { Alignment = TextAlignment.Right });
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
