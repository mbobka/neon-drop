namespace NeonTetris;

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(page);
        window.Stopped += (_, _) => page.Suspend();
        window.Deactivated += (_, _) => page.Suspend();
        return window;
    }
}
