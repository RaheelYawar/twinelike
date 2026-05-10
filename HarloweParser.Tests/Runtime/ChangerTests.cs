using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Direct tests for the <see cref="Changer"/> primitive — composition,
  /// application order, equality, and the HTML escape helper. Macro-level
  /// behaviour is covered separately in
  /// <c>Macros/TextStyleMacroTests</c> and <c>BodyRendererTests</c>.
  /// </summary>
  public class ChangerTests
  {
    private class CapturedOutput : IRenderOutput
    {
      public System.Collections.Generic.List<string> Calls = new System.Collections.Generic.List<string>();
      public void Text(string content) => Calls.Add("T:" + content);
      public void Html(string rawHtml) => Calls.Add("H:" + rawHtml);
      public void Link(string text, string target) => Calls.Add("L:" + text + "|" + target);
      public void Error(string message) => Calls.Add("E:" + message);
    }

    [Fact]
    public void Apply_EmitsOpenContentClose()
    {
      var changer = Changer.FromHtml("<b>", "</b>");
      var output = new CapturedOutput();
      changer.Apply(output, () => output.Text("hi"));
      Assert.Equal(new[] { "H:<b>", "T:hi", "H:</b>" }, output.Calls);
    }

    [Fact]
    public void Apply_NullRenderHook_StillEmitsOpenAndClose()
    {
      // Defensive: a changer with no inner content still bracketizes nothing.
      var changer = Changer.FromHtml("<b>", "</b>");
      var output = new CapturedOutput();
      changer.Apply(output, null);
      Assert.Equal(new[] { "H:<b>", "H:</b>" }, output.Calls);
    }

    [Fact]
    public void Compose_StacksWrappers_OuterFirst()
    {
      // A.Compose(B) means A wraps B wraps content. Open order: A, B.
      // Close order: B, A. So output is `<a><b>content</b></a>`.
      var a = Changer.FromHtml("<a>", "</a>");
      var b = Changer.FromHtml("<b>", "</b>");
      var combined = a.Compose(b);
      var output = new CapturedOutput();
      combined.Apply(output, () => output.Text("x"));
      Assert.Equal(new[] { "H:<a>", "H:<b>", "T:x", "H:</b>", "H:</a>" }, output.Calls);
    }

    [Fact]
    public void Compose_ThreeWay_PreservesOrder()
    {
      var a = Changer.FromHtml("<1>", "</1>");
      var b = Changer.FromHtml("<2>", "</2>");
      var c = Changer.FromHtml("<3>", "</3>");
      var combined = a.Compose(b).Compose(c);
      var output = new CapturedOutput();
      combined.Apply(output, () => output.Text("x"));
      Assert.Equal(new[] { "H:<1>", "H:<2>", "H:<3>", "T:x", "H:</3>", "H:</2>", "H:</1>" }, output.Calls);
    }

    [Fact]
    public void Compose_WithNull_ReturnsSelf()
    {
      var a = Changer.FromHtml("<a>", "</a>");
      var combined = a.Compose(null);
      var output = new CapturedOutput();
      combined.Apply(output, () => output.Text("x"));
      Assert.Equal(new[] { "H:<a>", "T:x", "H:</a>" }, output.Calls);
    }

    [Fact]
    public void Equals_StructuralOnWrapperList()
    {
      Assert.Equal(
        Changer.FromHtml("<b>", "</b>"),
        Changer.FromHtml("<b>", "</b>"));
      Assert.NotEqual(
        Changer.FromHtml("<b>", "</b>"),
        Changer.FromHtml("<i>", "</i>"));
      Assert.Equal(
        Changer.FromHtml("<a>", "</a>").Compose(Changer.FromHtml("<b>", "</b>")),
        Changer.FromHtml("<a>", "</a>").Compose(Changer.FromHtml("<b>", "</b>")));
    }

    [Fact]
    public void Equals_OrderSensitive()
    {
      // A+B is not equal to B+A — the rendered HTML differs.
      var a = Changer.FromHtml("<a>", "</a>");
      var b = Changer.FromHtml("<b>", "</b>");
      Assert.NotEqual(a.Compose(b), b.Compose(a));
    }

    [Fact]
    public void GetHashCode_ConsistentWithEquals()
    {
      var x = Changer.FromHtml("<b>", "</b>");
      var y = Changer.FromHtml("<b>", "</b>");
      Assert.Equal(x.GetHashCode(), y.GetHashCode());
    }

    [Fact]
    public void EscapeAttribute_HandlesAllFiveSpecials()
    {
      Assert.Equal("&amp;", Changer.EscapeAttribute("&"));
      Assert.Equal("&lt;", Changer.EscapeAttribute("<"));
      Assert.Equal("&gt;", Changer.EscapeAttribute(">"));
      Assert.Equal("&quot;", Changer.EscapeAttribute("\""));
      Assert.Equal("&#39;", Changer.EscapeAttribute("'"));
    }

    [Fact]
    public void EscapeAttribute_PassesThroughSafeText()
      => Assert.Equal("rgb(255, 0, 0)", Changer.EscapeAttribute("rgb(255, 0, 0)"));

    [Fact]
    public void EscapeAttribute_NullAndEmpty()
    {
      Assert.Equal(string.Empty, Changer.EscapeAttribute(null));
      Assert.Equal(string.Empty, Changer.EscapeAttribute(string.Empty));
    }

    [Fact]
    public void EscapeAttribute_MixedContent()
      => Assert.Equal("a &amp; &lt;b&gt; &quot;c&quot;", Changer.EscapeAttribute("a & <b> \"c\""));
  }
}
