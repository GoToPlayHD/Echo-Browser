using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Strg+Umschalt+T: zuletzt geschlossene Tabs.</summary>
public class ClosedTabStackTests
{
    [Fact]
    public void LastClosedTab_ComesBackFirst()
    {
        var stack = new ClosedTabStack();
        stack.Push("https://a.example/", "A", 0);
        stack.Push("https://b.example/", "B", 3);

        Assert.True(stack.TryPop(out var tab));
        Assert.Equal(new ClosedTab("https://b.example/", "B", 3), tab);
        Assert.True(stack.TryPop(out tab));
        Assert.Equal("https://a.example/", tab!.Url);
        Assert.False(stack.TryPop(out _));
    }

    [Theory]
    [InlineData("echo://start")]
    [InlineData("echo://newtab")]
    [InlineData("about:blank")]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyStartPages_AreNotRemembered(string? url)
    {
        var stack = new ClosedTabStack();
        stack.Push(url, "Neuer Tab", 0);

        Assert.Equal(0, stack.Count);
    }

    [Fact]
    public void SettingsPage_CanBeReopened()
    {
        var stack = new ClosedTabStack();
        stack.Push("echo://settings", "Einstellungen", 1);

        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void OldestEntries_AreDroppedAtCapacity()
    {
        var stack = new ClosedTabStack();
        for (int i = 0; i < ClosedTabStack.Capacity + 5; i++)
        {
            stack.Push($"https://example.com/{i}", null, i);
        }

        Assert.Equal(ClosedTabStack.Capacity, stack.Count);
        Assert.True(stack.TryPop(out var newest));
        Assert.Equal($"https://example.com/{ClosedTabStack.Capacity + 4}", newest!.Url);
        Assert.Equal(newest.Url, newest.Title); // ohne Titel wird die Adresse angezeigt
    }

    [Fact]
    public void NegativeIndex_IsClampedToZero()
    {
        var stack = new ClosedTabStack();
        stack.Push("https://example.com/", "X", -3);

        Assert.True(stack.TryPop(out var tab));
        Assert.Equal(0, tab!.Index);
    }
}
