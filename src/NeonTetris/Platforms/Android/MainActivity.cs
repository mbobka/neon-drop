using Android.App;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Views;
using AndroidX.Core.View;

namespace NeonTetris;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private FoldObserver? _foldObserver;

    protected override void OnStart()
    {
        base.OnStart();
        _foldObserver ??= new FoldObserver(this);
        _foldObserver.Start();
    }

    protected override void OnResume()
    {
        base.OnResume();

        if (Window is not { } window)
        {
            return;
        }

        var background = Android.Graphics.Color.Rgb(11, 16, 38);
        window.SetBackgroundDrawable(new ColorDrawable(background));

        // На Android 15+ панели прозрачны при edge-to-edge и показывают фон приложения.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            window.SetStatusBarColor(background);
            window.SetNavigationBarColor(background);
        }

        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller is null)
        {
            return;
        }

        // В C# bindings свойства называются без префикса Is; false означает светлые значки.
        controller.AppearanceLightStatusBars = false;
        controller.AppearanceLightNavigationBars = false;
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is null) return base.DispatchKeyEvent(e);
        var keyCode = e.KeyCode;
        var command = keyCode switch
        {
            Keycode.DpadLeft => "left",
            Keycode.DpadRight => "right",
            Keycode.DpadDown => "down",
            Keycode.Space => "drop",
            Keycode.DpadUp or Keycode.X => "rotate",
            Keycode.Z => "counterrotate",
            Keycode.P or Keycode.Escape => "pause",
            Keycode.Enter => "start",
            _ => null
        };

        if (command is null ||
            Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page is not MainPage page)
        {
            return base.DispatchKeyEvent(e);
        }

        var canRepeat = keyCode is Keycode.DpadLeft or Keycode.DpadRight or Keycode.DpadDown;
        if (e.Action == KeyEventActions.Down && (canRepeat || e.RepeatCount == 0))
        {
#if DEBUG
            Android.Util.Log.Debug("NeonDrop", $"Key: {command}");
#endif
            page.HandleKey(command);
        }

        // Подавленный повтор тоже поглощаем, чтобы он не ушёл в навигацию Android.
        return true;
    }

    protected override void OnStop()
    {
        try
        {
            _foldObserver?.Dispose();
            _foldObserver = null;
        }
        finally
        {
            base.OnStop();
        }
    }
}
