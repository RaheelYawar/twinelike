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

    /// <summary>
    /// Parses a full Harlowe-format Twine HTML export. Extracts story metadata
    /// from <c>&lt;tw-storydata&gt;</c> and one <see cref="HarlowePassage"/>
    /// per <c>&lt;tw-passagedata&gt;</c> element. Throws if the input is not a
    /// recognisable Harlowe document.
    /// </summary>
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
    
    /// <summary>
    /// Pulls story-level attributes off the <c>&lt;tw-storydata&gt;</c> node:
    /// name, start-passage pid, creator tool, and creator version. Missing
    /// attributes default to empty/zero rather than throwing.
    /// </summary>
    private void ParseStoryData(ref HtmlNode storyNode)
    {
      _storyName = storyNode.GetAttributeValue("name", "");
      StartNode = storyNode.GetAttributeValue("startnode", "0");
      _creator = storyNode.GetAttributeValue("creator", "");
      _creatorVersion = storyNode.GetAttributeValue("creator-version", "");
    }

    /// <summary>
    /// Looks up a passage by its author-facing name. Returns null if no such
    /// passage exists — does not throw.
    /// </summary>
    public HarlowePassage GetPassage(string passageName)
    {
      return !_passages.ContainsKey(passageName) ? null : _passages[passageName];
    }

    /// <summary>
    /// Returns the body text of the named passage with branch links stripped.
    /// Returns <see cref="string.Empty"/> for unknown names.
    /// </summary>
    public string GetPassageBody(string passageName)
    {
      if (!_passages.TryGetValue(passageName, out var passage)) return string.Empty;

      return passage.Body;
    }

    /// <summary>
    /// Returns the outgoing branch links for the named passage. Returns null
    /// for unknown names; returns an empty list for known passages with no
    /// links.
    /// </summary>
    public List<Branch> GetPassageBranches(string passageName)
    {
      if (!_passages.TryGetValue(passageName, out var passage)) return null;

      return passage.Branches;
    }

    /// <summary>
    /// Walks every <c>&lt;tw-passagedata&gt;</c> node, builds a
    /// <see cref="HarlowePassage"/> for each, and indexes them by name. Body
    /// text and branch lists are populated by <see cref="ParseBody"/> and
    /// <see cref="ParseBranches"/> respectively. Tag parsing is currently
    /// stubbed.
    /// </summary>
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

    /// <summary>
    /// Layer-2 body normalisation. Currently only decodes the HTML apostrophe
    /// entity; broader entity handling and the tokenizer/AST pipeline will
    /// take over once the body parser lands.
    /// </summary>
    private string ParseBody(string body)
    {
      return body.Replace("&#39;", "'");
    }

    /// <summary>
    /// Splits Harlowe <c>[[display-&gt;target]]</c> link syntax out of the
    /// passage body. Mutates <paramref name="body"/> in place to remove the
    /// link markup and returns the extracted <see cref="Branch"/>es. This is
    /// the legacy string-level shortcut; it will be replaced by the
    /// tokenizer-driven AST walker.
    /// </summary>
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
