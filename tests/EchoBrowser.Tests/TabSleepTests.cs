using System.Globalization;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Tab-Schlaf: wann schlafen, wann verwerfen; Speicher pro Tab und Anzeige.</summary>
public class TabSleepTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0);

    private static TabSleepState Idle(double minutes, bool visible = false, bool audible = false, bool loading = false,
        bool sleeping = false, bool discarded = false, bool hasWebView = true, bool refused = false) =>
        new(visible, hasWebView, sleeping, discarded, audible, loading, refused, Now.AddMinutes(-minutes));

    [Fact]
    public void InactiveTab_SleepsAfterConfiguredTime()
    {
        Assert.Equal(TabSleepAction.None, TabSleepPolicy.Decide(Idle(29), Now, 30));
        Assert.Equal(TabSleepAction.Sleep, TabSleepPolicy.Decide(Idle(30), Now, 30));
    }

    [Fact]
    public void Never_MeansNever()
    {
        Assert.Equal(TabSleepAction.None, TabSleepPolicy.Decide(Idle(10_000), Now, 0));
    }

    [Theory]
    [InlineData(true, false, false)]   // sichtbar (aktiv oder geteilte Ansicht)
    [InlineData(false, true, false)]   // spielt Ton
    [InlineData(false, false, true)]   // lädt noch
    public void BusyOrVisibleTabs_StayAwake(bool visible, bool audible, bool loading)
    {
        Assert.Equal(TabSleepAction.None, TabSleepPolicy.Decide(Idle(500, visible, audible, loading), Now, 5));
    }

    [Fact]
    public void SleepingTab_IsNotSuspendedAgain_ButDiscardedLater()
    {
        Assert.Equal(TabSleepAction.None, TabSleepPolicy.Decide(Idle(60, sleeping: true), Now, 30));
        Assert.Equal(TabSleepAction.Discard, TabSleepPolicy.Decide(Idle(120, sleeping: true), Now, 30));
    }

    [Fact]
    public void RefusedTab_IsNotRetriedEveryMinute()
    {
        Assert.Equal(TabSleepAction.None, TabSleepPolicy.Decide(Idle(45, refused: true), Now, 30));
    }

    [Theory]
    [InlineData(5, 120)]
    [InlineData(30, 120)]
    [InlineData(60, 240)]
    [InlineData(120, 480)]
    public void DiscardAfter_IsFourTimesSleep_AtLeastTwoHours(int sleep, int discardMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(discardMinutes), TabSleepPolicy.DiscardAfter(sleep));
    }

    [Fact]
    public void DiscardedOrUnloadedTabs_AreLeftAlone()
    {
        Assert.Equal(TabSleepAction.None, TabSleepPolicy.Decide(Idle(1000, discarded: true, hasWebView: false), Now, 5));
        Assert.Equal(TabSleepAction.None, TabSleepPolicy.Decide(Idle(1000, hasWebView: false), Now, 5));
    }

    [Fact]
    public void SleepNow_SkipsVisibleAudibleAndAlreadySleepingTabs()
    {
        Assert.True(TabSleepPolicy.CanSleepNow(Idle(0)));
        Assert.False(TabSleepPolicy.CanSleepNow(Idle(0, visible: true)));
        Assert.False(TabSleepPolicy.CanSleepNow(Idle(0, audible: true)));
        Assert.False(TabSleepPolicy.CanSleepNow(Idle(0, sleeping: true)));
    }

    [Fact]
    public void Memory_IsSplitBetweenTabsSharingAProcess()
    {
        var tabs = new Dictionary<uint, string> { [1] = "a", [2] = "b", [3] = "c" };
        var processes = new[]
        {
            new ProcessMemory(100, 300, new uint[] { 1, 2 }),     // zwei Tabs derselben Website
            new ProcessMemory(101, 50, new uint[] { 3, 3 }),      // Haupt- und iframe desselben Tabs
            new ProcessMemory(102, 40, new uint[] { 1 }),         // iframe einer anderen Website in Tab a
            new ProcessMemory(103, 999, new uint[] { 42 }),       // unbekannter Frame (z.B. Erweiterung)
        };

        var memory = TabMemory.Attribute(processes, tabs);

        Assert.Equal(190, memory["a"]);
        Assert.Equal(150, memory["b"]);
        Assert.Equal(50, memory["c"]);
        Assert.Equal(3, memory.Count);
    }

    [Theory]
    [InlineData(0L, "0 MB")]
    [InlineData(1000L, "1 MB")]
    [InlineData(85L * 1024 * 1024, "85 MB")]
    [InlineData(1536L * 1024 * 1024, "1.5 GB")]
    public void MemoryFormat_IsReadable(long bytes, string expected)
    {
        Assert.Equal(expected, MemoryFormat.Format(bytes, CultureInfo.InvariantCulture));
    }
}
