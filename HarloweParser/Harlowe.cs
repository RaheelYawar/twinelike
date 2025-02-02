using System;
using System.Collections.Generic;
using HtmlAgilityPack;

namespace Harlowe
{
  public class Harlowe
  {
    private const string HeaderPidKey = "pid";
    private const string HeaderNameKey = "name";

    private Dictionary<string, HarlowePassage> _passages;
    
    public int PassageCount => _passages.Count;

    public Harlowe(string htmlText)
    {
      Parse(htmlText);
    }

    public string GetFirstPassageName()
    {
      return "First";
    }

    public HarlowePassage GetPassage(string passageName)
    {
      return !_passages.ContainsKey(passageName) ? null : _passages[passageName];
    }
    
    public string GetPassageBody(string passageName)
    {
      if (!_passages.TryGetValue(passageName, out var passage)) return string.Empty;

      return passage.Body;
    }

    public List<HarloweBranch> GetPassageBranches(string passageName)
    {
      if (!_passages.TryGetValue(passageName, out var passage)) return null;

      return passage.Branches;
    }

    public void Parse(string htmlText)
    {
      _passages = new Dictionary<string, HarlowePassage>();
      
      var htmlDoc = new HtmlDocument();
      htmlDoc.LoadHtml(htmlText);
      var passagesData = htmlDoc.DocumentNode.SelectNodes("//tw-passagedata");

      foreach (var passageData in passagesData)
      {
        var body = passageData.InnerHtml;
        var branches = ParsePassageBody(ref body);
        
        var passage = new HarlowePassage
        {
          Body = body,
          Pid = passageData.Attributes[HeaderPidKey].Value,
          Name = passageData.Attributes[HeaderNameKey].Value,
          Branches = branches,
        };
        
        _passages.Add(passage.Name, passage);
      }
    }
    
    private List<HarloweBranch> ParsePassageBody(ref string body)
    {
      body = body.Replace("&#39;", "'");
      
      var branches = new List<HarloweBranch>();
      var tokens = body.Split(new[] { "[[" }, StringSplitOptions.None);
      body = tokens[0];

      for (var i = 1; i < tokens.Length; i++)
      {
        tokens[i] = tokens[i].Replace("]]", string.Empty);
        tokens[i] = tokens[i].Replace("\n", string.Empty);
        
        var branchTokens = tokens[i].Split(new[] { "-&gt;" }, StringSplitOptions.None);
        var branch = new HarloweBranch
        {
          Text = branchTokens[0],
          Name = branchTokens[1],
        };
        
        branches.Add(branch);
      }

      return branches;
    }
  }
}
