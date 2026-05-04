using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  public class MacroRegistryTests
  {
    private class FakeMacro : IMacro
    {
      public string Name { get; set; } = "fake";
      public int MinArgs { get; set; } = 0;
      public int MaxArgs { get; set; } = -1;
      public int CallCount;
      public List<HarloweValue> LastArgs;
      public MacroContext LastContext;
      public HarloweValue Result = HarloweValue.OfString("ok");

      public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      {
        CallCount++;
        LastArgs = args;
        LastContext = context;
        return Result;
      }
    }

    [Fact]
    public void Register_AndContains()
    {
      var reg = new MacroRegistry();
      reg.Register(new FakeMacro { Name = "x" });
      Assert.True(reg.Contains("x"));
      Assert.False(reg.Contains("y"));
    }

    [Fact]
    public void Register_ReplacesExistingMacro()
    {
      var reg = new MacroRegistry();
      var first = new FakeMacro { Name = "x", Result = HarloweValue.OfNumber(1) };
      var second = new FakeMacro { Name = "x", Result = HarloweValue.OfNumber(2) };
      reg.Register(first);
      reg.Register(second);
      var result = reg.Invoke("x", new List<HarloweValue>(), new MacroContext());
      Assert.Equal(2, result.AsNumber);
      Assert.Equal(0, first.CallCount);
    }

    [Fact]
    public void UnknownMacro_ReturnsError()
    {
      var reg = new MacroRegistry();
      var v = reg.Invoke("nope", new List<HarloweValue>(), new MacroContext());
      Assert.True(v.IsError);
      Assert.Contains("nope", v.ErrorMessage);
    }

    [Fact]
    public void TooFewArgs_ReturnsError()
    {
      var reg = new MacroRegistry();
      reg.Register(new FakeMacro { Name = "x", MinArgs = 2, MaxArgs = 3 });
      var v = reg.Invoke("x", new List<HarloweValue> { HarloweValue.OfNumber(1) }, new MacroContext());
      Assert.True(v.IsError);
      Assert.Contains("at least 2", v.ErrorMessage);
    }

    [Fact]
    public void TooManyArgs_ReturnsError()
    {
      var reg = new MacroRegistry();
      reg.Register(new FakeMacro { Name = "x", MinArgs = 1, MaxArgs = 1 });
      var v = reg.Invoke("x", new List<HarloweValue> { HarloweValue.OfNumber(1), HarloweValue.OfNumber(2) }, new MacroContext());
      Assert.True(v.IsError);
      Assert.Contains("at most 1", v.ErrorMessage);
    }

    [Fact]
    public void VariadicMacro_AcceptsAnyCount()
    {
      var reg = new MacroRegistry();
      var fake = new FakeMacro { Name = "x", MinArgs = 0, MaxArgs = -1 };
      reg.Register(fake);
      reg.Invoke("x", new List<HarloweValue>(), new MacroContext());
      reg.Invoke("x", new List<HarloweValue> { HarloweValue.OfNumber(1) }, new MacroContext());
      reg.Invoke("x", new List<HarloweValue>
      {
        HarloweValue.OfNumber(1), HarloweValue.OfNumber(2), HarloweValue.OfNumber(3)
      }, new MacroContext());
      Assert.Equal(3, fake.CallCount);
    }

    [Fact]
    public void NullReturnFromMacro_BecomesEmptyString()
    {
      var reg = new MacroRegistry();
      reg.Register(new FakeMacro { Name = "x", Result = null });
      var v = reg.Invoke("x", new List<HarloweValue>(), new MacroContext());
      Assert.Equal(HarloweValueKind.String, v.Kind);
      Assert.Equal(string.Empty, v.AsString);
    }

    [Fact]
    public void MacroReceivesContext()
    {
      var reg = new MacroRegistry();
      var fake = new FakeMacro { Name = "x" };
      reg.Register(fake);
      var ctx = new MacroContext();
      reg.Invoke("x", new List<HarloweValue>(), ctx);
      Assert.Same(ctx, fake.LastContext);
    }

    [Fact]
    public void IMacroInvoker_UsesPreSetContext()
    {
      var reg = new MacroRegistry();
      var fake = new FakeMacro { Name = "x" };
      reg.Register(fake);
      var ctx = new MacroContext();
      reg.Context = ctx;

      // Invoke via the IMacroInvoker.Invoke(name, args) overload — no context arg.
      IMacroInvoker invoker = reg;
      invoker.Invoke("x", new List<HarloweValue>());

      Assert.Same(ctx, fake.LastContext);
    }
  }
}
