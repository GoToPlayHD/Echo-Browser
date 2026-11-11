using System.Windows;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Tabs ziehen: Schwelle, Leistenbereich, Einfügeposition und Lage des neuen Fensters (MainWindow.TabDrag.cs).</summary>
public class TabDragMathTests
{
    [Theory]
    [InlineData(0, 0, false)]     // nur geklickt
    [InlineData(4, 4, false)]     // Zittern unter der Schwelle
    [InlineData(5, 0, true)]      // waagerecht: Umsortieren
    [InlineData(0, -5, true)]     // senkrecht: Herauslösen
    [InlineData(-7, 7, true)]     // schräg
    public void Threshold_SeparatesClickFromDrag(double dx, double dy, bool expected)
    {
        Assert.Equal(expected, TabDragMath.ExceedsThreshold(new Vector(dx, dy), 4, 4));
    }

    [Theory]
    [InlineData(100, 20, true)]    // mitten in der Leiste
    [InlineData(100, -30, true)]   // knapp darüber: noch Toleranz
    [InlineData(100, 70, true)]    // knapp darunter (Leiste 40 hoch + 36)
    [InlineData(100, 80, false)]   // weit darunter: herauslösen
    [InlineData(-1, 20, false)]    // links daneben (längs keine Toleranz)
    [InlineData(1001, 20, false)]
    public void HorizontalStrip_HasToleranceAcrossOnly(double x, double y, bool expected)
    {
        Assert.Equal(expected, TabDragMath.IsWithinStrip(new Point(x, y), new Size(1000, 40), vertical: false, margin: 36));
    }

    [Theory]
    [InlineData(260, 300, true)]   // rechts neben der senkrechten Leiste, innerhalb der Toleranz
    [InlineData(300, 300, false)]
    [InlineData(100, -1, false)]
    public void VerticalStrip_HasToleranceSideways(double x, double y, bool expected)
    {
        Assert.Equal(expected, TabDragMath.IsWithinStrip(new Point(x, y), new Size(240, 800), vertical: true, margin: 36));
    }

    [Fact]
    public void HiddenStrip_NeverMatches()
    {
        Assert.False(TabDragMath.IsWithinStrip(new Point(0, 0), new Size(0, 0), vertical: false, margin: 36));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(150, 1)]
    [InlineData(250, 2)]
    [InlineData(999, 3)]
    public void InsertionIndex_CountsCentersBeforeCursor(double position, int expected)
    {
        Assert.Equal(expected, TabDragMath.InsertionIndex(new[] { 100.0, 200, 300 }, position));
    }

    [Fact]
    public void InsertionIndex_EmptyStripIsZero()
    {
        Assert.Equal(0, TabDragMath.InsertionIndex(System.Array.Empty<double>(), 500));
    }

    [Theory]
    [InlineData(0, false, 2)]  // normaler Tab nie vor die angehefteten
    [InlineData(4, false, 4)]
    [InlineData(9, false, 5)]  // höchstens ans Ende
    [InlineData(4, true, 2)]   // angehefteter Tab bleibt bei den angehefteten
    [InlineData(1, true, 1)]
    public void PinnedTabs_StaySeparate(int index, bool isPinned, int expected)
    {
        Assert.Equal(expected, TabDragMath.ClampToPinnedRange(index, pinnedCount: 2, count: 5, isPinned));
    }

    [Fact]
    public void NewWindow_PutsGrabPointUnderCursor()
    {
        var rect = TabDragMath.PlaceWindow(new Point(800, 400), new Vector(120, 20), new Size(1200, 800),
            new Rect(0, 0, 2560, 1400), minVisible: 96);
        Assert.Equal(new Rect(680, 380, 1200, 800), rect);
    }

    [Fact]
    public void NewWindow_NeverAboveTheWorkArea()
    {
        // Losgelassen ganz oben am Bildschirmrand: Titelzeile bleibt erreichbar
        var rect = TabDragMath.PlaceWindow(new Point(500, 2), new Vector(100, 20), new Size(1000, 700),
            new Rect(0, 0, 1920, 1040), minVisible: 96);
        Assert.Equal(0, rect.Top);
    }

    [Fact]
    public void NewWindow_KeepsAPartVisibleAtTheEdges()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var right = TabDragMath.PlaceWindow(new Point(1919, 1039), new Vector(0, 0), new Size(1000, 700), work, minVisible: 96);
        Assert.Equal(1920 - 96, right.Left);
        Assert.Equal(1040 - 96, right.Top);

        var left = TabDragMath.PlaceWindow(new Point(0, 300), new Vector(950, 20), new Size(1000, 700), work, minVisible: 96);
        Assert.Equal(96 - 1000, left.Left);
    }

    [Fact]
    public void NewWindow_OnSecondMonitorWithNegativeCoordinates()
    {
        // Monitor links vom Hauptmonitor (negative Koordinaten), 200 % Skalierung: Werte schon in Pixeln
        var work = new Rect(-3840, 0, 3840, 2100);
        var rect = TabDragMath.PlaceWindow(new Point(-2000, 600), new Vector(240, 40), new Size(2400, 1600), work, minVisible: 192);
        Assert.Equal(new Rect(-2240, 560, 2400, 1600), rect);
    }

    [Fact]
    public void NewWindow_LargerThanMonitor_IsShrunkToFit()
    {
        var rect = TabDragMath.PlaceWindow(new Point(100, 100), new Vector(0, 0), new Size(3000, 2000),
            new Rect(0, 0, 1366, 728), minVisible: 96);
        Assert.Equal(1366, rect.Width);
        Assert.Equal(728, rect.Height);
    }
}
