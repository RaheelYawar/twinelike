namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Convenience entry point for wiring the standard macro set onto a fresh
  /// <see cref="MacroRegistry"/>. The session calls <see cref="RegisterAll"/>
  /// once at construction; tests that want the full set use the same call.
  /// Tests that want only a subset register macros directly.
  /// </summary>
  public static class StandardMacros
  {
    /// <summary>
    /// Register every standard macro on <paramref name="registry"/>. Order
    /// does not matter; the registry is a flat name lookup. Re-registering
    /// replaces the prior implementation, so this is idempotent.
    /// </summary>
    public static void RegisterAll(MacroRegistry registry)
    {
      registry.Register(new SetMacro());
      registry.Register(new PutMacro());
      registry.Register(new PrintMacro());
      registry.Register(new IfMacro());
      registry.Register(new ElseMacro());
      // One registration covers both spellings: "else-if" and "elseif"
      // normalize to the same key (the dash is stripped).
      registry.Register(new ElseIfMacro("else-if"));
      registry.Register(new UnlessMacro());
      registry.Register(new GotoMacro());
      registry.Register(new SaveGameMacro());
      registry.Register(new LoadGameMacro());
      registry.Register(new SavedGamesMacro());
      registry.Register(new DisplayMacro());
      registry.Register(new RandomMacro());
      registry.Register(new EitherMacro());
      registry.Register(new AMacro());
      registry.Register(new DmMacro());
      registry.Register(new ModuloMacro());
      registry.Register(new TextMacro("text"));
      registry.Register(new TextMacro("str"));
      registry.Register(new TextMacro("string"));
      registry.Register(new NumMacro("num"));
      registry.Register(new NumMacro("number"));
      registry.Register(new RoundMacro());
      registry.Register(new MinMacro());
      registry.Register(new HistoryMacro());
      registry.Register(new TextStyleMacro());
      registry.Register(new TextColorMacro("text-color"));
      registry.Register(new TextColorMacro("text-colour"));
      registry.Register(new TextColorMacro("color"));
      registry.Register(new TextColorMacro("colour"));
      registry.Register(new BackgroundMacro("background"));
      registry.Register(new BackgroundMacro("bg"));
      registry.Register(new FontMacro());
      registry.Register(new TextSizeMacro("text-size"));
      registry.Register(new TextSizeMacro("size"));
      registry.Register(new OpacityMacro());
      registry.Register(new AlignMacro());
      registry.Register(new FindMacro());
      registry.Register(new AllPassMacro());
      registry.Register(new AlteredMacro());
      registry.Register(new SomePassMacro());
      registry.Register(new NonePassMacro());
      registry.Register(new ForMacro("for"));
      registry.Register(new ForMacro("loop"));
      registry.Register(new FoldedMacro());
      registry.Register(new RotatedToMacro());
      registry.Register(new SortedMacro());
      registry.Register(new ReplaceMacro());
      registry.Register(new AppendMacro());
      registry.Register(new PrependMacro());
      registry.Register(new ChangeMacro());
      registry.Register(new EnchantMacro());
      registry.Register(new InteractionMacro("click", InteractionKind.Click, RevisionMode.Replace));
      registry.Register(new InteractionMacro("click-replace", InteractionKind.Click, RevisionMode.Replace));
      registry.Register(new InteractionMacro("click-append", InteractionKind.Click, RevisionMode.Append));
      registry.Register(new InteractionMacro("click-prepend", InteractionKind.Click, RevisionMode.Prepend));
      registry.Register(new InteractionMacro("mouseover", InteractionKind.MouseOver, RevisionMode.Replace));
      registry.Register(new InteractionMacro("mouseover-replace", InteractionKind.MouseOver, RevisionMode.Replace));
      registry.Register(new InteractionMacro("mouseover-append", InteractionKind.MouseOver, RevisionMode.Append));
      registry.Register(new InteractionMacro("mouseover-prepend", InteractionKind.MouseOver, RevisionMode.Prepend));
      registry.Register(new InteractionMacro("mouseout", InteractionKind.MouseOut, RevisionMode.Replace));
      registry.Register(new InteractionMacro("mouseout-replace", InteractionKind.MouseOut, RevisionMode.Replace));
      registry.Register(new InteractionMacro("mouseout-append", InteractionKind.MouseOut, RevisionMode.Append));
      registry.Register(new InteractionMacro("mouseout-prepend", InteractionKind.MouseOut, RevisionMode.Prepend));
    }
  }
}
