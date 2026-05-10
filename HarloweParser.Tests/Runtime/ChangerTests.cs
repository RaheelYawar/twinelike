using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Direct tests for the <see cref="Changer"/> primitive — composition,
  /// application order, and equality. Changers now describe their wrapping
  /// as <see cref="StyleSpec"/> layers rather than HTML snippets; the HTML
  /// mapping lives in <see cref="HtmlRenderOutput"/> and is covered by its
  /// own tests. Macro-level behaviour is in <c>Macros/TextStyleMacroTests</c>
  /// and <c>BodyRendererTests</c>.
  /// </summary>
  public class ChangerTests
  {
    private class CapturedOutput : IRenderOutput
    {
      public List<string> Calls = new List<string>();
      public void Text(string content) => Calls.Add("T:" + content);
      public void Html(string rawHtml) => Calls.Add("H:" + rawHtml);
      public void Link(string text, string target) => Calls.Add("L:" + text + "|" + target);
      public void Error(string message) => Calls.Add("E:" + message);
      public void PushStyle(StyleSpec style) => Calls.Add("P:" + Describe(style));
      public void PopStyle() => Calls.Add("/P");

      private static string Describe(StyleSpec s)
      {
        if (s.Bold) return "bold";
        if (s.Italic) return "italic";
        if (s.Underline) return "underline";
        return "?";
      }
    }

    private static Changer Bold() => Changer.FromStyle(new StyleSpec { Bold = true });
    private static Changer Italic() => Changer.FromStyle(new StyleSpec { Italic = true });
    private static Changer Underline() => Changer.FromStyle(new StyleSpec { Underline = true });

    [Fact]
    public void Apply_PushesStyleRendersHookPopsStyle()
    {
      var output = new CapturedOutput();
      Bold().Apply(output, () => output.Text("hi"));
      Assert.Equal(new[] { "P:bold", "T:hi", "/P" }, output.Calls);
    }

    [Fact]
    public void Apply_NullRenderHook_StillEmitsPushAndPop()
    {
      // Defensive: a changer with no inner content still brackets nothing.
      var output = new CapturedOutput();
      Bold().Apply(output, null);
      Assert.Equal(new[] { "P:bold", "/P" }, output.Calls);
    }

    [Fact]
    public void Compose_StacksLayers_OuterFirst()
    {
      // A.Compose(B) means A wraps B wraps content. Push order: A, B.
      // Pop order is implicit (reverse-stack on the consumer side); the
      // adapter is responsible for closing in reverse.
      var combined = Bold().Compose(Italic());
      var output = new CapturedOutput();
      combined.Apply(output, () => output.Text("x"));
      Assert.Equal(new[] { "P:bold", "P:italic", "T:x", "/P", "/P" }, output.Calls);
    }

    [Fact]
    public void Compose_ThreeWay_PreservesOrder()
    {
      var combined = Bold().Compose(Italic()).Compose(Underline());
      var output = new CapturedOutput();
      combined.Apply(output, () => output.Text("x"));
      Assert.Equal(
        new[] { "P:bold", "P:italic", "P:underline", "T:x", "/P", "/P", "/P" },
        output.Calls);
    }

    [Fact]
    public void Compose_WithNull_ReturnsSelf()
    {
      var combined = Bold().Compose(null);
      var output = new CapturedOutput();
      combined.Apply(output, () => output.Text("x"));
      Assert.Equal(new[] { "P:bold", "T:x", "/P" }, output.Calls);
    }

    [Fact]
    public void Equals_StructuralOnLayerList()
    {
      Assert.Equal(Bold(), Bold());
      Assert.NotEqual(Bold(), Italic());
      Assert.Equal(Bold().Compose(Italic()), Bold().Compose(Italic()));
    }

    [Fact]
    public void Equals_OrderSensitive()
    {
      // A+B is not equal to B+A — the rendered nesting differs.
      Assert.NotEqual(Bold().Compose(Italic()), Italic().Compose(Bold()));
    }

    [Fact]
    public void GetHashCode_ConsistentWithEquals()
    {
      Assert.Equal(Bold().GetHashCode(), Bold().GetHashCode());
    }

    [Fact]
    public void Layers_ExposesUnderlyingStyleList()
    {
      // Diagnostic accessor — engine adapters generally use Apply, but the
      // raw layer list is useful for inspection.
      var c = Bold().Compose(Italic());
      Assert.Equal(2, c.Layers.Count);
      Assert.True(c.Layers[0].Bold);
      Assert.True(c.Layers[1].Italic);
    }

    [Fact]
    public void FromStyle_NullStyle_TreatedAsEmptyLayer()
    {
      // Defensive — null in shouldn't crash; layer is just an empty spec.
      var c = Changer.FromStyle(null);
      Assert.Single(c.Layers);
      Assert.True(c.Layers[0].IsEmpty);
    }
  }
}
