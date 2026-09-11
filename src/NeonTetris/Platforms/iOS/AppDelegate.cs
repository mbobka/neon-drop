using Foundation;
using ObjCRuntime;
using UIKit;

namespace NeonTetris;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    // Значения констант UIKeyInput*: совпадают с UIKeyCommand.Input, который приходит обратно.
    private const string LeftArrow = "UIKeyInputLeftArrow";
    private const string RightArrow = "UIKeyInputRightArrow";
    private const string UpArrow = "UIKeyInputUpArrow";
    private const string DownArrow = "UIKeyInputDownArrow";
    private const string Escape = "UIKeyInputEscape";

    // Те же команды, что DispatchKeyEvent на Android: аппаратная клавиатура iPad и симулятор.
    private static readonly (string Input, string Command)[] Bindings =
    [
        (LeftArrow, "left"),
        (RightArrow, "right"),
        (DownArrow, "down"),
        (UpArrow, "rotate"),
        ("x", "rotate"),
        ("z", "counterrotate"),
        (" ", "drop"),
        ("p", "pause"),
        (Escape, "pause"),
        ("\r", "start"),
    ];

    private UIKeyCommand[]? keyCommands;

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // AppDelegate — последнее звено цепочки респондеров, сюда доходят необработанные нажатия.
    public override UIKeyCommand[] KeyCommands => keyCommands ??= BuildKeyCommands();

    [Export("handleGameKey:")]
    public void HandleGameKey(UIKeyCommand command)
    {
        var input = command.Input;
        if (string.IsNullOrEmpty(input)) return;
        if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page is not MainPage page) return;
        foreach (var (key, name) in Bindings)
        {
            if (!string.Equals(key, input, StringComparison.OrdinalIgnoreCase)) continue;
            page.HandleKey(name);
            return;
        }
    }

    private static UIKeyCommand[] BuildKeyCommands()
    {
        var action = new Selector("handleGameKey:");
        var commands = new UIKeyCommand[Bindings.Length];
        for (var i = 0; i < Bindings.Length; i++)
        {
            var command = UIKeyCommand.Create(new NSString(Bindings[i].Input), 0, action);
            // Иначе пробел и стрелки перехватывает системная навигация по элементам.
            command.WantsPriorityOverSystemBehavior = true;
            commands[i] = command;
        }
        return commands;
    }
}
