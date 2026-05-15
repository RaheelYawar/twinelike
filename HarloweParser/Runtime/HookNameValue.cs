using System;
using System.Collections.Generic;
using Harlowe.Ast.Expression;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Runtime value behind <see cref="HarloweValueKind.HookName"/>: a selector
  /// spec — a name plus the ordinal steps that narrow it. A hook name is a
  /// <em>query</em>, re-resolved against the live render tree every time a
  /// macro consumes it (see <see cref="Rendering.HookResolver"/>), never a
  /// captured node reference. This mirrors the reference Harlowe runtime, where
  /// a <c>HookSet</c> is a <c>{section, selector}</c> pair lazily re-run against
  /// the DOM.
  ///
  /// <para>
  /// Equality is structural over name (case-insensitively, matching Harlowe's
  /// case-insensitive hook names) plus steps. Authors rarely compare hook
  /// names, but the tagged union wants a sane <see cref="Equals"/>.
  /// </para>
  /// </summary>
  public class HookNameValue
  {
    /// <summary>The hook name without the leading <c>?</c>. Built-ins: <c>page</c>, <c>passage</c>, <c>link</c>.</summary>
    public string Name;

    /// <summary>Ordinal narrowing steps applied after the name match. Never null in practice; treated as empty when null.</summary>
    public IReadOnlyList<HookRefStep> Steps;

    public override bool Equals(object obj)
    {
      if (!(obj is HookNameValue other)) return false;
      if (!string.Equals(Name ?? string.Empty, other.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        return false;
      var a = Steps;
      var b = other.Steps;
      int ac = a?.Count ?? 0;
      int bc = b?.Count ?? 0;
      if (ac != bc) return false;
      for (int i = 0; i < ac; i++)
        if (!Equals(a[i], b[i])) return false;
      return true;
    }

    public override int GetHashCode()
    {
      int h = (Name ?? string.Empty).ToLowerInvariant().GetHashCode();
      h = (h * 397) ^ (Steps?.Count ?? 0);
      return h;
    }
  }
}
