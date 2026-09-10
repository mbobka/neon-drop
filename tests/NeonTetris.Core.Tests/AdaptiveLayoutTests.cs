using NeonTetris.Core;
using Xunit;

namespace NeonTetris.Core.Tests;

public sealed class AdaptiveLayoutTests
{
    [Fact]
    public void NoFoldsReturnsTheWholeWindow()
    {
        Assert.Equal(new LayoutRect(0, 0, 1000, 800),
            Assert.Single(AdaptiveLayout.SafePanes(1000, 800, [])));
    }

    [Theory]
    [InlineData(20, 532, 468)]
    [InlineData(0, 512, 488)]
    public void VerticalFoldReservesItsThicknessAndTwelveDipOnEachSide(
        double thickness, double rightX, double rightWidth)
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [new(new(500, 0, thickness, 800), false)]);

        Assert.Equal<LayoutRect>([new(0, 0, 488, 800), new(rightX, 0, rightWidth, 800)], panes);
    }

    [Theory]
    [InlineData(20, 432, 368)]
    [InlineData(0, 412, 388)]
    public void HorizontalFoldReservesItsThicknessAndTwelveDipOnEachSide(
        double thickness, double bottomY, double bottomHeight)
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [new(new(0, 400, 1000, thickness), true)]);

        Assert.Equal<LayoutRect>([new(0, 0, 1000, 388), new(0, bottomY, 1000, bottomHeight)], panes);
    }

    [Fact]
    public void ParallelFoldsSplitTheRemainingPanels()
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [new(new(300, 0, 0, 800), false), new(new(700, 0, 20, 800), false)]);

        Assert.Equal<LayoutRect>(
            [new(0, 0, 288, 800), new(312, 0, 376, 800), new(732, 0, 268, 800)], panes);
    }

    [Fact]
    public void PerpendicularFoldsSplitEveryIntersectingPanel()
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [new(new(500, 0, 0, 800), false), new(new(0, 400, 1000, 0), true)]);

        Assert.Equal<LayoutRect>(
        [
            new(0, 0, 488, 388), new(0, 412, 488, 388),
            new(512, 0, 488, 388), new(512, 412, 488, 388)
        ], panes);
    }

    [Fact]
    public void PartialHorizontalFoldDoesNotSplitTheOtherPanel()
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [new(new(500, 0, 0, 800), false), new(new(0, 400, 488, 0), true)]);

        Assert.Equal<LayoutRect>(
            [new(0, 0, 488, 388), new(0, 412, 488, 388), new(512, 0, 488, 800)], panes);
    }

    [Fact]
    public void PartialVerticalFoldDoesNotSplitTheOtherPanel()
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [new(new(0, 400, 1000, 0), true), new(new(500, 0, 0, 388), false)]);

        Assert.Equal<LayoutRect>(
            [new(0, 0, 488, 388), new(512, 0, 488, 388), new(0, 412, 1000, 388)], panes);
    }

    [Theory]
    [InlineData(-5, 0, 0, 800, false)]
    [InlineData(1005, 0, 0, 800, false)]
    [InlineData(-10, 0, 10, 800, false)]
    [InlineData(1000, 0, 10, 800, false)]
    [InlineData(500, -100, 0, 100, false)]
    [InlineData(500, 800, 0, 100, false)]
    [InlineData(0, -5, 1000, 0, true)]
    [InlineData(0, 805, 1000, 0, true)]
    [InlineData(0, -10, 1000, 10, true)]
    [InlineData(0, 800, 1000, 10, true)]
    [InlineData(-100, 400, 100, 0, true)]
    [InlineData(1000, 400, 100, 0, true)]
    public void OutsideOrMerelyTouchingBoundsAreIgnoredBeforeAddingGap(
        double x, double y, double width, double height, bool horizontal)
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800, [new(new(x, y, width, height), horizontal)]);

        Assert.Equal(new LayoutRect(0, 0, 1000, 800), Assert.Single(panes));
    }

    [Theory]
    [InlineData(-10, 30, 32, 968)]
    [InlineData(990, 30, 0, 978)]
    [InlineData(5, 0, 17, 983)]
    [InlineData(995, 0, 0, 983)]
    [InlineData(0, 0, 12, 988)]
    [InlineData(1000, 0, 0, 988)]
    public void VerticalCutsAndGapAreClampedToWindowBounds(
        double x, double thickness, double expectedX, double expectedWidth)
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800, [new(new(x, -100, thickness, 1000), false)]);

        Assert.Equal(new LayoutRect(expectedX, 0, expectedWidth, 800), Assert.Single(panes));
    }

    [Theory]
    [InlineData(-10, 30, 32, 768)]
    [InlineData(790, 30, 0, 778)]
    [InlineData(5, 0, 17, 783)]
    [InlineData(795, 0, 0, 783)]
    [InlineData(0, 0, 12, 788)]
    [InlineData(800, 0, 0, 788)]
    public void HorizontalCutsAndGapAreClampedToWindowBounds(
        double y, double thickness, double expectedY, double expectedHeight)
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800, [new(new(-100, y, 1200, thickness), true)]);

        Assert.Equal(new LayoutRect(0, expectedY, 1000, expectedHeight), Assert.Single(panes));
    }

    [Fact]
    public void RepeatedFoldAndFoldInsideExistingGapLeavePanelsUnchanged()
    {
        var fold = new LayoutFold(new(500, 0, 0, 800), false);
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [fold, fold, new(new(505, 0, 0, 800), false)]);

        Assert.Equal<LayoutRect>([new(0, 0, 488, 800), new(512, 0, 488, 800)], panes);
    }

    [Fact]
    public void OverlappingHingesClampToTheCurrentPanel()
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
            [new(new(400, 0, 20, 800), false), new(new(410, 0, 40, 800), false)]);

        Assert.Equal<LayoutRect>([new(0, 0, 388, 800), new(462, 0, 538, 800)], panes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GapConsumingTheEntireWindowReturnsNoPanels(bool horizontal)
    {
        var bounds = horizontal ? new LayoutRect(0, 10, 20, 0) : new LayoutRect(10, 0, 0, 20);

        Assert.Empty(AdaptiveLayout.SafePanes(20, 20, [new(bounds, horizontal)]));
        Assert.Empty(AdaptiveLayout.SafePanes(20, 20, [new(new(-10, -10, 40, 40), horizontal)]));
    }

    [Theory]
    [InlineData(0, 800)]
    [InlineData(1000, 0)]
    [InlineData(-1, 800)]
    [InlineData(1000, -1)]
    [InlineData(double.NaN, 800)]
    [InlineData(1000, double.PositiveInfinity)]
    public void InvalidOrEmptyWindowReturnsNoPanels(double width, double height)
    {
        Assert.Empty(AdaptiveLayout.SafePanes(width, height, []));
    }

    [Theory]
    [InlineData(500, 400, 0, 0, false)]
    [InlineData(500, 400, 0, 0, true)]
    [InlineData(500, 400, 20, 0, false)]
    [InlineData(500, 400, 0, 20, true)]
    [InlineData(500, 0, -1, 800, false)]
    [InlineData(0, 400, 1000, -1, true)]
    [InlineData(double.NaN, 0, 0, 800, false)]
    [InlineData(0, 400, double.PositiveInfinity, 0, true)]
    public void InvalidBoundsOrZeroLengthFoldsAreIgnored(
        double x, double y, double width, double height, bool horizontal)
    {
        Assert.Equal(new LayoutRect(0, 0, 1000, 800), Assert.Single(
            AdaptiveLayout.SafePanes(1000, 800, [new(new(x, y, width, height), horizontal)])));
    }

    [Fact]
    public void ResultingPanelsHavePositiveDimensionsStayInsideWindowAndNeverOverlap()
    {
        var panes = AdaptiveLayout.SafePanes(1000, 800,
        [
            new(new(-5, -50, 15, 900), false),
            new(new(300, 0, 20, 800), false),
            new(new(700, 0, 0, 800), false),
            new(new(0, 400, 1000, 10), true),
            new(new(0, 790, 1000, 30), true)
        ]);

        Assert.Equal(6, panes.Count);
        Assert.All(panes, pane =>
        {
            Assert.True(pane.Width > 0 && pane.Height > 0);
            Assert.InRange(pane.X, 0, 1000);
            Assert.InRange(pane.Y, 0, 800);
            Assert.InRange(pane.X + pane.Width, 0, 1000);
            Assert.InRange(pane.Y + pane.Height, 0, 800);
        });

        for (var i = 0; i < panes.Count; i++)
        {
            for (var j = i + 1; j < panes.Count; j++)
            {
                var first = panes[i];
                var second = panes[j];
                Assert.True(first.X + first.Width <= second.X || second.X + second.Width <= first.X ||
                    first.Y + first.Height <= second.Y || second.Y + second.Height <= first.Y);
            }
        }
    }
}
