using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using NeonTetris.Core;
using NeonTetris.Services;

namespace NeonTetris;

public sealed class MainPage : ContentPage
{
    private static readonly Color Ink = Color.FromArgb("#F2F3FF");
    private static readonly Color Muted = Color.FromArgb("#98A4C4");
    private static readonly Color Mint = Color.FromArgb("#7CE8C8");
    private readonly GameEngine game;
    private readonly AbsoluteLayout scene = new();
    private readonly GraphicsView board;
    private readonly BoardDrawable boardArt;
    private readonly GraphicsView next;
    private readonly GraphicsView held;
    private readonly Border boardFrame;
    private readonly Grid header;
    private readonly Grid stats;
    private readonly VerticalStackLayout sidebar;
    private readonly ScrollView sidebarScroll;
    private readonly Grid controls;
    private readonly Label score = Text("0", 27, Ink, true);
    private readonly Label best = Text("0", 18, Ink, true);
    private readonly Label level = Text("01", 18, Mint, true);
    private readonly Label lines = Text("0 / 10", 13, Muted);
    private readonly Label status = Text("МАРАФОН  /  В СВОЁМ РИТМЕ", 10, Muted);
    private readonly Button pause;
    private readonly Border overlay;
    private readonly Label overlayTitle = Text("Поймай свой ритм", 25, Ink, true);
    private readonly Label overlayText = Text("Собирай линии. Освобождай место.\nПусть всё встанет на свои места.", 13, Muted);
    private readonly Image hero = new() { Source = "hero.png", Aspect = Aspect.AspectFill, HeightRequest = 140 };
    private readonly Button play;
    private readonly Button restart;
    private readonly IDispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private TimeSpan previous;
    private Action? repeat;
    private double repeatWait;
    private int highScore;
    private int lastLines;
    private double lastWidth;
    private double lastHeight;
    private bool haptics = true;
    private bool helpOpen;
    private bool spaceLimited;
    private double saveElapsed;
    private PointF touchStart;

