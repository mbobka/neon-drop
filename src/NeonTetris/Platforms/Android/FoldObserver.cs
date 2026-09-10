using Android.App;
using Android.Runtime;
using Android.Views;
using AndroidX.Core.Content;
using AndroidX.Core.Util;
using AndroidX.Window.Java.Layout;
using AndroidX.Window.Layout;
using NeonTetris.Services;
using Rect = Microsoft.Maui.Graphics.Rect;
using View = Android.Views.View;

namespace NeonTetris;

// API сверены с Microsoft bindings Window/WindowJava 1.4.0.1 (net10.0-android36.0).
internal sealed class FoldObserver(Activity activity) : Java.Lang.Object, IConsumer
{
    private readonly Activity _activity = activity;
    private readonly int[] _contentOrigin = new int[2];
    private WindowInfoTrackerCallbackAdapter? _tracker;
    private View? _contentView;
    private ViewTreeObserver? _viewTreeObserver;
    private IReadOnlyList<FoldRegion> _windowRegions = Array.Empty<FoldRegion>();
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        var executor = ContextCompat.GetMainExecutor(_activity);
        if (executor is null)
        {
            // Без UI-executor не создаём частичную подписку и не оставляем старую геометрию.
            FoldState.Update(Array.Empty<FoldRegion>());
            return;
        }

        _contentView = _activity.FindViewById<View>(Android.Resource.Id.Content);
        _viewTreeObserver = _contentView?.ViewTreeObserver;
        _tracker = new WindowInfoTrackerCallbackAdapter(WindowInfoTracker.GetOrCreate(_activity));
        _started = true;

        if (_viewTreeObserver is { IsAlive: true })
        {
            _viewTreeObserver.GlobalLayout += OnGlobalLayout;
        }

        // Этот же consumer удаляется в Stop; executor доставляет изменения на UI-поток.
        _tracker.AddWindowLayoutInfoListener(_activity, executor, this);
        PublishRegions();
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        // Отложенные callbacks старой Activity больше не должны менять общий снимок.
        _started = false;
        _tracker?.RemoveWindowLayoutInfoListener(this);

        if (_viewTreeObserver is { IsAlive: true })
        {
            _viewTreeObserver.GlobalLayout -= OnGlobalLayout;
        }

        _tracker?.Dispose();
        _tracker = null;
        _viewTreeObserver = null;
        _contentView = null;
        _windowRegions = Array.Empty<FoldRegion>();
        FoldState.Update(Array.Empty<FoldRegion>());
    }

    public void Accept(Java.Lang.Object? value)
    {
        if (!_started || value is not WindowLayoutInfo layoutInfo)
        {
            return;
        }

        var regions = new List<FoldRegion>();
#if DEBUG
        Android.Util.Log.Debug("NeonDrop", $"Window layout: {layoutInfo}");
#endif
        using var foldingType = Java.Lang.Class.FromType(typeof(IFoldingFeature));
        foreach (var feature in layoutInfo.DisplayFeatures)
        {
            // IList<IDisplayFeature> может вернуть invoker базового интерфейса.
            // Проверяем Java-тип и получаем folding-интерфейс через JNI.
            if (!foldingType.IsInstance(feature.JavaCast<Java.Lang.Object>())) continue;
            var fold = feature.JavaCast<IFoldingFeature>()!;
            if (!(fold.IsSeparating ||
                  FoldingFeatureState.HalfOpened.Equals(fold.State) ||
                  FoldingFeatureOcclusionType.Full.Equals(fold.OcclusionType)))
            {
                continue;
            }

            // Храним только копию чисел, а не Java-объекты события.
            var bounds = fold.Bounds;
            regions.Add(new FoldRegion(
                new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
                FoldingFeatureOrientation.Horizontal.Equals(fold.Orientation)));
        }

        _windowRegions = regions;
#if DEBUG
        Android.Util.Log.Debug("NeonDrop", $"Separating folds: {regions.Count}");
#endif
        PublishRegions();
    }

    private void OnGlobalLayout(object? sender, EventArgs args) => PublishRegions();

    private void PublishRegions()
    {
        if (!_started)
        {
            return;
        }

        var contentView = _contentView;
        if (contentView is null)
        {
            FoldState.Update(Array.Empty<FoldRegion>());
            return;
        }

        var density = _activity.Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0 || contentView.Width <= 0 || contentView.Height <= 0)
        {
            FoldState.Update(Array.Empty<FoldRegion>());
            return;
        }

        // Координаты относительно android.R.id.content. Смещение до scene, включая
        // safe area страницы, учитывает MainPage; здесь его повторно не вычитаем.
        contentView.GetLocationInWindow(_contentOrigin);
        var regions = new List<FoldRegion>(_windowRegions.Count);
        foreach (var region in _windowRegions)
        {
            var left = Math.Max(0, region.Bounds.Left - _contentOrigin[0]);
            var top = Math.Max(0, region.Bounds.Top - _contentOrigin[1]);
            var right = Math.Min(contentView.Width, region.Bounds.Right - _contentOrigin[0]);
            var bottom = Math.Min(contentView.Height, region.Bounds.Bottom - _contentOrigin[1]);

            // Сгиб бывает линией: Rect.IsEmpty/пересечение по площади отбросили бы его.
            if (right < left || bottom < top ||
                (region.IsHorizontal ? right <= left : bottom <= top))
            {
                continue;
            }

            regions.Add(new FoldRegion(
                new Rect(left / density, top / density, (right - left) / density, (bottom - top) / density),
                region.IsHorizontal));
        }

        FoldState.Update(regions);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
        }

        base.Dispose(disposing);
    }
}
