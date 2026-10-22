using System.Collections.Generic;
using Xunit;

namespace Harlowe.Tests
{
  public class PublicApiTests
  {
    [Fact]
    public void NoPublicTypeSharesANameWithANamespace()
    {
      // A type named like a namespace (the story class was once Harlowe.Harlowe)
      // breaks consumers: with `using Harlowe;`, `new Harlowe(...)` resolves to
      // the namespace and fails with CS0118. Code compiled inside the namespace,
      // like these tests, resolves the type instead, so only this guard sees it.
      var exported = typeof(HarloweStory).Assembly.GetExportedTypes();
      var segments = new HashSet<string>();
      foreach (var type in exported)
        if (type.Namespace != null)
          foreach (var segment in type.Namespace.Split('.'))
            segments.Add(segment);

      foreach (var type in exported)
        Assert.False(segments.Contains(type.Name), $"public type {type.FullName} is named like a namespace");
    }
  }
}
