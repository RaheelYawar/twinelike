using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Recording <see cref="IRenderOutput"/>: every callback appends a typed
  /// entry to <see cref="Entries"/> and a flat string fragment to an internal
  /// builder. Useful as a test double — assertions can either inspect the
  /// ordered call log or the concatenated <see cref="Text"/> for prose-level
  /// checks. Lives in the production assembly because the body renderer's own
  /// tests need it; engine integrations should write their own
  /// <see cref="IRenderOutput"/> rather than depending on this type.
  ///
  /// <para><see cref="Text"/> includes <c>Text</c> and <c>Html</c> fragments
  /// (raw author HTML pass-through), but not style events — styles are
  /// out-of-band metadata. To inspect styling, walk <see cref="Entries"/> for
  /// <see cref="Kind.PushStyle"/>/<see cref="Kind.PopStyle"/>.</para>
  /// </summary>
  public class BufferedRenderOutput : IRenderOutput
  {
    /// <summary>Kind tag for an entry in <see cref="Entries"/>.</summary>
    public enum Kind { Text, Html, Link, Error, PushStyle, PopStyle, BeginInteractive, EndInteractive, BeginLink, EndLink }

    /// <summary>One recorded callback. <see cref="Target"/> is non-null only for <see cref="Kind.Link"/> and <see cref="Kind.BeginLink"/>; <see cref="Style"/> is non-null only for <see cref="Kind.PushStyle"/>; <see cref="Region"/> is non-null only for <see cref="Kind.BeginInteractive"/>.</summary>
    public class Entry
    {
      public Kind Kind;
      public string Content;
      public string Target;
      public StyleSpec Style;
      public InteractiveRegion Region;

      /// <summary>
      /// Dispatch this entry to the <see cref="IRenderOutput"/> method it
      /// records, so a stored render (<see cref="RenderResult.Entries"/>) can
      /// be replayed through any adapter — e.g. an
      /// <see cref="HtmlRenderOutput"/> wrapping the host's sink.
      /// </summary>
      public void ReplayTo(IRenderOutput output)
      {
        switch (Kind)
        {
          case Kind.Text: output.Text(Content); break;
          case Kind.Html: output.Html(Content); break;
          case Kind.Link: output.Link(Content, Target); break;
          case Kind.BeginLink: output.BeginLink(Target); break;
          case Kind.EndLink: output.EndLink(); break;
          case Kind.Error: output.Error(Content); break;
          case Kind.PushStyle: output.PushStyle(Style); break;
          case Kind.PopStyle: output.PopStyle(); break;
          case Kind.BeginInteractive: output.BeginInteractive(Region); break;
          case Kind.EndInteractive: output.EndInteractive(); break;
        }
      }
    }

    /// <summary>Ordered log of every render call.</summary>
    public readonly List<Entry> Entries = new List<Entry>();

    private readonly StringBuilder _text = new StringBuilder();

    // Depth of open BeginLink brackets. Link labels never contribute to Text
    // — a flat Link entry's text doesn't, so a bracketed label's fragments
    // are suppressed too, keeping the property's meaning stable across the
    // two link shapes.
    private int _linkDepth;

    /// <summary>Concatenation of every <see cref="IRenderOutput.Text"/> and <see cref="IRenderOutput.Html"/> fragment received, in order. Style events and link labels (either shape) do not contribute.</summary>
    public string Text => _text.ToString();

    void IRenderOutput.Text(string content)
    {
      Entries.Add(new Entry { Kind = Kind.Text, Content = content });
      if (_linkDepth == 0) _text.Append(content);
    }

    void IRenderOutput.Html(string rawHtml)
    {
      Entries.Add(new Entry { Kind = Kind.Html, Content = rawHtml });
      if (_linkDepth == 0) _text.Append(rawHtml);
    }

    void IRenderOutput.Link(string text, string target)
    {
      Entries.Add(new Entry { Kind = Kind.Link, Content = text, Target = target });
    }

    void IRenderOutput.BeginLink(string target)
    {
      Entries.Add(new Entry { Kind = Kind.BeginLink, Target = target });
      _linkDepth++;
    }

    void IRenderOutput.EndLink()
    {
      Entries.Add(new Entry { Kind = Kind.EndLink });
      if (_linkDepth > 0) _linkDepth--;
    }

    void IRenderOutput.Error(string message)
    {
      Entries.Add(new Entry { Kind = Kind.Error, Content = message });
    }

    void IRenderOutput.PushStyle(StyleSpec style)
    {
      Entries.Add(new Entry { Kind = Kind.PushStyle, Style = style });
    }

    void IRenderOutput.PopStyle()
    {
      Entries.Add(new Entry { Kind = Kind.PopStyle });
    }

    void IRenderOutput.BeginInteractive(InteractiveRegion region)
    {
      Entries.Add(new Entry { Kind = Kind.BeginInteractive, Region = region });
    }

    void IRenderOutput.EndInteractive()
    {
      Entries.Add(new Entry { Kind = Kind.EndInteractive });
    }
  }
}
