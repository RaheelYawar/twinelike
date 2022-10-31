using System;
using System.Collections.Generic;
using HtmlAgilityPack;

namespace Harlowe
{
  public class Harlowe
  {
    private const string HEADER_PID_KEY = "pid";
    private const string HEADER_NAME_KEY = "name";

    private Dictionary<string, HarlowePassage> _passages;

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
      if (!_passages.ContainsKey(passageName)) return string.Empty;

      return _passages[passageName].Body;
    }

    public List<HarloweBranch> GetPassageBranches(string passageName)
    {
      if (!_passages.ContainsKey(passageName)) return null;

      return _passages[passageName].Branches;
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
        var branches = ParseBody(ref body);
        
        var passage = new HarlowePassage
        {
          Body = body,
          Pid = passageData.Attributes[HEADER_PID_KEY].Value,
          Name = passageData.Attributes[HEADER_NAME_KEY].Value,
          Branches = branches,
        };
        
        _passages.Add(passage.Name, passage);
      }
    }
    
    private List<HarloweBranch> ParseBody(ref string body)
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
