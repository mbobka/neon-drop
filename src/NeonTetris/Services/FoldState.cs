using Microsoft.Maui.Graphics;

namespace NeonTetris.Services;

// DIP относительно android.R.id.content текущей Activity.
// MainPage самостоятельно вычитает смещение своей scene, включая safe area.
public sealed record FoldRegion(Rect Bounds, bool IsHorizontal);

public static class FoldState
{
    public static IReadOnlyList<FoldRegion> Current { get; private set; } = Array.Empty<FoldRegion>();

    public static IReadOnlyList<FoldRegion> Regions => Current;

    // Снимок обновляется и событие вызывается на UI-потоке.
    public static event EventHandler? Changed;

    internal static void Update(IEnumerable<FoldRegion> regions)
    {
        var snapshot = regions.ToArray();
        if (Current.SequenceEqual(snapshot))
        {
            return;
        }

        Current = Array.AsReadOnly(snapshot);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
