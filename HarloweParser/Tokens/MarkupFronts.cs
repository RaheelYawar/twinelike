using System;
using System.Collections.Generic;

namespace Harlowe.Tokens
{
  /// <summary>Which paired markup a <see cref="MarkupFronts"/> entry opens.</summary>
  internal enum FrontKind
  {
    Hook,
    NamedHook,
    Collapse,
    Bold,
    Italic,
    Strike,
    Superscript,
    Emphasis,
    Strong,
    Verbatim
  }

  /// <summary>
  /// The markup a body scan has opened and not yet closed: reference Harlowe's
  /// <c>frontTokenStack</c> in <c>ts/markup/lexer.ts</c>. A closer pairs with
  /// the nearest front of its kind and discards every front above it, which
  /// then stays literal text, so surviving pairs always nest. Only a verbatim
  /// closer can reach past an open verbatim front (reference's
  /// <c>cannotCross: [`verbatimOpener`]</c> on every other closer).
  ///
  /// <para>Each lookup is constant time, because a passage can hold hundreds of
  /// thousands of unmatched fronts and a scan down the stack per closer would
  /// be quadratic.</para>
  /// </summary>
  internal sealed class MarkupFronts
  {
    private struct Front
    {
      public FrontKind Kind;
      public int Run;
      public int TokenIndex;
      public int PreviousSameKind;
      public int NearestVerbatim;
    }

    private readonly List<Front> _stack = new List<Front>();
    private readonly int[] _nearest;
    private readonly Dictionary<int, int> _nearestVerbatimByRun = new Dictionary<int, int>();

    public MarkupFronts()
    {
      _nearest = new int[Enum.GetValues(typeof(FrontKind)).Length];
      for (int i = 0; i < _nearest.Length; i++) _nearest[i] = -1;
    }

    private int Barrier => _stack.Count == 0 ? -1 : _stack[_stack.Count - 1].NearestVerbatim;

    /// <summary>Opens a front for the token at <paramref name="tokenIndex"/>. <paramref name="run"/> is a verbatim opener's grave count.</summary>
    public void Push(FrontKind kind, int tokenIndex, int run = 0)
    {
      int at = _stack.Count;
      int previous;
      if (kind == FrontKind.Verbatim)
      {
        if (!_nearestVerbatimByRun.TryGetValue(run, out previous)) previous = -1;
        _nearestVerbatimByRun[run] = at;
      }
      else
      {
        previous = _nearest[(int)kind];
        _nearest[(int)kind] = at;
      }
      int barrier = kind == FrontKind.Verbatim ? at : Barrier;
      _stack.Add(new Front { Kind = kind, Run = run, TokenIndex = tokenIndex, PreviousSameKind = previous, NearestVerbatim = barrier });
    }

    /// <summary>True when a closer of <paramref name="kind"/> has a front to pair with.</summary>
    public bool CanClose(FrontKind kind) => _nearest[(int)kind] > Barrier;

    /// <summary>Pairs a closer of <paramref name="kind"/>. Returns the front's token index, or -1 when there is none.</summary>
    public int Close(FrontKind kind) => CanClose(kind) ? TakeFrom(_nearest[(int)kind]) : -1;

    /// <summary>
    /// Pairs a <c>]</c>. A <c>]&lt;name|</c> can only close a plain <c>[</c>,
    /// since a hook can't be named at both ends (reference's
    /// <c>hookAppendedBack</c> matches <c>hookFront</c> alone).
    /// </summary>
    public int CloseHook(bool namesItself)
    {
      int at = _nearest[(int)FrontKind.Hook];
      if (!namesItself) at = Math.Max(at, _nearest[(int)FrontKind.NamedHook]);
      return at > Barrier ? TakeFrom(at) : -1;
    }

    /// <summary>Pairs a run of <paramref name="run"/> graves with the nearest opener of the same length.</summary>
    public int CloseVerbatim(int run) =>
      _nearestVerbatimByRun.TryGetValue(run, out int at) ? TakeFrom(at) : -1;

    private int TakeFrom(int at)
    {
      int tokenIndex = _stack[at].TokenIndex;
      for (int i = _stack.Count - 1; i >= at; i--)
      {
        var front = _stack[i];
        if (front.Kind != FrontKind.Verbatim) _nearest[(int)front.Kind] = front.PreviousSameKind;
        else if (front.PreviousSameKind < 0) _nearestVerbatimByRun.Remove(front.Run);
        else _nearestVerbatimByRun[front.Run] = front.PreviousSameKind;
      }
      _stack.RemoveRange(at, _stack.Count - at);
      return tokenIndex;
    }
  }
}