    public MainPage(GameEngine game)
    {
        this.game = game;
        BackgroundColor = Color.FromArgb("#090E20");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        NavigationPage.SetHasNavigationBar(this, false);
        highScore = Preferences.Default.Get("best", 0);
        haptics = Preferences.Default.Get("haptics", true);
        var saved = Preferences.Default.Get("session", "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            try
            {
                game.RestoreSnapshot(saved);
                if (game.IsStarted && !game.IsPaused && !game.IsGameOver) game.TogglePause();
            }
            catch (ArgumentException) { Preferences.Default.Remove("session"); }
        }
        boardArt = new BoardDrawable(game);
        board = new GraphicsView { Drawable = boardArt };
        SemanticProperties.SetDescription(board, "Игровое поле, 10 столбцов и 20 строк. Управление кнопками под полем.");
        boardFrame = Card(board, 12);
        next = new GraphicsView { Drawable = new PreviewDrawable(game, false), HeightRequest = 160 };
        held = new GraphicsView { Drawable = new PreviewDrawable(game, true), HeightRequest = 48 };
        pause = Button("Ⅱ", PauseOrResume, "Пауза или продолжение");
        pause.WidthRequest = 48;
        var title = new VerticalStackLayout { Spacing = 0, Children = { Text("NEON DROP", 23, Ink, true), status } };
        header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        header.Add(title);
        header.Add(pause, 1);
        stats = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        stats.Add(Stat("СЧЁТ", score));
        stats.Add(Stat("РЕКОРД", best), 1);
        stats.Add(Stat("УРОВЕНЬ", level), 2);
        var reserve = Button("В резерв", () => Act(game.Hold), "Сохранить фигуру или обменять резерв");
        reserve.FontSize = 11;
        reserve.Padding = new Thickness(2);
        var sound = Button(haptics ? "Вибро: вкл" : "Вибро: выкл", ToggleHaptics, "Переключить виброотклик");
        sound.FontSize = 10;
        sound.Padding = new Thickness(2);
        hapticsButton = sound;
        sidebar = new VerticalStackLayout { Spacing = 10, Children = {
            Text("ДАЛЬШЕ", 10, Muted, true), next,
            Text("РЕЗЕРВ", 10, Muted, true), held, reserve,
            Text("ЛИНИИ", 10, Muted, true), lines, sound
        }};
        sidebarScroll = new ScrollView { Content = sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        controls = new Grid {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(new GridLength(54)), new(new GridLength(48))],
            ColumnSpacing = 8, RowSpacing = 8
        };
        controls.Add(RepeatButton("←", () => game.Move(-1), "Сдвинуть влево"), 0);
        controls.Add(Button("↻", () => Act(() => game.Rotate()), "Повернуть по часовой стрелке"), 1);
        controls.Add(RepeatButton("→", () => game.Move(1), "Сдвинуть вправо"), 2);
        controls.Add(RepeatButton("↓", game.SoftDrop, "Ускорить падение"), 3);
        var drop = Button("СБРОСИТЬ  ↓↓", () => Act(game.HardDrop), "Мгновенно опустить фигуру", true);
        drop.FontSize = 13;
        controls.Add(drop, 0, 1);
        Grid.SetColumnSpan(drop, 3);
        controls.Add(Button("?", OpenHelp, "Как играть"), 3, 1);
        play = Button("ИГРАТЬ   →", Play, "Начать игру", true);
        restart = Button("Новая игра", () => { game.Start(); helpOpen = false; ArrangeScene(); }, "Начать новую партию");
        var overlayContent = new VerticalStackLayout { Spacing = 14, Padding = 20,
            Children = { hero, Text("N E O N   D R O P", 11, Mint, true), overlayTitle, overlayText, play, restart } };
        overlay = Card(new ScrollView { Content = overlayContent }, 24);
        overlay.BackgroundColor = Color.FromArgb("#131B32");
        overlay.ZIndex = 10;
        foreach (var view in new View[] { header, stats, boardFrame, sidebarScroll, controls, overlay }) scene.Add(view);
        Content = new Grid { Children = { new Image { Source = "aurora.png", Aspect = Aspect.AspectFill, Opacity = .48, InputTransparent = true }, scene } };
        scene.SizeChanged += (_, _) => ArrangeScene();
        board.StartInteraction += (_, e) => { if (e.Touches.Length > 0) touchStart = e.Touches[0]; };
        board.EndInteraction += (_, e) =>
        {
            if (e.Touches.Length == 0) return;
            var delta = e.Touches[0] - touchStart;
            if (Math.Abs(delta.Width) < 15 && Math.Abs(delta.Height) < 15) Act(() => game.Rotate());
            else if (Math.Abs(delta.Width) > Math.Abs(delta.Height)) Act(() => game.Move(Math.Sign(delta.Width)));
            else if (delta.Height > 30) Act(game.SoftDrop);
        };
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) => UpdateGame();
        Refresh();
    }

    private readonly Button hapticsButton;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        FoldState.Changed += OnFoldChanged;
        ArrangeScene();
        clock.Restart();
        previous = TimeSpan.Zero;
        timer.Start();
    }

    protected override void OnDisappearing()
    {
        Suspend();
        FoldState.Changed -= OnFoldChanged;
        timer.Stop();
        base.OnDisappearing();
    }

    public void Suspend()
    {
        repeat = null;
        if (game.IsStarted && !game.IsPaused && !game.IsGameOver) game.TogglePause();
        Preferences.Default.Set("session", game.SerializeSnapshot());
        Refresh();
    }

    private void OnFoldChanged(object? sender, EventArgs args)
    {
        Suspend();
        ArrangeScene();
    }

    public void HandleKey(string key)
    {
        if (key == "pause") { PauseOrResume(); return; }
        if (key == "start") { Play(); return; }
        Act(key switch
        {
            "left" => () => game.Move(-1), "right" => () => game.Move(1),
            "down" => game.SoftDrop, "drop" => game.HardDrop,
            "rotate" => () => game.Rotate(), "counterrotate" => () => game.Rotate(-1),
            "hold" => game.Hold, _ => () => { }
        });
    }

    protected override bool OnBackButtonPressed()
    {
        if (game.IsStarted && !game.IsPaused && !game.IsGameOver) { Suspend(); return true; }
        return base.OnBackButtonPressed();
    }

    private void UpdateGame()
    {
        var now = clock.Elapsed;
        var elapsed = now - previous;
        previous = now;
        if (elapsed.TotalMilliseconds > 150) elapsed = TimeSpan.FromMilliseconds(150);
        if (repeat is not null && !game.IsPaused)
        {
            repeatWait -= elapsed.TotalMilliseconds;
            if (repeatWait <= 0) { repeat(); repeatWait = 75; }
        }
        game.Tick(elapsed);
        saveElapsed += elapsed.TotalSeconds;
        if (saveElapsed >= 2 && game.IsStarted)
        {
            Preferences.Default.Set("session", game.SerializeSnapshot());
            saveElapsed = 0;
        }
        boardArt.Flash = Math.Max(0, boardArt.Flash - elapsed.TotalSeconds * 3);
        if (game.Lines > lastLines) { boardArt.Flash = 1; Pulse(); }
        lastLines = game.Lines;
        Refresh();
    }

    private void Act(Action action)
    {
        if (!game.IsStarted || game.IsPaused || game.IsGameOver || spaceLimited || helpOpen) return;
        action();
        Pulse();
        Refresh();
    }

    private void Pulse()
    {
        if (!haptics) return;
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch (FeatureNotSupportedException) { }
    }

    private void ToggleHaptics()
    {
        haptics = !haptics;
        Preferences.Default.Set("haptics", haptics);
        hapticsButton.Text = haptics ? "Вибро: вкл" : "Вибро: выкл";
    }

    private void Play()
    {
        if (spaceLimited) return;
        helpOpen = false;
        if (!game.IsStarted || game.IsGameOver) game.Start();
        else if (game.IsPaused) game.TogglePause();
        previous = clock.Elapsed;
        ArrangeScene();
        Refresh();
    }

    private void PauseOrResume()
    {
        if (!game.IsStarted || game.IsGameOver) return;
        if (game.IsPaused) Play();
        else Suspend();
    }

    private void OpenHelp()
    {
        Suspend();
        helpOpen = true;
        ArrangeScene();
    }

    private void Refresh()
    {
        if (game.Score > highScore) { highScore = game.Score; Preferences.Default.Set("best", highScore); }
        score.Text = game.Score.ToString("N0");
        best.Text = highScore.ToString("N0");
        level.Text = game.Level.ToString("D2");
        lines.Text = $"{game.Lines} / {(game.Lines / 10 + 1) * 10}";
        pause.IsEnabled = game.IsStarted && !game.IsGameOver;
        pause.Text = game.IsPaused ? "▷" : "Ⅱ";
        overlay.IsVisible = !game.IsStarted || game.IsPaused || game.IsGameOver || helpOpen || spaceLimited;
        hero.IsVisible = !game.IsStarted && !helpOpen;
        restart.IsVisible = game.IsStarted && !game.IsGameOver && !helpOpen;
        overlayTitle.Text = helpOpen ? "Как играть" : game.IsGameOver ? "Ещё одну партию?" : game.IsPaused ? "Можно выдохнуть" : "Поймай свой ритм";
        overlayText.Text = helpOpen
            ? "← → — движение, ↻ — поворот.\n↓ — мягкое падение. СБРОСИТЬ — мгновенное.\nРезерв меняет фигуру один раз за ход.\nПолная линия исчезает. Каждые 10 линий — новый уровень.\nКонтур показывает место приземления."
            : game.IsGameOver ? $"Счёт: {game.Score:N0} · Линий: {game.Lines}\nТвой следующий рекорд уже близко."
            : game.IsPaused ? "Партия на паузе.\nПродолжай, когда будешь готов."
            : "Собирай линии. Освобождай место.\nПусть всё встанет на свои места.";
        play.Text = game.IsStarted && !game.IsGameOver ? "ПРОДОЛЖИТЬ   →" : "ИГРАТЬ   →";
        play.IsEnabled = !spaceLimited;
        if (spaceLimited)
        {
            hero.IsVisible = false;
            restart.IsVisible = false;
            overlayTitle.Text = "Нужно чуть больше места";
            overlayText.Text = "Разверни или раскрой устройство,\nлибо увеличь окно. Партия сохранена.";
        }
        board.Invalidate(); next.Invalidate(); held.Invalidate();
    }

    private void ArrangeScene()
    {
        var w = scene.Width;
        var h = scene.Height;
        if (w <= 0 || h <= 0) return;
        if (lastWidth > 0 && (Math.Abs(lastWidth - w) > 2 || Math.Abs(lastHeight - h) > 2)) Suspend();
        lastWidth = w; lastHeight = h;
        var pad = w >= 600 ? 28d : 16d;
        var wide = (w > h && w >= 480) || w >= 700;
        var panes = AdaptiveLayout.SafePanes(w, h, LocalFolds());
        var modalPane = new LayoutRect(0, 0, w, h);
        spaceLimited = w < 280 || h < 320;
        if (panes.Count > 1)
        {
            var boardPane = panes.OrderByDescending(p => Math.Min(p.Width - 24, (p.Height - 24) / 2)).First();
            var auxPane = panes.Where(p => !ReferenceEquals(p, boardPane)).OrderByDescending(p => p.Width * p.Height).First();
            var bh = Math.Min(boardPane.Height - 24, (boardPane.Width - 24) * 2);
            Place(boardFrame, boardPane.X + (boardPane.Width - bh / 2) / 2, boardPane.Y + (boardPane.Height - bh) / 2, bh / 2, bh);
            ArrangeAuxiliary(auxPane);
            modalPane = boardPane;
            spaceLimited = bh < 240 || auxPane.Width < 240 || auxPane.Height < 300;
            status.Text = boardPane.Y != auxPane.Y ? "МАРАФОН  /  TABLETOP" : "МАРАФОН  /  BOOK";
        }
        else if (panes.Count == 1 && (panes[0].Width < w || panes[0].Height < h))
        {
            modalPane = panes[0];
            spaceLimited = true;
            foreach (var view in new View[] { header, stats, boardFrame, sidebarScroll, controls }) Place(view, 0, 0, 1, 1);
        }
        else if (panes.Count == 0) spaceLimited = true;
        else if (wide)
        {
            var bh = Math.Max(120, Math.Min(h - 32, (w * .49 - pad * 2) * 2));
            var bw = bh / 2;
            var left = Math.Max(pad, (w * .49 - bw) / 2);
            Place(boardFrame, left, (h - bh) / 2, bw + 2, bh + 2);
            var sx = Math.Max(left + bw + 24, w * .51);
            ArrangeAuxiliary(new LayoutRect(sx, 0, w - sx - 12, h));
            status.Text = "МАРАФОН  /  В СВОЁМ РИТМЕ";
        }
        else
        {
            Place(header, pad, 10, w - pad * 2, 54);
            Place(stats, pad, 74, w - pad * 2, 70);
            var sideWidth = w >= 600 ? 140d : 82d;
            var bh = Math.Max(120, Math.Min(h - 294, (w - pad * 2 - sideWidth - 16) * 2));
            var bw = bh / 2;
            var left = (w - bw - sideWidth - 16) / 2;
            Place(boardFrame, left, 155, bw + 2, bh + 2);
            next.HeightRequest = Math.Clamp(bh * .31, 72, 170);
            Place(sidebarScroll, left + bw + 16, 156, sideWidth, bh);
            Place(controls, Math.Max(pad, (w - 520) / 2), h - 122, Math.Min(w - pad * 2, 520), 110);
            status.Text = "МАРАФОН  /  В СВОЁМ РИТМЕ";
        }
        var ow = Math.Min(modalPane.Width - 24, 350);
        var oh = Math.Min(modalPane.Height - 24, helpOpen ? 430 : game.IsStarted ? 300 : 382);
        Place(overlay, modalPane.X + (modalPane.Width - ow) / 2, modalPane.Y + (modalPane.Height - oh) / 2, ow, oh);
        if (spaceLimited && game.IsStarted && !game.IsPaused && !game.IsGameOver) game.TogglePause();
        Refresh();
    }

    private void ArrangeAuxiliary(LayoutRect pane)
    {
        var x = pane.X + 12;
        var width = Math.Max(1, pane.Width - 24);
        Place(header, x, pane.Y + 10, width, 54);
        Place(stats, x, pane.Y + 74, width, 70);
        next.HeightRequest = 90;
        Place(sidebarScroll, x, pane.Y + 154, width, Math.Max(1, pane.Height - 284));
        Place(controls, x, pane.Y + pane.Height - 120, width, 110);
    }

    private IReadOnlyList<LayoutFold> LocalFolds()
    {
        double offsetX = 0, offsetY = 0;
#if ANDROID
        if (scene.Handler?.PlatformView is Android.Views.View native &&
            Platform.CurrentActivity?.FindViewById<Android.Views.View>(Android.Resource.Id.Content) is { } content)
        {
            int[] sceneOrigin = new int[2], contentOrigin = new int[2];
            native.GetLocationInWindow(sceneOrigin);
            content.GetLocationInWindow(contentOrigin);
            var density = native.Resources?.DisplayMetrics?.Density ?? 1;
            offsetX = (sceneOrigin[0] - contentOrigin[0]) / density;
            offsetY = (sceneOrigin[1] - contentOrigin[1]) / density;
        }
#endif
        return FoldState.Regions.Select(f => new LayoutFold(
            new LayoutRect(f.Bounds.X - offsetX, f.Bounds.Y - offsetY, f.Bounds.Width, f.Bounds.Height), f.IsHorizontal)).ToArray();
    }

    private static void Place(View view, double x, double y, double width, double height)
        => AbsoluteLayout.SetLayoutBounds(view, new Rect(x, y, Math.Max(1, width), Math.Max(1, height)));

    private Button RepeatButton(string text, Action action, string description)
    {
        var button = Button(text, () => { }, description);
        button.Pressed += (_, _) => { Act(action); repeat = action; repeatWait = 280; };
        button.Released += (_, _) => repeat = null;
        button.Unfocused += (_, _) => repeat = null;
        return button;
    }

    private static Button Button(string text, Action action, string description, bool primary = false)
    {
        var button = new Button { Text = text, FontFamily = "OpenSansSemibold", FontSize = 23,
            BackgroundColor = primary ? Mint : Color.FromArgb("#202C47"), TextColor = primary ? Color.FromArgb("#102A2A") : Ink,
            CornerRadius = 14, MinimumHeightRequest = 48, Padding = new Thickness(8, 2), BorderWidth = primary ? 0 : 1,
            BorderColor = Color.FromArgb("#354261") };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += (_, _) => action();
        return button;
    }

    private static Label Text(string text, double size, Color color, bool bold = false)
        => new() { Text = text, FontSize = size, TextColor = color, FontFamily = bold ? "OpenSansSemibold" : "OpenSansRegular" };

    private static Border Stat(string title, Label value)
        => Card(new VerticalStackLayout { Spacing = 2, Padding = new Thickness(12, 8), Children = { Text(title, 9, Muted, true), value } }, 14);

    private static Border Card(View content, float radius)
        => new() { Content = content, BackgroundColor = Color.FromArgb("#121B31"),
            Stroke = Color.FromArgb("#303D5C"), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = radius }, Padding = 0 };
}
