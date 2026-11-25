using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary><c>(dm-names:)</c>, <c>(dm-values:)</c> and <c>(dm-entries:)</c>. Expected text was rendered by the real 3.3.9 build.</summary>
  public class DatamapListMacroTests
  {
    private static RenderResult Render(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
      story.StartNode = story.GetPassage("test").Pid;
      return new StorySession(story).Render();
    }

    private static string Shown(string body)
    {
      var text = new System.Text.StringBuilder();
      foreach (var entry in Render(body).Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.Text) text.Append(entry.Content);
        else if (entry.Kind == BufferedRenderOutput.Kind.Error) text.Append("!");
      }
      return text.ToString();
    }

    private const string Entries = "(altered: _e via (str: _e's name) + '=' + (str: _e's value), ...";

    [Theory]
    [InlineData("(print: (dm-names: (dm: 'B','Y', 'A','X')))", "A,B")]
    [InlineData("(print: (dm-values: (dm: 'B',24, 'A',25)))", "25,24")]
    [InlineData("(print: " + Entries + "(dm-entries: (dm: 'B',24, 'A',25))))", "A=25,B=24")]
    [InlineData("(print: (dm-names: (dm-entries: (dm: 'a', 1))'s 1st))|(print: (dm-values: (dm-entries: (dm: 'a', 1))'s 1st))", "name,value|a,1")]
    [InlineData("(print: (dm-entries: (dm: 'a', 1)) is (a: (dm: 'value', 1, 'name', 'a')))", "true")]
    [InlineData("(print: (dm-names: (dm: 'b', 1, 'a', 2, 'c', 3)) is (a: 'a', 'b', 'c'))", "true")]
    [InlineData("[(print: (dm-names: (dm:)))]|(print: (dm-names: (dm:)) is (a:))|(print: (dm-values: (dm:)) is (a:))|(print: (dm-entries: (dm:)) is (a:))", "|true|true|true")]
    [InlineData("(print: (a: ...(dm-names: (dm: 'b',1,'a',2)), 'c'))|(print: (dm-names: (dm: 'b',1,'a',2))'s length)|(print: (dm-names: (dm: 'b',1,'a',2)) contains 'a')", "a,b,c|2|true")]
    [InlineData("(print: (dm-names: (passage:)))", "name,source,tags")]
    public void ListsADatamap(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Theory]
    [InlineData("(print: (data-names: (dm: 'b',1,'a',2)))|(print: (datamap-names: (dm: 'b',1,'a',2)))|(print: (datanames: (dm: 'b',1,'a',2)))|(print: (DM_NAMES: (dm: 'b',1,'a',2)))", "a,b|a,b|a,b|a,b")]
    [InlineData("(print: (data-values: (dm: 'b',1,'a',2)))|(print: (datamap-values: (dm: 'b',1,'a',2)))|(print: (datavalues: (dm: 'b',1,'a',2)))", "2,1|2,1|2,1")]
    [InlineData("(print: (dm-entries: (dm: 'b',1,'a',2))'s length)|(print: (data-entries: (dm: 'b',1,'a',2))'s 1st's name)|(print: (datamap-entries: (dm: 'b',1,'a',2))'s 2nd's value)|(print: (dataentries: (dm: 'b',1,'a',2))'s 1st's value)", "2|a|1|2")]
    public void AnswersToEveryAlias(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Theory]
    [InlineData("(print: (dm-values: (dm: 'item10',1,'item2',2,'Item1',3,'apple',4,'_x',5,'10',6,'9',7,'-3',8,'1.5',9,'',10)))", "8,10,9,7,6,5,4,2,1,3")]
    [InlineData("(print: (dm-names: (dm: 'b',1,'B',2,'a',3,'A',4,'ab',5,'Ab',6,'aB',7)))", "a,A,ab,aB,Ab,b,B")]
    [InlineData("(print: (dm-names: (dm: 'Level 10',1,'Level 9',2,'level 9',3,'Level 9a',4,'Level',5)))", "Level,level 9,Level 9,Level 9a,Level 10")]
    [InlineData("(print: (dm-names: (dm: '10',1,'9',2,'010',3,'09',4,'1e3',5,'2d',6)))", "010,09,2d,9,10,1e3")]
    [InlineData("(print: (dm-names: (dm: 'b2',1,'b10',2,'b02',3,'b1.5',4,'b-1',5)))", "b02,b1.5,b2,b10,b-1")]
    [InlineData("(print: (dm-names: (dm: 2,'b', 10,'a', 1,'c', -4,'d', 0.5,'e')))", "-4,0.5,1,2,10")]
    [InlineData("(print: (dm-values: (dm: 2,'b', 10,'a', 1,'c', -4,'d', 0.5,'e')))", "d,e,c,b,a")]
    [InlineData("(print: " + Entries + "(dm-entries: (dm: 2,'b', 10,'a', 1,'c', -4,'d', 0.5,'e'))))", "-4=d,0.5=e,1=c,2=b,10=a")]
    [InlineData("(print: (dm-names: (dm: 2,'x', 'a','y', '10b','z', 'b',1, 11,'w')))", "2,10b,11,a,b")]
    [InlineData("(print: " + Entries + "(dm-entries: (dm: 2,'x', 'a','y', '10b','z', 'b',1, 11,'w'))))", "2=x,10b=z,11=w,a=y,b=1")]
    public void SortsByNameInNaturalOrder(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Fact]
    public void ANumberNameStaysANumber()
      => Assert.Equal("3|!", Shown("(print: (dm-names: (dm: 2,'x'))'s 1st + 1)|(print: (dm-names: (dm: '2','x'))'s 1st + 1)"));

    [Theory]
    [InlineData("(print: (dm-names: (dm: '1.0',1, '1',2, '1.',3)))|(print: (dm-values: (dm: '1.0',1, '1',2, '1.',3)))|(print: " + Entries + "(dm-entries: (dm: '1.0',1, '1',2, '1.',3))))", "1.0,1,1.|1,2,3|1.=3,1=2,1.0=1")]
    [InlineData("(print: (dm-names: (dm: 'x',1, '1',2, '1.0',3, 'y',4, '1.00',5)))|(print: " + Entries + "(dm-entries: (dm: 'x',1, '1',2, '1.0',3, 'y',4, '1.00',5))))", "1,1.0,1.00,x,y|1.00=5,1.0=3,1=2,x=1,y=4")]
    [InlineData("(print: (dm-values: (dm: 'b',1, ' a',2, 'a',3, 'a ',4)))|(print: " + Entries + "(dm-entries: (dm: 'b',1, ' a',2, 'a',3, 'a ',4))))", "2,3,4,1|a =4,a=3, a=2,b=1")]
    [InlineData("(set: $m to (dm: 'x',1, '1',2))(move: $m's x into $y)(set: $m's ('1.0') to 3)(print: (dm-names: $m))|(print: " + Entries + "(dm-entries: $m)))", "1,1.0|1.0=3,1=2")]
    [InlineData("(set: $m to (dm: 'x',1, '1',2, 'y',3))(set: $n to $m)(move: $n's x into $y)(set: $n's ('1.0') to 4)(print: (dm-names: $n))|(print: (dm-names: $m))|(print: (dm-values: $n))", "1,1.0,y|1,x,y|2,4,3")]
    public void EqualNamesKeepTheirOrderExceptInEntries(string source, string expected)
    {
      // Names that sort as equal keep the datamap's order, which a deleted
      // name doesn't disturb. (dm-entries:)'s comparator never returns 0, so
      // they come out reversed there.
      Assert.Equal(expected, Shown(source));
    }

    [Theory]
    [InlineData("(set: $m to (dm: 'a', (a: 1,2)))(set: $v to (dm-values: $m))(set: $v's 1st's 1st to 9)(print: $m's a)|(print: $v's 1st)", "1,2|9,2")]
    [InlineData("(set: $m to (dm: 'a', (dm: 'x', 1)))(set: $e to (dm-entries: $m))(set: $e's 1st's value's x to 9)(print: $m's a's x)|(print: $e's 1st's value's x)", "1|9")]
    [InlineData("(set: $m to (dm: 'b',1,'a',2))(set: $m's a to 5)(print: (dm-values: $m))|(print: (dm-entries: $m)'s 1st's value)", "5,1|5")]
    public void ChangingTheListLeavesTheDatamapAlone(string source, string expected) => Assert.Equal(expected, Shown(source));

    [Fact]
    public void TheDemoEffectivenessCheckWorks()
    {
      // harlowe-demo looks a move's type up this way before every damage step.
      Assert.Equal("ABD", Shown("(set: $types to (dm: 'fire', (dm: 'grass', 2, 'water', 0.5), 'water', (dm: 'fire', 2)))(set: _t to 'fire')"
        + "(if: (dm-names: $types) contains _t)[A](if: (dm-names: $types's _t) contains 'water')[B](if: (dm-names: $types's _t) contains 'rock')[C](else:)[D]"));
    }

    [Theory]
    [InlineData("(dm-names: 'x')", "The (dm-names:) macro's 1st value is the string \"x\", but should be a datamap.")]
    [InlineData("(dm-entries: 2)", "The (dm-entries:) macro's 1st value is the number 2, but should be a datamap.")]
    [InlineData("(dm-names: $nothing)", "The (dm-names:) macro's 1st value is the number 0, but should be a datamap.")]
    [InlineData("(dm-names: (a: 1))", null)]
    [InlineData("(dm-values: (ds: 1))", null)]
    [InlineData("(dm-names:)", null)]
    [InlineData("(data-values:)", null)]
    [InlineData("(datamap-entries: (dm:), 1)", null)]
    [InlineData("(dm-names: ?a)", null)]
    [InlineData("(dm-names: (dm: 'a', 1) + 1)", null)]
    public void RefusesAnythingButOneDatamap(string source, string message)
    {
      var error = Render(source).Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(error);
      if (message != null) Assert.Equal(message, error.Content);
    }

    [Fact]
    public void AccentedNamesSortAfterZ()
    {
      // Real 3.3.9's collator gives e,E,é,f,z. NaturalOrder covers ASCII
      // exactly and puts other letters after z.
      Assert.Equal("e,E,f,z,é", Shown("(print: (dm-names: (dm: 'f',1,'e',2,'é',3,'E',4,'z',5)))"));
    }
  }
}
