using System.Collections.ObjectModel;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Reihenfolge der Tabs (angeheftet, Gruppen) und Abgleich der Tab-Leiste.</summary>
public class TabOrderTests
{
    private sealed class G
    {
        public G(string name) => Name = name;
        public string Name { get; }
        public override string ToString() => Name;
    }

    private sealed record T(string Name, G? Group = null, bool Pinned = false)
    {
        public override string ToString() => Name;
    }

    private static string Order(IEnumerable<T> tabs) => string.Join(",", tabs.Select(t => t.Name));

    private static List<T> Normalize(params T[] tabs) => TabOrder.Normalize(tabs, t => t.Group, t => t.Pinned);

    [Fact]
    public void PinnedTabs_MoveToTheFront_KeepingTheirOrder()
    {
        var result = Normalize(new T("a"), new T("p1", Pinned: true), new T("b"), new T("p2", Pinned: true));
        Assert.Equal("p1,p2,a,b", Order(result));
    }

    [Fact]
    public void Group_StaysTogether_AtItsFirstTab()
    {
        var g = new G("g");
        var result = Normalize(new T("a", g), new T("x"), new T("b", g), new T("y"));
        Assert.Equal("a,b,x,y", Order(result));
    }

    [Fact]
    public void TabRemovedFromMiddleOfGroup_EndsUpBehindTheGroup()
    {
        var g = new G("g");
        var result = Normalize(new T("a", g), new T("b"), new T("c", g));
        Assert.Equal("a,c,b", Order(result));
    }

    [Fact]
    public void AlreadyNormalOrder_IsUnchanged()
    {
        var g = new G("g");
        var h = new G("h");
        var tabs = new[] { new T("p", Pinned: true), new T("a", g), new T("b", g), new T("x"), new T("c", h) };
        Assert.Equal(Order(tabs), Order(Normalize(tabs)));
    }

    [Fact]
    public void DroppedBetweenTwoGroupMembers_JoinsTheGroup()
    {
        var g = new G("g");
        Assert.Same(g, TabOrder.GroupAfterMove(g, g, null));
    }

    [Fact]
    public void DroppedAwayFromItsGroup_LeavesIt()
    {
        var g = new G("g");
        var h = new G("h");
        Assert.Null(TabOrder.GroupAfterMove<G>(null, null, g));
        Assert.Null(TabOrder.GroupAfterMove(h, null, g));
    }

    [Fact]
    public void DroppedAtTheEdgeOfItsGroup_StaysInIt()
    {
        var g = new G("g");
        Assert.Same(g, TabOrder.GroupAfterMove(g, null, g));
        Assert.Same(g, TabOrder.GroupAfterMove(null, g, g));
    }

    [Fact]
    public void DroppedAtTheEdgeOfAnotherGroup_DoesNotJoinIt()
    {
        var g = new G("g");
        Assert.Null(TabOrder.GroupAfterMove<G>(g, null, null));
    }

    [Fact]
    public void Compose_PutsOneHeaderBeforeEachGroup()
    {
        var g = new G("G");
        var h = new G("H");
        var items = TabOrder.Compose(new[] { new T("x"), new T("a", g), new T("b", g), new T("c", h) }, t => t.Group);
        Assert.Equal("x,G,a,b,H,c", string.Join(",", items));
    }

    [Fact]
    public void Compose_NeverRepeatsAHeader_EvenIfAGroupIsSplitDuringDrag()
    {
        var g = new G("G");
        var items = TabOrder.Compose(new[] { new T("a", g), new T("x"), new T("b", g) }, t => t.Group);
        Assert.Equal("G,a,x,b", string.Join(",", items));
    }

    [Theory]
    [InlineData("a,b,c", "a,b,c")]
    [InlineData("a,b,c", "c,a,b")]
    [InlineData("a,b,c", "a,c")]
    [InlineData("a,b,c", "x,a,b,c,y")]
    [InlineData("a,b,c", "b,x,a")]
    [InlineData("", "a,b")]
    [InlineData("a,b", "")]
    public void ListSync_ReachesTheDesiredOrder(string start, string desired)
    {
        var target = new ObservableCollection<string>(start.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var wanted = desired.Split(',', StringSplitOptions.RemoveEmptyEntries);

        ListSync.Apply(target, wanted);

        Assert.Equal(wanted, target);
    }

    [Fact]
    public void ListSync_MovesInsteadOfRecreating()
    {
        var target = new ObservableCollection<string>(new[] { "a", "b", "c" });
        var actions = new List<string>();
        target.CollectionChanged += (s, e) => actions.Add(e.Action.ToString());

        ListSync.Apply(target, new[] { "c", "a", "b" });

        Assert.Equal(new[] { "Move" }, actions);
    }
}
