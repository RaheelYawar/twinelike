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
      public void BeginInteractive(InteractiveRegion region) => Calls.Add("I:" + region?.Id);
      public void EndInteractive() => Calls.Add("/I");
      public void BeginLink(string target) => Calls.Add("BL:" + target);
      public void EndLink() => Calls.Add("/BL");

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
      Bold().Apply(output, o => o.Text("hi"));
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
      combined.Apply(output, o => o.Text("x"));
      Assert.Equal(new[] { "P:bold", "P:italic", "T:x", "/P", "/P" }, output.Calls);
    }

    [Fact]
    public void Compose_ThreeWay_PreservesOrder()
    {
      var combined = Bold().Compose(Italic()).Compose(Underline());
      var output = new CapturedOutput();
      combined.Apply(output, o => o.Text("x"));
      Assert.Equal(
        new[] { "P:bold", "P:italic", "P:underline", "T:x", "/P", "/P", "/P" },
        output.Calls);
    }

    [Fact]
    public void Compose_WithNull_ReturnsSelf()
    {
      var combined = Bold().Compose(null);
      var output = new CapturedOutput();
      combined.Apply(output, o => o.Text("x"));
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
    public void FromStyle_NullStyle_TreatedAsEmptyLayer()
    {
      // Defensive — null in shouldn't crash; it becomes one empty-spec layer,
      // observable as a single push/pop (the "?" describe = an empty spec).
      var output = new CapturedOutput();
      Changer.FromStyle(null).Apply(output, o => o.Text("z"));
      Assert.Equal(new[] { "P:?", "T:z", "/P" }, output.Calls);
    }

    // Patch structural equality — the per-patch Equals/GetHashCode contract
    // Changer.Equals recurses into when authors compare stored changers with
    // `is` (see HarloweValue.Equals) ----------------------------------------

    private static HookNameValue Hook(string name) => new HookNameValue { Name = name };

    [Fact]
    public void ClearStylesPatch_EqualsAnyOtherInstance()
    {
      Assert.True(new ClearStylesPatch().Equals(new ClearStylesPatch()));
      Assert.False(new ClearStylesPatch().Equals(new StylePatch()));
      Assert.Equal(new ClearStylesPatch().GetHashCode(), new ClearStylesPatch().GetHashCode());
    }

    [Fact]
    public void ConditionalPatch_HashesByKindAndValue()
    {
      var a = new ConditionalPatch { Kind = ConditionalKind.If, Value = true };
      var b = new ConditionalPatch { Kind = ConditionalKind.If, Value = true };
      Assert.Equal(a, b);
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
      Assert.NotEqual(a.GetHashCode(),
        new ConditionalPatch { Kind = ConditionalKind.Unless, Value = true }.GetHashCode());
    }

    [Fact]
    public void IterationPatch_EqualityComparesParamAndItems()
    {
      IterationPatch Make(string param, double item) => new IterationPatch
      {
        Iteration = new IterationSpec
        {
          ParamName = param,
          ParamIsTemporary = true,
          Items = new List<HarloweValue> { HarloweValue.OfNumber(item) },
        }
      };

      var a = Make("x", 1);
      Assert.Equal(a, Make("x", 1));
      Assert.Equal(a.GetHashCode(), Make("x", 1).GetHashCode());
      Assert.NotEqual(a, Make("y", 1));
      Assert.NotEqual(a, Make("x", 2));

      // Null-spec pairs are equal; a null spec never equals a populated one.
      Assert.Equal(new IterationPatch(), new IterationPatch());
      Assert.NotEqual(a, new IterationPatch());
      Assert.Equal(new IterationPatch().GetHashCode(), new IterationPatch().GetHashCode());
    }

    [Fact]
    public void IterationPatch_LambdaComparesByReference()
    {
      var shared = new LambdaValue { Node = new Ast.Expression.LambdaNode() };
      IterationPatch With(LambdaValue lambda) => new IterationPatch
      {
        Iteration = new IterationSpec { Lambda = lambda, ParamName = "x" }
      };

      Assert.Equal(With(shared), With(shared));
      Assert.NotEqual(With(shared), With(new LambdaValue { Node = new Ast.Expression.LambdaNode() }));
    }

    [Fact]
    public void RevisionPatch_EqualityByModeAndTarget()
    {
      RevisionPatch HookTargeted(RevisionMode mode, string hook) => new RevisionPatch
      {
        Revisions = new List<RevisionSpec> { new RevisionSpec { Mode = mode, HookTarget = Hook(hook) } }
      };
      RevisionPatch StringTargeted(string needle) => new RevisionPatch
      {
        Revisions = new List<RevisionSpec> { new RevisionSpec { Mode = RevisionMode.Replace, StringTarget = needle } }
      };

      var a = HookTargeted(RevisionMode.Replace, "x");
      Assert.Equal(a, HookTargeted(RevisionMode.Replace, "X")); // hook names are case-insensitive
      Assert.Equal(a.GetHashCode(), HookTargeted(RevisionMode.Replace, "x").GetHashCode());
      Assert.NotEqual(a, HookTargeted(RevisionMode.Append, "x"));
      Assert.NotEqual(a, StringTargeted("x"));
      Assert.Equal(StringTargeted("old"), StringTargeted("old"));
      Assert.Equal(StringTargeted("old").GetHashCode(), StringTargeted("old").GetHashCode());
      Assert.Equal(new RevisionPatch(), new RevisionPatch());
      Assert.NotEqual(a, new RevisionPatch());
    }

    [Fact]
    public void InteractionPatch_EqualityComparesEveryField()
    {
      InteractionSpec Spec() => new InteractionSpec
      {
        Kind = InteractionKind.Click,
        Mode = RevisionMode.Append,
        Once = true,
        StringTarget = "gold",
        LinkText = null,
        LinkReplacesLabel = false,
        RevealMode = RevisionMode.Append,
      };
      InteractionPatch Patch(InteractionSpec spec) => new InteractionPatch { Interaction = spec };

      var a = Patch(Spec());
      Assert.Equal(a, Patch(Spec()));
      Assert.Equal(a.GetHashCode(), Patch(Spec()).GetHashCode());

      var kind = Spec(); kind.Kind = InteractionKind.MouseOver;
      Assert.NotEqual(a, Patch(kind));
      var mode = Spec(); mode.Mode = null;
      Assert.NotEqual(a, Patch(mode));
      var once = Spec(); once.Once = false;
      Assert.NotEqual(a, Patch(once));
      var target = Spec(); target.StringTarget = "silver";
      Assert.NotEqual(a, Patch(target));
      var link = Spec(); link.LinkText = "Go";
      Assert.NotEqual(a, Patch(link));
      var replaces = Spec(); replaces.LinkReplacesLabel = true;
      Assert.NotEqual(a, Patch(replaces));
      var reveal = Spec(); reveal.RevealMode = RevisionMode.Replace;
      Assert.NotEqual(a, Patch(reveal));

      var hookA = Spec(); hookA.StringTarget = null; hookA.HookTarget = Hook("h");
      var hookB = Spec(); hookB.StringTarget = null; hookB.HookTarget = Hook("H");
      Assert.Equal(Patch(hookA), Patch(hookB));
      Assert.NotEqual(a, Patch(hookA));

      Assert.Equal(new InteractionPatch(), new InteractionPatch());
      Assert.NotEqual(a, new InteractionPatch());
    }

    [Fact]
    public void HookNameValue_HashMatchesCaseInsensitiveEquality()
    {
      Assert.Equal(Hook("Cake"), Hook("cake"));
      Assert.Equal(Hook("Cake").GetHashCode(), Hook("cake").GetHashCode());
    }
  }
}
