namespace NeonTetris.Core;

public sealed record LayoutRect(double X, double Y, double Width, double Height);

public sealed record LayoutFold(LayoutRect Bounds, bool IsHorizontal);

public static class AdaptiveLayout
{
    private const double HingeGap = 12;

    // Сгибы применяются по порядку. Каждый разрез проходит через всю текущую панель.
    // От каждой стороны шарнира отступаем на 12 DIP; пустые панели не возвращаем.
    public static IReadOnlyList<LayoutRect> SafePanes(double width, double height,
        IReadOnlyList<LayoutFold> folds)
    {
        ArgumentNullException.ThrowIfNull(folds);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return [];
        }

        var window = new LayoutRect(0, 0, width, height);
        List<LayoutRect> panes = [window];
        foreach (var fold in folds)
        {
            var bounds = fold.Bounds;
            if (!IsValid(bounds) || !Intersects(window, fold))
            {
                continue;
            }

            List<LayoutRect> next = [];
            foreach (var pane in panes)
            {
                // Сначала проверяем сам сгиб, чтобы внешний сгиб не затронул панель своим отступом.
                if (!Intersects(pane, fold))
                {
                    next.Add(pane);
                    continue;
                }

                var start = fold.IsHorizontal ? pane.Y : pane.X;
                var end = start + (fold.IsHorizontal ? pane.Height : pane.Width);
                var foldStart = fold.IsHorizontal ? bounds.Y : bounds.X;
                var foldEnd = foldStart + (fold.IsHorizontal ? bounds.Height : bounds.Width);
                var beforeEnd = Math.Clamp(foldStart - HingeGap, start, end);
                var afterStart = Math.Clamp(foldEnd + HingeGap, start, end);

                if (beforeEnd > start)
                {
                    next.Add(fold.IsHorizontal
                        ? new(pane.X, start, pane.Width, beforeEnd - start)
                        : new(start, pane.Y, beforeEnd - start, pane.Height));
                }

                if (afterStart < end)
                {
                    next.Add(fold.IsHorizontal
                        ? new(pane.X, afterStart, pane.Width, end - afterStart)
                        : new(afterStart, pane.Y, end - afterStart, pane.Height));
                }
            }

            panes = next;
        }

        return panes.ToArray();
    }

    private static bool IsValid(LayoutRect bounds) =>
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
        double.IsFinite(bounds.X + bounds.Width) && double.IsFinite(bounds.Y + bounds.Height) &&
        bounds.Width >= 0 && bounds.Height >= 0;

    private static bool Intersects(LayoutRect pane, LayoutFold fold)
    {
        var bounds = fold.Bounds;
        // Нулевая толщина допустима поперёк сгиба, но его длина должна быть положительной.
        return fold.IsHorizontal
            ? bounds.Width > 0 && bounds.X < pane.X + pane.Width && bounds.X + bounds.Width > pane.X &&
                IntersectsAxis(bounds.Y, bounds.Height, pane.Y, pane.Height)
            : bounds.Height > 0 && bounds.Y < pane.Y + pane.Height && bounds.Y + bounds.Height > pane.Y &&
                IntersectsAxis(bounds.X, bounds.Width, pane.X, pane.Width);
    }

    private static bool IntersectsAxis(double start, double size, double paneStart, double paneSize) =>
        size == 0
            ? start >= paneStart && start <= paneStart + paneSize
            : start < paneStart + paneSize && start + size > paneStart;
}
