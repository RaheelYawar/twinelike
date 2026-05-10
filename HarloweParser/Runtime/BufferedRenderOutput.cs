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
    public enum Kind { Text, Html, Link, Error, PushStyle, PopStyle }

    /// <summary>One recorded callback. <see cref="Target"/> is non-null only for <see cref="Kind.Link"/>; <see cref="Style"/> is non-null only for <see cref="Kind.PushStyle"/>.</summary>
    public class Entry
    {
      public Kind Kind;
      public string Content;
      public string Target;
      public StyleSpec Style;
    }

    /// <summary>Ordered log of every render call.</summary>
    public readonly List<Entry> Entries = new List<Entry>();

    private readonly StringBuilder _text = new StringBuilder();

    /// <summary>Concatenation of every <see cref="IRenderOutput.Text"/> and <see cref="IRenderOutput.Html"/> fragment received, in order. Style events do not contribute.</summary>
    public string Text => _text.ToString();

    void IRenderOutput.Text(string content)
    {
      Entries.Add(new Entry { Kind = Kind.Text, Content = content });
      _text.Append(content);
    }

    void IRenderOutput.Html(string rawHtml)
    {
      Entries.Add(new Entry { Kind = Kind.Html, Content = rawHtml });
      _text.Append(rawHtml);
    }

    void IRenderOutput.Link(string text, string target)
    {
      Entries.Add(new Entry { Kind = Kind.Link, Content = text, Target = target });
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
  }
}
