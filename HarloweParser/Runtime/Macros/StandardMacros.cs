namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Convenience entry point for wiring the v1 macro set onto a fresh
  /// <see cref="MacroRegistry"/>. The session calls <see cref="RegisterAll"/>
  /// once at construction; tests that want the full set use the same call.
  /// Tests that want only a subset register macros directly.
  /// </summary>
  public static class StandardMacros
  {
    /// <summary>
    /// Register every v1 macro on <paramref name="registry"/>. Order does not
    /// matter; the registry is a flat name lookup. Re-registering replaces
    /// the prior implementation, so this is idempotent.
    /// </summary>
    public static void RegisterAll(MacroRegistry registry)
    {
      registry.Register(new SetMacro());
      registry.Register(new PutMacro());
      registry.Register(new PrintMacro());
      registry.Register(new IfMacro());
      registry.Register(new ElseMacro());
      registry.Register(new UnlessMacro());
      registry.Register(new GotoMacro());
      registry.Register(new DisplayMacro());
      registry.Register(new RandomMacro());
      registry.Register(new EitherMacro());
      registry.Register(new AMacro());
      registry.Register(new DmMacro());
      registry.Register(new ModuloMacro());
      registry.Register(new TextMacro());
      registry.Register(new NumMacro());
      registry.Register(new HistoryMacro());
    }
  }
}
