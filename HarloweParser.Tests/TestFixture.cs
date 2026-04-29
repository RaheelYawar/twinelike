using System;
using System.IO;

namespace Harlowe.Tests
{
  internal static class TestFixture
  {
    private const string TestFileRelativePath = "TestFiles/testFile.html";

    private static string _testFileHtml;

    public static string TestFileHtml
    {
      get
      {
        if (_testFileHtml != null) return _testFileHtml;

        var path = Path.Combine(AppContext.BaseDirectory, TestFileRelativePath);
        _testFileHtml = File.ReadAllText(path);
        return _testFileHtml;
      }
    }

    /// <summary>
    /// Builds a fresh <see cref="Harlowe"/> from the cached fixture HTML.
    /// Each test gets its own instance so mutations cannot leak between tests.
    /// </summary>
    public static Harlowe LoadTestFile() => new Harlowe(TestFileHtml);
  }
}
