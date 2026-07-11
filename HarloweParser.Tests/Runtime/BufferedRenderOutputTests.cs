using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  public class BufferedRenderOutputTests
  {
    [Fact]
    public void Text_AppendsEntryAndConcatenates()
    {
      var buf = new BufferedRenderOutput();
      IRenderOutput sink = buf;
      sink.Text("Hello, ");
      sink.Text("world.");

      Assert.Equal("Hello, world.", buf.Text);
      Assert.Equal(2, buf.Entries.Count);
      Assert.Equal(BufferedRenderOutput.Kind.Text, buf.Entries[0].Kind);
      Assert.Equal("Hello, ", buf.Entries[0].Content);
    }

    [Fact]
    public void Html_AppendsToTextStream()
    {
      var buf = new BufferedRenderOutput();
      IRenderOutput sink = buf;
      sink.Html("<b>");
      sink.Text("bold");
      sink.Html("</b>");

      Assert.Equal("<b>bold</b>", buf.Text);
      Assert.Equal(BufferedRenderOutput.Kind.Html, buf.Entries[0].Kind);
    }

    [Fact]
    public void Link_RecordsTextAndTarget_DoesNotAppendToText()
    {
      var buf = new BufferedRenderOutput();
      IRenderOutput sink = buf;
      sink.Text("Choose: ");
      sink.Link("Continue", "FirstPassage");

      Assert.Equal("Choose: ", buf.Text);
      Assert.Equal(2, buf.Entries.Count);
      Assert.Equal(BufferedRenderOutput.Kind.Link, buf.Entries[1].Kind);
      Assert.Equal("Continue", buf.Entries[1].Content);
      Assert.Equal("FirstPassage", buf.Entries[1].Target);
    }

    [Fact]
    public void Error_RecordsEntry_DoesNotAppendToText()
    {
      var buf = new BufferedRenderOutput();
      IRenderOutput sink = buf;
      sink.Text("Before ");
      sink.Error("$missing is not set");
      sink.Text(" after");

      Assert.Equal("Before  after", buf.Text);
      Assert.Equal(3, buf.Entries.Count);
      Assert.Equal(BufferedRenderOutput.Kind.Error, buf.Entries[1].Kind);
      Assert.Equal("$missing is not set", buf.Entries[1].Content);
    }

    [Fact]
    public void Entries_PreserveOrderAcrossKinds()
    {
      var buf = new BufferedRenderOutput();
      IRenderOutput sink = buf;
      sink.Text("a");
      sink.Link("L", "T");
      sink.Error("e");
      sink.Html("<br/>");

      Assert.Equal(BufferedRenderOutput.Kind.Text, buf.Entries[0].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.Link, buf.Entries[1].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.Error, buf.Entries[2].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.Html, buf.Entries[3].Kind);
    }

    [Fact]
    public void FreshBuffer_IsEmpty()
    {
      var buf = new BufferedRenderOutput();
      Assert.Empty(buf.Entries);
      Assert.Equal(string.Empty, buf.Text);
    }

    [Fact]
    public void BeginLink_RecordsTarget_LabelSuppressedFromText()
    {
      // A bracketed link's label fragments are entries but stay out of Text,
      // matching the flat Link shape (whose label never contributed).
      var buf = new BufferedRenderOutput();
      IRenderOutput sink = buf;
      sink.Text("go ");
      sink.BeginLink("P2");
      sink.Text("label");
      sink.EndLink();
      sink.Text(" now");

      Assert.Equal("go  now", buf.Text);
      Assert.Equal(BufferedRenderOutput.Kind.BeginLink, buf.Entries[1].Kind);
      Assert.Equal("P2", buf.Entries[1].Target);
      Assert.Equal(BufferedRenderOutput.Kind.Text, buf.Entries[2].Kind);
      Assert.Equal("label", buf.Entries[2].Content);
      Assert.Equal(BufferedRenderOutput.Kind.EndLink, buf.Entries[3].Kind);
    }
  }
}
