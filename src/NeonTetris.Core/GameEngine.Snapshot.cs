using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeonTetris.Core;

public sealed partial class GameEngine
{
    public string SerializeSnapshot()
    {
        var board = new int[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                board[y * Width + x] = Board[x, y];
            }
        }

        return JsonSerializer.Serialize(new Snapshot
        {
            Version = 1,
            Board = board,
            Active = Active,
            Queue = _queue.ToArray(),
            IsPaused = IsPaused,
            IsGameOver = IsGameOver,
            IsStarted = IsStarted,
            Score = Score,
            Lines = Lines,
            InitialRandomState = _initialRandomState,
            RandomState = _randomState,
            GravityTicks = _gravityTicks,
            LockTicks = _lockTicks,
            LockResets = _lockResets
        }, SnapshotJsonContext.Default.Snapshot);
    }

    public void RestoreSnapshot(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Snapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.Snapshot)
                ?? throw new ArgumentException("Снимок не содержит состояния игры.", nameof(json));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Некорректный JSON снимка игры.", nameof(json), exception);
        }

        var board = ValidateSnapshot(snapshot);

        // Применяем состояние только после полной проверки, сохраняя ссылку UI на Board.
        Array.Copy(board, Board, board.Length);
        Active = snapshot.Active;
        _queue.Clear();
        _queue.AddRange(snapshot.Queue);
        RefreshPreview();
        IsPaused = snapshot.IsPaused;
        IsGameOver = snapshot.IsGameOver;
        IsStarted = snapshot.IsStarted;
        Score = snapshot.Score;
        Lines = snapshot.Lines;
        _initialRandomState = snapshot.InitialRandomState;
        _randomState = snapshot.RandomState;
        _gravityTicks = snapshot.GravityTicks;
        _lockTicks = snapshot.LockTicks;
        _lockResets = snapshot.LockResets;
    }

    private int[,] ValidateSnapshot(Snapshot snapshot)
    {
        if (snapshot.Version != 1 || snapshot.Board is not { Length: Width * Height } ||
            snapshot.Board.Any(value => value is < 0 or > 7) || snapshot.Queue is null ||
            snapshot.Queue.Any(kind => !Enum.IsDefined(kind)) ||
            snapshot.Score < 0 || snapshot.Lines < 0 ||
            snapshot.InitialRandomState == 0 || snapshot.RandomState == 0 ||
            snapshot.GravityTicks < 0 ||
            snapshot.LockTicks < 0 || snapshot.LockTicks >= LockDelayTicks ||
            snapshot.LockResets is < 0 or > MaxLockResets ||
            snapshot.IsPaused && (!snapshot.IsStarted || snapshot.IsGameOver))
        {
            throw new ArgumentException("Снимок содержит недопустимые значения.", "json");
        }

        if (snapshot.GravityTicks >= GetGravityIntervalTicks(snapshot.Lines))
        {
            throw new ArgumentException("Некорректное время падения в снимке.", "json");
        }

        if (snapshot.IsStarted)
        {
            if (snapshot.Queue.Length is < PreviewCount or > PreviewCount + 6 ||
                (snapshot.Active is null) != snapshot.IsGameOver)
            {
                throw new ArgumentException("Снимок содержит противоречивое состояние игры.", "json");
            }
        }
        else if (snapshot.Active is not null || snapshot.IsGameOver || snapshot.Queue.Length != 0 ||
                 snapshot.Score != 0 || snapshot.Lines != 0 ||
                 snapshot.Board.Any(value => value != 0) || snapshot.GravityTicks != 0 ||
                 snapshot.LockTicks != 0 || snapshot.LockResets != 0 ||
                 snapshot.RandomState != snapshot.InitialRandomState)
        {
            throw new ArgumentException("Снимок незапущенной игры содержит игровое состояние.", "json");
        }

        var board = new int[Width, Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                board[x, y] = snapshot.Board[y * Width + x];
            }
        }

        if (snapshot.Active is { } piece &&
            (!Enum.IsDefined(piece.Kind) || piece.Rotation is < 0 or > 3 ||
             piece.X is < -3 or >= Width || piece.Y is < -7 or >= Height || !CanPlace(piece, board)))
        {
            throw new ArgumentException("Активная фигура в снимке выходит за поле или пересекает блоки.", "json");
        }

        return board;
    }

    private sealed class Snapshot
    {
        public int Version { get; init; }
        public required int[] Board { get; init; }
        public ActivePiece? Active { get; init; }
        public required Tetromino[] Queue { get; init; }
        public bool IsPaused { get; init; }
        public bool IsGameOver { get; init; }
        public bool IsStarted { get; init; }
        public int Score { get; init; }
        public int Lines { get; init; }
        public uint InitialRandomState { get; init; }
        public uint RandomState { get; init; }
        public long GravityTicks { get; init; }
        public long LockTicks { get; init; }
        public int LockResets { get; init; }
    }

    [JsonSerializable(typeof(Snapshot))]
    private sealed partial class SnapshotJsonContext : JsonSerializerContext;
}
