using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Delivers the library's full event stream to any host output. Implements
  /// every capability interface; per feature, it forwards to the wrapped output
  /// when that output implements the feature's interface and otherwise
  /// <em>lowers</em> the feature to core <see cref="IRenderOutput"/> channels.
  /// There is one adapter rather than a wrapper per feature because a wrapper
  /// exposes only the interfaces it implements itself, so stacking them would
  /// hide a host's support for the other features.
  ///
  /// <para><b>Link-label lowering.</b> From <see cref="BeginLink"/> to the
  /// matching <see cref="EndLink"/>, text is buffered as received (this adapter
  /// escapes nothing, so nothing is escaped twice) and inner links join it with
  /// their targets dropped; HTML, styles and interactive regions are
  /// suppressed; errors are held. <see cref="EndLink"/> then emits one flat
  /// <see cref="Link"/> followed by the errors in order. The text equals
  /// <see cref="Rendering.RenderLinkNode.Text"/>'s flattening.</para>
  ///
  /// <para>Stateful across a stream: use one instance per flush or replay.</para>
  /// </summary>
  internal sealed class OutputFeatureAdapter : ILinkLabelOutput
  {
    private readonly IRenderOutput _inner;

    // The wrapped output's link-label support; null when labels are lowered here.
    private readonly ILinkLabelOutput _linkLabels;

    // Lowering state for the outermost open label. Depth counts nested brackets;
    // it stays 0 whenever _linkLabels is set, so every core channel forwards.
    private int _labelDepth;
    private string _labelTarget;
    private StringBuilder _labelText;
    private List<string> _labelErrors;

    /// <summary>Wrap <paramref name="inner"/>, forwarding each feature it implements and lowering the rest.</summary>
    public OutputFeatureAdapter(IRenderOutput inner) : this(inner, lowerLinkLabels: false) { }

    private OutputFeatureAdapter(IRenderOutput inner, bool lowerLinkLabels)
    {
      _inner = inner;
      _linkLabels = lowerLinkLabels ? null : inner as ILinkLabelOutput;
    }

    /// <summary>The link-label sink behind <paramref name="output"/>, seeing through an adapter. Null when the output would receive labels lowered.</summary>
    public static ILinkLabelOutput LinkLabelsOf(IRenderOutput output)
      => output is OutputFeatureAdapter adapter ? adapter._linkLabels : output as ILinkLabelOutput;

    /// <summary>
    /// Lower every bracket in a finished, balanced stream to a flat
    /// <see cref="BufferedRenderOutput.Kind.Link"/> entry followed by the
    /// label's errors. Each such entry keeps the label's other events, so a
    /// later replay into an <see cref="ILinkLabelOutput"/> can still emit the
    /// bracket. Returns <paramref name="entries"/> itself when it holds none.
    /// </summary>
    public static List<BufferedRenderOutput.Entry> LowerEntries(List<BufferedRenderOutput.Entry> entries)
    {
      List<BufferedRenderOutput.Entry> lowered = null;
      for (int i = 0; i < entries.Count; i++)
      {
        if (entries[i].Kind == BufferedRenderOutput.Kind.BeginLink)
        {
          if (lowered == null) lowered = entries.GetRange(0, i);
          i = LowerLabel(entries, i, lowered);
        }
        else if (lowered != null)
        {
          lowered.Add(entries[i]);
        }
      }
      return lowered ?? entries;
    }

    /// <summary>Lower the bracket opening at <paramref name="begin"/> into <paramref name="lowered"/>; returns the index of its closing entry.</summary>
    private static int LowerLabel(List<BufferedRenderOutput.Entry> entries, int begin, List<BufferedRenderOutput.Entry> lowered)
    {
      var capture = new BufferedRenderOutput();
      var adapter = new OutputFeatureAdapter(capture, lowerLinkLabels: true);
      var label = new List<BufferedRenderOutput.Entry>();
      int depth = 0;
      int i = begin;
      for (; ; i++)
      {
        var entry = entries[i];
        if (entry.Kind == BufferedRenderOutput.Kind.BeginLink) depth++;
        else if (entry.Kind == BufferedRenderOutput.Kind.EndLink) depth--;
        entry.ReplayTo(adapter);
        if (depth == 0) break;
        // Errors become separate entries after the link, so keeping them in
        // the label too would deliver them twice on a bracketed replay.
        if (i > begin && entry.Kind != BufferedRenderOutput.Kind.Error) label.Add(entry);
      }
      var link = capture.Entries[0];
      link.StructuredLabel = label;
      link.StructuredLabelText = link.Content;
      lowered.AddRange(capture.Entries);
      return i;
    }

    public void Text(string content)
    {
      if (_labelDepth > 0) _labelText.Append(content);
      else _inner.Text(content);
    }

    public void Html(string rawHtml)
    {
      if (_labelDepth == 0) _inner.Html(rawHtml);
    }

    public void Link(string text, string target)
    {
      if (_labelDepth > 0) _labelText.Append(text);
      else _inner.Link(text, target);
    }

    public void Error(string message)
    {
      if (_labelDepth == 0)
      {
        _inner.Error(message);
        return;
      }
      if (_labelErrors == null) _labelErrors = new List<string>();
      _labelErrors.Add(message);
    }

    public void PushStyle(StyleSpec style)
    {
      if (_labelDepth == 0) _inner.PushStyle(style);
    }

    public void PopStyle()
    {
      if (_labelDepth == 0) _inner.PopStyle();
    }

    public void BeginInteractive(InteractiveRegion region)
    {
      if (_labelDepth == 0) _inner.BeginInteractive(region);
    }

    public void EndInteractive()
    {
      if (_labelDepth == 0) _inner.EndInteractive();
    }

    public void BeginLink(string target)
    {
      if (_linkLabels != null)
      {
        _linkLabels.BeginLink(target);
        return;
      }
      if (_labelDepth++ > 0) return;
      _labelTarget = target;
      _labelText = new StringBuilder();
      _labelErrors = null;
    }

    public void EndLink()
    {
      if (_linkLabels != null)
      {
        _linkLabels.EndLink();
        return;
      }
      if (_labelDepth == 0 || --_labelDepth > 0) return;

      var text = _labelText.ToString();
      var target = _labelTarget;
      var errors = _labelErrors;
      _labelTarget = null;
      _labelText = null;
      _labelErrors = null;

      _inner.Link(text, target);
      if (errors == null) return;
      for (int i = 0; i < errors.Count; i++) _inner.Error(errors[i]);
    }
  }
}
