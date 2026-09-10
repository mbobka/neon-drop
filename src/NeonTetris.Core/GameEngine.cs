namespace NeonTetris.Core;

public sealed partial class GameEngine
{
    public const int Width = 10;
    public const int Height = 20;

    private const int PreviewCount = 5;
    private const int MaxLockResets = 15;
    private const long LockDelayTicks = TimeSpan.TicksPerMillisecond * 500;
    private readonly List<Tetromino> _queue = [];
    private uint _initialRandomState;
    private uint _randomState;
    private long _gravityTicks;
    private long _lockTicks;
    private int _lockResets;

    public GameEngine(int? seed = null)
    {
        _initialRandomState = unchecked((uint)(seed ?? Random.Shared.Next()));
        if (_initialRandomState == 0)
        {
            _initialRandomState = 0x9E3779B9;
        }

        _randomState = _initialRandomState;
    }

    // Board[x, y] содержит только зафиксированные клетки. UI должен лишь читать массив.
    public int[,] Board { get; } = new int[Width, Height];
    public ActivePiece? Active { get; private set; }
    public IReadOnlyList<Tetromino> Next { get; private set; } = Array.Empty<Tetromino>();
    public bool IsPaused { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsStarted { get; private set; }
    public int Score { get; private set; }
    public int Lines { get; private set; }
    public int Level => Lines / 10 + 1;

    public ActivePiece? Ghost
    {
        get
        {
            if (Active is not { } piece)
            {
                return null;
            }

            while (CanPlace(piece with { Y = piece.Y + 1 }))
            {
                piece = piece with { Y = piece.Y + 1 };
            }

            return piece;
        }
    }

    private bool CanPlay => IsStarted && !IsPaused && !IsGameOver && Active is not null;
    private long GravityIntervalTicks => GetGravityIntervalTicks(Lines);

    private static long GetGravityIntervalTicks(int lines) =>
        TimeSpan.FromMilliseconds(Math.Max(50, 1000 * Math.Pow(0.8, lines / 10))).Ticks;

    public void Start()
    {
        Array.Clear(Board);
        _queue.Clear();
        _randomState = _initialRandomState;
        Score = 0;
        Lines = 0;
        IsPaused = false;
        IsGameOver = false;
        IsStarted = true;
        SpawnNext();
    }

    public void TogglePause()
    {
        if (IsStarted && !IsGameOver)
        {
            IsPaused = !IsPaused;
        }
    }

    public void Move(int dx)
    {
        if (!CanPlay || dx == 0)
        {
            return;
        }

        var piece = Active!;
        var grounded = IsGrounded(piece);
        var step = Math.Sign(dx);
        var count = Math.Min(Math.Abs((long)dx), Width);
        for (var i = 0; i < count; i++)
        {
            var candidate = piece with { X = piece.X + step };
            if (!CanPlace(candidate))
            {
                break;
            }

            piece = candidate;
        }

        if (piece != Active)
        {
            Active = piece;
            ResetLockAfterManipulation(grounded);
        }
    }

    // Положительное направление — 90° по часовой стрелке, отрицательное — против.
    public void Rotate(int direction = 1)
    {
        if (!CanPlay || direction == 0)
        {
            return;
        }

        var piece = Active!;
        var rotation = (piece.Rotation + (direction > 0 ? 1 : 3)) % 4;
        foreach (var kick in PieceGeometry.Kicks(piece.Kind, piece.Rotation, rotation))
        {
            var candidate = piece with { Rotation = rotation, X = piece.X + kick.X, Y = piece.Y + kick.Y };
            if (CanPlace(candidate))
            {
                Active = candidate;
                ResetLockAfterManipulation(IsGrounded(piece));
                return;
            }
        }
    }

    public void SoftDrop()
    {
        if (!CanPlay)
        {
            return;
        }

        var candidate = Active! with { Y = Active!.Y + 1 };
        if (CanPlace(candidate))
        {
            Active = candidate;
            _gravityTicks = 0;
            AddScore(1);
        }
    }

    public void HardDrop()
    {
        if (!CanPlay)
        {
            return;
        }

        var landing = Ghost!;
        AddScore((landing.Y - Active!.Y) * 2L);
        Active = landing;
        LockActive();
    }

    public void Tick(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        var remaining = elapsed.Ticks;
        while (CanPlay && remaining > 0)
        {
            // Обработка до ближайшего события делает результат независимым от частоты кадров.
            if (IsGrounded(Active!))
            {
                var step = Math.Min(remaining, LockDelayTicks - _lockTicks);
                _lockTicks += step;
                remaining -= step;
                if (_lockTicks >= LockDelayTicks)
                {
                    LockActive();
                }
            }
            else
            {
                var interval = GravityIntervalTicks;
                var step = Math.Min(remaining, interval - _gravityTicks);
                _gravityTicks += step;
                remaining -= step;
                if (_gravityTicks >= interval)
                {
                    _gravityTicks = 0;
                    Active = Active! with { Y = Active!.Y + 1 };
                }
            }
        }
    }

    public IEnumerable<Cell> GetCells(ActivePiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (!Enum.IsDefined(piece.Kind) || piece.Rotation is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(piece));
        }

        return PieceGeometry.Shape(piece.Kind, piece.Rotation)
            .Select(cell => new Cell(piece.X + cell.X, piece.Y + cell.Y));
    }

