using System;
using System.IO;
using Harlowe;

namespace HarloweParser
{
  internal class Program
  {
    public static void Main(string[] args)
    {
      var html = ReadTestFile();
      if (string.IsNullOrEmpty(html))
      {
        Console.WriteLine("The test HTML string is NULL or empty");
        return;
      }

      // var harlowe = new Harlowe();
    }

    private static string ReadTestFile()
    {
      // using (File f = )
      return null;
    }
  }
}