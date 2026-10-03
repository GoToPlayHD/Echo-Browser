using EchoBrowser.Views.Controls;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Tab-Breite wie in Chrome: gleichmäßig verteilt, mit Unter- und Obergrenze.</summary>
public class TabStripPanelTests
{
    [Theory]
    [InlineData(1200, 2, 240)]   // wenige Tabs: volle Breite
    [InlineData(1200, 8, 150)]   // Platz wird gleichmäßig geteilt
    [InlineData(1200, 7, 171)]   // ganze Pixel (abgerundet)
    [InlineData(1200, 50, 40)]   // sehr viele Tabs: Mindestbreite (nur Symbol)
    [InlineData(1200, 0, 240)]
    public void TabWidth_IsSharedWithinLimits(double available, int count, double expected)
    {
        Assert.Equal(expected, TabStripPanel.TabWidthFor(available, count, min: 40, max: 240));
    }

    [Theory]
    [InlineData(400, 40, 20, 19, 0, 400)]   // letzter Tab aktiv: ganz nach rechts (800 - 400)
    [InlineData(400, 40, 20, 0, 400, 0)]    // erster Tab aktiv: zurück an den Anfang
    [InlineData(400, 40, 20, 12, 200, 200)] // aktiver Tab schon sichtbar: nichts verschieben
    [InlineData(400, 40, 5, 4, 0, 0)]       // alles passt: keine Verschiebung
    [InlineData(400, 40, 20, -1, 999, 400)] // ohne aktiven Tab nur auf den gültigen Bereich begrenzen
    public void ScrollOffset_KeepsActiveTabVisible(double viewport, double width, int count, int active, double current, double expected)
    {
        Assert.Equal(expected, TabStripPanel.ScrollOffsetFor(viewport, width, count, active, current));
    }

    [Fact]
    public void UnlimitedSpace_UsesMaximumWidth()
    {
        Assert.Equal(240, TabStripPanel.TabWidthFor(double.PositiveInfinity, 5, 40, 240));
    }
}