    private bool CanPlace(ActivePiece piece) => CanPlace(piece, Board);

    private bool CanPlace(ActivePiece piece, int[,] board) => GetCells(piece).All(cell =>
        cell.X >= 0 && cell.X < Width && cell.Y >= -4 && cell.Y < Height &&
        (cell.Y < 0 || board[cell.X, cell.Y] == 0));

    private bool IsGrounded(ActivePiece piece) => !CanPlace(piece with { Y = piece.Y + 1 });

    private void ResetLockAfterManipulation(bool wasGrounded)
    {
        if (wasGrounded && _lockResets < MaxLockResets)
        {
            _lockTicks = 0;
            _lockResets++;
        }
    }

    private void SpawnNext()
    {
        RefillQueue();
        var kind = _queue[0];
        _queue.RemoveAt(0);
        RefillQueue();
        RefreshPreview();
        Spawn(kind);
    }

    private void Spawn(Tetromino kind)
    {
        _gravityTicks = 0;
        _lockTicks = 0;
        _lockResets = 0;
        var piece = new ActivePiece(kind, 0, 3, -1);
        if (CanPlace(piece))
        {
            Active = piece;
        }
        else
        {
            EndGame();
        }
    }

    private void LockActive()
    {
        var cells = GetCells(Active!).ToArray();
        if (cells.Any(cell => cell.Y < 0))
        {
            EndGame();
            return;
        }

        foreach (var cell in cells)
        {
            Board[cell.X, cell.Y] = (int)Active!.Kind + 1;
        }

        var cleared = ClearLines();
        var points = cleared switch { 1 => 100, 2 => 300, 3 => 500, 4 => 800, _ => 0 };
        AddScore((long)points * Level);
        Lines = (int)Math.Min(int.MaxValue, (long)Lines + cleared);
        SpawnNext();
    }

    private int ClearLines()
    {
        var target = Height - 1;
        for (var source = Height - 1; source >= 0; source--)
        {
            var full = true;
            for (var x = 0; x < Width; x++)
            {
                full &= Board[x, source] != 0;
            }

            if (full)
            {
                continue;
            }

            for (var x = 0; x < Width; x++)
            {
                Board[x, target] = Board[x, source];
            }

            target--;
        }

        var cleared = target + 1;
        for (var y = target; y >= 0; y--)
        {
            for (var x = 0; x < Width; x++)
            {
                Board[x, y] = 0;
            }
        }

        return cleared;
    }

    private void EndGame()
    {
        Active = null;
        IsGameOver = true;
        IsPaused = false;
        _gravityTicks = 0;
        _lockTicks = 0;
        _lockResets = 0;
    }

    private void AddScore(long points) => Score = (int)Math.Min(int.MaxValue, Score + points);

    private void RefreshPreview() => Next = Array.AsReadOnly(_queue.Take(PreviewCount).ToArray());

    private void RefillQueue()
    {
        if (_queue.Count >= PreviewCount)
        {
            return;
        }

        var bag = Enum.GetValues<Tetromino>();
        for (var i = bag.Length - 1; i > 0; i--)
        {
            var j = NextRandom(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        _queue.AddRange(bag);
    }

    private int NextRandom(int bound)
    {
        // Явное состояние PRNG позволяет точно продолжить 7-bag после загрузки.
        var limit = uint.MaxValue - uint.MaxValue % (uint)bound;
        uint value;
        do
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            value = _randomState - 1;
        }
        while (value >= limit);

        return (int)(value % (uint)bound);
    }
}
