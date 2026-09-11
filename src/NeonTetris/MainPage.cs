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
    private const double Gap = 8;
    private readonly GameEngine game;
    private readonly AbsoluteLayout scene = new();
    private readonly GraphicsView board;
    private readonly BoardDrawable boardArt;
    private readonly GraphicsView next;
    private readonly Border boardFrame;
    private readonly Grid header;
    private readonly Grid stats;
    private readonly VerticalStackLayout sidebar;
    private readonly ScrollView sidebarScroll;
    private readonly Button moveLeft;
    private readonly Button moveRight;
    private readonly Button softDrop;
    private readonly Button rotate;
    private readonly Button hardDrop;
    private readonly Button helpButton;
    private readonly View[] controls;
    private readonly Label score = Text("0", 27, Ink, true);
    private readonly Label best = Text("0", 18, Ink, true);
    private readonly Label level = Text("01", 18, Mint, true);
    private readonly Label lines = Text("0 / 10", 13, Muted);
    private readonly Label status = Text("МАРАФОН  /  В СВОЁМ РИТМЕ", 10, Muted);
    private readonly Label title = Text("GAME OF BLOCKS", 23, Ink, true);
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
    private bool rightHanded = true;
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
        rightHanded = Preferences.Default.Get("righthanded", true);
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
        SemanticProperties.SetDescription(board, "Игровое поле, 10 столбцов и 20 строк. Управление крестовиной снизу.");
        boardFrame = Card(board, 12);
        next = new GraphicsView { Drawable = new PreviewDrawable(game), HeightRequest = 160 };
        pause = Button("Ⅱ", PauseOrResume, "Пауза или продолжение");
        pause.WidthRequest = 48;
        title.LineBreakMode = LineBreakMode.NoWrap;
        var titleBlock = new VerticalStackLayout { Spacing = 0, Children = { title, status } };
        header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        header.Add(titleBlock);
        header.Add(pause, 1);
        stats = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        stats.Add(Stat("СЧЁТ", score));
        stats.Add(Stat("РЕКОРД", best), 1);
        stats.Add(Stat("УРОВЕНЬ", level), 2);
        var sound = Button(haptics ? "Вибро: вкл" : "Вибро: выкл", ToggleHaptics, "Переключить виброотклик");
        sound.FontSize = 10;
        sound.Padding = new Thickness(2);
        hapticsButton = sound;
        var hand = Button(HandText, ToggleHand, "Выбрать руку для крестовины");
        hand.FontSize = 10;
        hand.Padding = new Thickness(2);
        handButton = hand;
        sidebar = new VerticalStackLayout { Spacing = 10, Children = {
            Text("ДАЛЬШЕ", 10, Muted, true), next,
            Text("ЛИНИИ", 10, Muted, true), lines, sound, hand
        }};
        sidebarScroll = new ScrollView { Content = sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        moveLeft = RepeatButton("←", () => game.Move(-1), "Сдвинуть влево");
        moveRight = RepeatButton("→", () => game.Move(1), "Сдвинуть вправо");
        softDrop = RepeatButton("↓", game.SoftDrop, "Ускорить падение");
        rotate = Button("↻", () => Act(() => game.Rotate()), "Повернуть по часовой стрелке");
        hardDrop = Button("СБРОСИТЬ  ↓↓", () => Act(game.HardDrop), "Мгновенно опустить фигуру", true);
        hardDrop.FontSize = 12;
        helpButton = Button("?", OpenHelp, "Как играть");
        controls = [moveLeft, moveRight, softDrop, rotate, hardDrop, helpButton];
        play = Button("ИГРАТЬ   →", Play, "Начать игру", true);
        restart = Button("Новая игра", () => { game.Start(); helpOpen = false; ArrangeScene(); }, "Начать новую партию");
        var overlayContent = new VerticalStackLayout { Spacing = 14, Padding = 20,
            Children = { hero, Text("G A M E   O F   B L O C K S", 11, Mint, true), overlayTitle, overlayText, play, restart } };
        overlay = Card(new ScrollView { Content = overlayContent }, 24);
        overlay.BackgroundColor = Color.FromArgb("#131B32");
        overlay.ZIndex = 10;
        foreach (var view in new View[] { header, stats, boardFrame, sidebarScroll, overlay }) scene.Add(view);
        foreach (var view in controls) scene.Add(view);
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
    private readonly Button handButton;

    private string HandText => rightHanded ? "Рука: правая" : "Рука: левая";

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
            _ => () => { }
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

    private void ToggleHand()
    {
        rightHanded = !rightHanded;
        Preferences.Default.Set("righthanded", rightHanded);
        handButton.Text = HandText;
        ArrangeScene();
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
            ? "← → — движение, ↻ — поворот.\n↓ — мягкое падение. СБРОСИТЬ — мгновенное.\nПолная линия исчезает. Каждые 10 линий — новый уровень.\nКонтур показывает место приземления."
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
        board.Invalidate(); next.Invalidate();
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
            foreach (var view in new View[] { header, stats, boardFrame, sidebarScroll }) Place(view, 0, 0, 1, 1);
            foreach (var view in controls) Place(view, 0, 0, 1, 1);
        }
        else if (panes.Count == 0) spaceLimited = true;
        else if (wide)
        {
            // Две руки: поле по центру, кластеры управления в нижних углах.
            var side = Math.Clamp(w * .27, 170, 320);
            var bh = Math.Max(120, Math.Min(h - 20, (w - side * 2) * 2));
            var bw = bh / 2;
            var boardX = (w - bw) / 2;
            Place(boardFrame, boardX, (h - bh) / 2, bw + 2, bh + 2);
            var leftWidth = Math.Max(1, boardX - pad - 14);
            var rightX = boardX + bw + 16;
            var rightWidth = Math.Max(1, w - pad - rightX);
            var key = Math.Clamp(Math.Min((h - 160) / 2.6, (Math.Min(leftWidth, rightWidth) - Gap) / 2), 46, 88);
            var leftHeight = key * 2 + Gap;
            var rightHeight = key * 1.5 + Gap + DropHeight(key);
            // В узкой боковой колонке (планшет в портрете) заголовок не должен переноситься на две строки.
            title.FontSize = leftWidth < 300 ? 16 : 23;
            Place(header, pad, 10, leftWidth, 54);
            Place(stats, pad, 70, leftWidth, 70);
            var sidebarHeight = Math.Max(1, h - 26 - rightHeight - 10);
            next.HeightRequest = Math.Clamp(sidebarHeight * .4, 60, 130);
            Place(sidebarScroll, rightX, 10, Math.Min(rightWidth, 190), sidebarHeight);
            PlaceTwoHanded(new LayoutRect(pad, h - 12 - leftHeight, leftWidth, leftHeight),
                new LayoutRect(rightX, h - 12 - rightHeight, rightWidth, rightHeight), key);
            spaceLimited |= bh < 200 || Math.Min(leftWidth, rightWidth) < 130;
            status.Text = "МАРАФОН  /  В СВОЁМ РИТМЕ";
        }
        else
        {
            title.FontSize = 23;
            Place(header, pad, 10, w - pad * 2, 54);
            Place(stats, pad, 74, w - pad * 2, 70);
            var key = Math.Clamp(Math.Min(h * .076, (w - pad * 2 - 40) / 3.2), 46, 78);
            var zone = ZoneHeight(key);
            var sideWidth = w >= 600 ? 140d : 82d;
            var bh = Math.Max(120, Math.Min(h - 183 - zone, (w - pad * 2 - sideWidth - 16) * 2));
            var bw = bh / 2;
            var left = (w - bw - sideWidth - 16) / 2;
            Place(boardFrame, left, 155, bw + 2, bh + 2);
            next.HeightRequest = Math.Clamp(bh * .31, 72, 170);
            Place(sidebarScroll, left + bw + 16, 156, sideWidth, bh);
            PlaceOneHanded(new LayoutRect(pad, h - 12 - zone, w - pad * 2, zone), key);
            spaceLimited |= bh < 200;
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
        // В низкой широкой панели шапка и счёт занимают один ряд, иначе управление не поместится.
        var row = pane.Width > pane.Height * 1.2;
        var headerWidth = row ? width * .4 : width;
        title.FontSize = headerWidth < 300 ? 16 : 23;
        Place(header, x, pane.Y + 10, headerWidth, 54);
        Place(stats, row ? x + width * .42 : x, pane.Y + (row ? 8 : 74), row ? width * .58 : width, row ? 62 : 70);
        var top = pane.Y + (row ? 78 : 154);
        var rest = Math.Max(1, pane.Y + pane.Height - 12 - top);
        var key = Math.Clamp(Math.Min((rest - 50) / 3.9, (width - 40) / 3.2), 46, 78);
        var zone = ZoneHeight(key);
        var cross = CrossSize(key);
        if (rest - zone < 150 && width - cross > 150)
        {
            // Панель «ДАЛЬШЕ» встаёт рядом с крестовиной, со стороны свободной руки.
            next.HeightRequest = Math.Clamp(rest * .45, 60, 130);
            Place(sidebarScroll, rightHanded ? x : x + cross + 24, top, width - cross - 24, rest);
        }
        else
        {
            var sidebarHeight = Math.Max(1, rest - zone - 12);
            next.HeightRequest = Math.Clamp(sidebarHeight * .45, 60, 150);
            Place(sidebarScroll, x, top, width, sidebarHeight);
        }

        PlaceOneHanded(new LayoutRect(x, top + Math.Max(0, rest - zone), width, Math.Min(rest, zone)), key);
    }

    // Крестовина: ↻ сверху, ← и → по бокам, ↓ снизу; центр пустой.
    private void PlaceDpad(double x, double y, double key)
    {
        var step = key + Gap;
        Place(rotate, x + step, y, key, key);
        Place(moveLeft, x, y + step, key, key);
        Place(moveRight, x + step * 2, y + step, key, key);
        Place(softDrop, x + step, y + step * 2, key, key);
    }

    // Одна рука: крестовина в нижнем углу под большой палец, «СБРОСИТЬ» и «?» — над ней.
    private void PlaceOneHanded(LayoutRect zone, double key)
    {
        var cross = CrossSize(key);
        var drop = DropHeight(key);
        var x = rightHanded ? zone.X + zone.Width - cross : zone.X;
        var bottom = zone.Y + zone.Height;
        PlaceDpad(x, bottom - cross, key);
        Place(hardDrop, x, bottom - cross - Gap - drop, cross - drop - Gap, drop);
        Place(helpButton, x + cross - drop, bottom - cross - Gap - drop, drop, drop);
    }

    // Две руки: слева ← → и ↓ под ними, справа крупный ↻, над ним «СБРОСИТЬ» и «?».
    private void PlaceTwoHanded(LayoutRect left, LayoutRect right, double key)
    {
        var step = key + Gap;
        Place(moveLeft, left.X, left.Y, key, key);
        Place(moveRight, left.X + step, left.Y, key, key);
        Place(softDrop, left.X + step / 2, left.Y + step, key, key);
        var big = key * 1.5;
        var drop = DropHeight(key);
        var edge = right.X + right.Width;
        var width = Math.Min(right.Width, Math.Max(big, key * 2.4));
        Place(rotate, edge - big, right.Y + right.Height - big, big, big);
        Place(hardDrop, edge - width, right.Y, width - drop - Gap, drop);
        Place(helpButton, edge - drop, right.Y, drop, drop);
    }

    private static double CrossSize(double key) => key * 3 + Gap * 2;

    private static double DropHeight(double key) => Math.Clamp(key * .8, 40, 56);

    private static double ZoneHeight(double key) => CrossSize(key) + Gap + DropHeight(key);

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
