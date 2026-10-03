using EchoBrowser.Models;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Format von session.json – auch das alte Format muss nach einem Update noch gelesen werden.</summary>
public class SessionSerializerTests
{
    [Fact]
    public void RoundTrip_KeepsWindowsTabsAndActiveIndex()
    {
        var snapshot = new SessionSnapshot
        {
            Windows =
            {
                new SessionWindow
                {
                    ActiveIndex = 1,
                    Tabs =
                    {
                        new SessionTab { Url = "https://example.com/", Title = "Example" },
                        new SessionTab { Url = "https://github.com/", Title = "GitHub" }
                    }
                },
                new SessionWindow { Tabs = { new SessionTab { Url = "https://wikipedia.org/" } } }
            }
        };

        var restored = SessionSerializer.Deserialize(SessionSerializer.Serialize(snapshot));

        Assert.Equal(2, restored.Windows.Count);
        Assert.Equal(1, restored.Windows[0].ActiveIndex);
        Assert.Equal("GitHub", restored.Windows[0].Tabs[1].Title);
        Assert.Equal("https://wikipedia.org/", restored.Windows[1].Tabs[0].Url);
    }

    [Fact]
    public void LegacyFormat_ListOfUrls_BecomesOneWindow()
    {
        var restored = SessionSerializer.Deserialize("[\"https://example.com/\", \"https://github.com/\"]");

        var window = Assert.Single(restored.Windows);
        Assert.Equal(new[] { "https://example.com/", "https://github.com/" }, window.Tabs.Select(t => t.Url));
    }

    [Fact]
    public void InternalAndBlankPages_AreNotRestored()
    {
        var restored = SessionSerializer.Deserialize(
            "[\"echo://start\", \"about:blank\", \"data:text/html,x\", \"\", \"https://example.com/\"]");

        Assert.Equal("https://example.com/", Assert.Single(Assert.Single(restored.Windows).Tabs).Url);
    }

    [Fact]
    public void WindowsWithoutTabs_AreDropped_AndActiveIndexIsClamped()
    {
        const string json = """
        {
          "Version": 2,
          "Windows": [
            { "Tabs": [ { "Url": "echo://settings" } ], "ActiveIndex": 0 },
            { "Tabs": [ { "Url": "https://example.com/" } ], "ActiveIndex": 7 }
          ]
        }
        """;

        var window = Assert.Single(SessionSerializer.Deserialize(json).Windows);
        Assert.Equal(0, window.ActiveIndex);
    }

    [Fact]
    public void EmptyLegacyList_GivesEmptySession()
    {
        Assert.Empty(SessionSerializer.Deserialize("[]").Windows);
    }

    [Fact]
    public void RoundTrip_KeepsPinnedTabsAndGroups()
    {
        var snapshot = new SessionSnapshot();
        snapshot.Windows.Add(new SessionWindow
        {
            Tabs =
            {
                new SessionTab { Url = "https://mail.example.com/", IsPinned = true },
                new SessionTab { Url = "https://a.example.com/", GroupId = "g1" },
                new SessionTab { Url = "https://b.example.com/", GroupId = "g1" },
            },
            Groups = { new SessionGroup { Id = "g1", Name = "Arbeit", Color = "Green", IsCollapsed = true } }
        });

        var window = Assert.Single(SessionSerializer.Deserialize(SessionSerializer.Serialize(snapshot)).Windows);

        Assert.True(window.Tabs[0].IsPinned);
        Assert.Equal(new[] { null, "g1", "g1" }, window.Tabs.Select(t => t.GroupId));
        var group = Assert.Single(window.Groups);
        Assert.Equal(("Arbeit", "Green", true), (group.Name, group.Color, group.IsCollapsed));
    }

    [Fact]
    public void SessionWithoutGroups_FromOlderVersion_StillLoads()
    {
        const string json = """{ "Version": 2, "Windows": [ { "Tabs": [ { "Url": "https://example.com/" } ], "ActiveIndex": 0 } ] }""";

        var window = Assert.Single(SessionSerializer.Deserialize(json).Windows);
        Assert.Empty(window.Groups);
        Assert.False(window.Tabs[0].IsPinned);
    }
}
