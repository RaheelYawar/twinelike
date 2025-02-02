using System;
using System.Collections.Generic;
using HtmlAgilityPack;

namespace Harlowe
{
  public class Harlowe
  {
    private string _storyName;
    private string _creator;
    private string _creatorVersion;
    private Dictionary<string, HarlowePassage> _passages;
    
    public string StartNode { get; private set; }

    public int PassageCount => _passages.Count;

    public Harlowe(string htmlText)
    {
      var htmlDoc = new HtmlDocument();
      htmlDoc.LoadHtml(htmlText);

      HtmlNode storyNode = htmlDoc.DocumentNode.SelectSingleNode("//tw-storydata");
      if (storyNode == null)
      {
        throw new Exception("Invalid Harlowe HTML file: <tw-storydata> not found."); 
      }
      
      ParseStoryData(ref storyNode);
      Parse(storyNode.SelectNodes("//tw-passagedata"));
    }
    
    private void ParseStoryData(ref HtmlNode storyNode)
    {
      _storyName = storyNode.GetAttributeValue("name", "");
      StartNode = storyNode.GetAttributeValue("startnode", "0");
      _creator = storyNode.GetAttributeValue("creator", "");
      _creatorVersion = storyNode.GetAttributeValue("creator-version", "");
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

    public List<Branch> GetPassageBranches(string passageName)
    {
      if (!_passages.TryGetValue(passageName, out var passage)) return null;

      return passage.Branches;
    }

    private void Parse(HtmlNodeCollection passageNodes)
    {
      _passages = new Dictionary<string, HarlowePassage>();
      foreach (var passageNode in passageNodes)
      {
        var body = ParseBody(passageNode.InnerHtml);
        var branches = ParseBranches(ref body);
        
        var passage = new HarlowePassage
        {
          Body = body,
          Pid = passageNode.Attributes["pid"].Value,
          Name = passageNode.Attributes["name"].Value,
          Tags = null,  // TODO: Parse tags
          Branches = branches,
        };
        
        _passages.Add(passage.Name, passage);
      }
    }

    private string ParseBody(string body)
    {
      return body.Replace("&#39;", "'");
    }
    
    private List<Branch> ParseBranches(ref string body)
    {
      var branches = new List<Branch>();
      var tokens = body.Split(new[] { "[[" }, StringSplitOptions.None);
      body = tokens[0];

      for (var i = 1; i < tokens.Length; i++)
      {
        tokens[i] = tokens[i].Replace("]]", string.Empty);
        tokens[i] = tokens[i].Replace("\n", string.Empty);
        
        var branchTokens = tokens[i].Split(new[] { "-&gt;" }, StringSplitOptions.None);
        var branch = new Branch
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
