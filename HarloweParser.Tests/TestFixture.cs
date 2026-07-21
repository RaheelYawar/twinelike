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
    ///
    /// <para><b>This is a Harlowe 3 fixture.</b> It is a real Twine export
    /// declaring <c>format-version="3.3.9"</c>, so it loads under
    /// <see cref="HarloweProfile.V3"/> — correctly, since that is what it is.
    /// Its passage bodies contain no <c>--</c>, so no test reading it depends
    /// on the comment-markup switch either way; the only <c>--</c> in the file
    /// sits inside the embedded engine JavaScript, well past the passage
    /// data. A test needing newest-major semantics should build its own story
    /// rather than change this file's declared version.</para>
    /// </summary>
    public static Harlowe LoadTestFile() => new Harlowe(TestFileHtml);
  }
}
