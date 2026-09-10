using NeonTetris.Core;

namespace NeonTetris;

internal static class GameArtwork
{
    internal static readonly Color[] Colors = [
        Color.FromArgb("#63DEEF"), Color.FromArgb("#F8CF72"), Color.FromArgb("#B39BFF"),
        Color.FromArgb("#6DE1BA"), Color.FromArgb("#F280A3"), Color.FromArgb("#7C9EFF"),
        Color.FromArgb("#FFA67A")];

    internal static void Block(ICanvas canvas, float x, float y, float size, int kind, bool ghost = false)
    {
        var color = Colors[kind];
        var gap = Math.Max(1.2f, size * .06f);
        var rect = new RectF(x + gap, y + gap, size - gap * 2, size - gap * 2);
        canvas.FillColor = color.WithAlpha(ghost ? .10f : .95f);
        canvas.FillRoundedRectangle(rect, size * .16f);
        canvas.StrokeColor = color.WithAlpha(ghost ? .65f : 1);
        canvas.StrokeSize = ghost ? 1.3f : .7f;
        canvas.DrawRoundedRectangle(rect, size * .16f);
        if (ghost) return;
        canvas.FillColor = Microsoft.Maui.Graphics.Colors.White.WithAlpha(.24f);
        canvas.FillRoundedRectangle(x + gap + 2, y + gap + 2, size - gap * 2 - 4, size * .19f, 2);
        canvas.StrokeColor = Microsoft.Maui.Graphics.Colors.Black.WithAlpha(.16f);
        canvas.StrokeSize = 2;
        canvas.DrawLine(rect.Left + 4, rect.Bottom - 3, rect.Right - 4, rect.Bottom - 3);
    }
}

internal sealed class BoardDrawable(GameEngine game) : IDrawable
{
    public double Flash { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cell = Math.Min(dirtyRect.Width / GameEngine.Width, dirtyRect.Height / GameEngine.Height);
        canvas.FillColor = Color.FromArgb("#0B1023");
        canvas.FillRectangle(dirtyRect);
        canvas.StrokeColor = Color.FromArgb("#1B2540");
        canvas.StrokeSize = .5f;
        for (var x = 0; x <= GameEngine.Width; x++)
            canvas.DrawLine(x * cell, 0, x * cell, GameEngine.Height * cell);
        for (var y = 0; y <= GameEngine.Height; y++)
            canvas.DrawLine(0, y * cell, GameEngine.Width * cell, y * cell);
        if (!game.IsStarted)
        {
            int[] heights = [2, 3, 2, 1, 0, 1, 2, 3, 2, 1];
            for (var x = 0; x < 10; x++)
                for (var y = 20 - heights[x]; y < 20; y++)
                    GameArtwork.Block(canvas, x * cell, y * cell, cell, (x + y / 2) % 7);
            return;
        }
        for (var x = 0; x < GameEngine.Width; x++)
            for (var y = 0; y < GameEngine.Height; y++)
                if (game.Board[x, y] > 0)
                    GameArtwork.Block(canvas, x * cell, y * cell, cell, game.Board[x, y] - 1);
        if (game.Ghost is { } ghost)
            foreach (var point in game.GetCells(ghost).Where(p => p.Y >= 0))
                GameArtwork.Block(canvas, point.X * cell, point.Y * cell, cell, (int)ghost.Kind, true);
        if (game.Active is { } active)
            foreach (var point in game.GetCells(active).Where(p => p.Y >= 0))
                GameArtwork.Block(canvas, point.X * cell, point.Y * cell, cell, (int)active.Kind);
        if (Flash > 0)
        {
            canvas.FillColor = Color.FromArgb("#94FFE0").WithAlpha((float)Flash * .23f);
            canvas.FillRectangle(dirtyRect);
        }
    }
}

internal sealed class PreviewDrawable(GameEngine game, bool hold) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var pieces = hold
            ? (game.Held is { } held ? new[] { held } : Array.Empty<Tetromino>())
            : game.Next.Take(3).ToArray();
        var slotHeight = dirtyRect.Height / (hold ? 1 : 3);
        var cell = Math.Min(21, Math.Min(dirtyRect.Width / 5, slotHeight / 3));
        for (var i = 0; i < pieces.Length; i++)
        {
            var points = game.GetCells(new ActivePiece(pieces[i], 0, 0, 0)).ToArray();
            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);
            var width = points.Max(p => p.X) - minX + 1;
            var height = points.Max(p => p.Y) - minY + 1;
            foreach (var point in points)
                GameArtwork.Block(canvas,
                    (dirtyRect.Width - width * cell) / 2 + (point.X - minX) * cell,
                    i * slotHeight + (slotHeight - height * cell) / 2 + (point.Y - minY) * cell,
                    cell, (int)pieces[i]);
        }
        if (hold && pieces.Length == 0)
        {
            canvas.FontColor = Color.FromArgb("#65728F");
            canvas.FontSize = 22;
            canvas.DrawString("—", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
