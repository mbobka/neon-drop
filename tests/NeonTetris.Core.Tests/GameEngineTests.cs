using NeonTetris.Core;
using Xunit;

namespace NeonTetris.Core.Tests;

public sealed class GameEngineTests
{
    [Fact]
    public void BeforeStartCommandsDoNotCreateOrChangeTheGame()
    {
        var game = new GameEngine(42);
        var before = game.SerializeSnapshot();

        game.TogglePause();
        game.Move(1);
        game.Rotate();
        game.SoftDrop();
        game.HardDrop();
        game.Tick(TimeSpan.FromMinutes(1));

        Assert.Equal(before, game.SerializeSnapshot());
        Assert.False(game.IsStarted);
        Assert.Null(game.Active);
        Assert.Null(game.Ghost);
        Assert.Empty(game.Next);
        Assert.Equal(1, game.Level);
    }

    [Fact]
    public void StartResetsEverythingAndRetainsTheBoardReference()
    {
        var game = new GameEngine(42);
        var board = game.Board;
        game.Start();
        var initial = game.SerializeSnapshot();
        game.Move(-2);
        game.HardDrop();
        game.TogglePause();

        game.Start();

        Assert.Same(board, game.Board);
        Assert.Equal(initial, game.SerializeSnapshot());
        Assert.Equal(10, game.Board.GetLength(0));
        Assert.Equal(20, game.Board.GetLength(1));
        Assert.Equal(5, game.Next.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void EverySevenDrawsContainAllKindsAndSeedIsReproducible(int seed)
    {
        var game = new GameEngine(seed);
        var twin = new GameEngine(seed);
        game.Start();
        twin.Start();
        for (var bag = 0; bag < 20; bag++)
        {
            var drawn = new HashSet<Tetromino>();
            for (var index = 0; index < 7; index++)
            {
                Assert.Equal(game.Active, twin.Active);
                Assert.Equal(game.Next.ToArray(), twin.Next.ToArray());
                drawn.Add(game.Active!.Kind);
                var expectedNext = game.Next[0];
                game.HardDrop();
                twin.HardDrop();
                Assert.Equal(expectedNext, game.Active!.Kind);
                Array.Clear(game.Board);
                Array.Clear(twin.Board);
            }

            Assert.Equal(7, drawn.Count);
        }
    }

    [Fact]
    public void DifferentSeedsProduceDifferentSequences()
    {
        var first = new GameEngine(1);
        var second = new GameEngine(2);
        first.Start();
        second.Start();

        Assert.NotEqual(first.Next.ToArray(), second.Next.ToArray());
    }

    [Fact]
    public void PreviewCannotBeMutatedThroughACollectionCast()
    {
        var game = new GameEngine(42);
        game.Start();

        Assert.Throws<NotSupportedException>(() => ((IList<Tetromino>)game.Next)[0] = Tetromino.O);
    }

    [Fact]
    public void PauseFreezesAllGameplayAndDoesNotAccumulateTime()
    {
        var game = GameFixture.Create(new(Tetromino.T, 0, 3, 5));
        game.Tick(TimeSpan.FromMilliseconds(700));
        game.TogglePause();
        var before = game.SerializeSnapshot();

        game.Move(1);
        game.Rotate();
        game.SoftDrop();
        game.HardDrop();
        game.Tick(TimeSpan.FromHours(2));

        Assert.Equal(before, game.SerializeSnapshot());
        game.TogglePause();
        game.Tick(TimeSpan.FromMilliseconds(299));
        Assert.Equal(5, game.Active!.Y);
        game.Tick(TimeSpan.FromMilliseconds(1));
        Assert.Equal(6, game.Active!.Y);
    }

    [Theory]
    [InlineData(int.MinValue, -1)]
    [InlineData(int.MaxValue, 7)]
    public void LargeMovesStopAtTheWallWithoutOverflow(int dx, int expectedX)
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, 5));

        game.Move(dx);

        Assert.Equal(expectedX, game.Active!.X);
        Assert.Equal(0, GameFixture.Occupied(game));
    }

    [Fact]
    public void LargeMoveCannotTunnelThroughOccupiedCells()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 0, 5), board => board[4, 5] = 7);

        game.Move(9);

        Assert.Equal(1, game.Active!.X);
        Assert.Equal(7, game.Board[4, 5]);
    }

    [Fact]
    public void GhostStopsAboveAnObstacleAndHardDropLocksExactlyThoseCells()
    {
        var game = GameFixture.Create(new(Tetromino.T, 0, 3, 2), board => board[4, 10] = 7);
        var before = game.SerializeSnapshot();
        var ghost = game.Ghost!;
        var landingCells = game.GetCells(ghost).ToArray();

        Assert.Equal(8, ghost.Y);
        Assert.Equal(before, game.SerializeSnapshot());
        Assert.Equal(1, GameFixture.Occupied(game));

        game.HardDrop();

        Assert.Equal(12, game.Score);
        Assert.Equal(5, GameFixture.Occupied(game));
        Assert.All(landingCells, cell => Assert.Equal((int)Tetromino.T + 1, game.Board[cell.X, cell.Y]));
        Assert.Equal(7, game.Board[4, 10]);
    }

    [Fact]
    public void SoftDropAwardsOnlySuccessfulMovementAndDoesNotLockImmediately()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, 17));

        game.SoftDrop();
        game.SoftDrop();

        Assert.Equal(18, game.Active!.Y);
        Assert.Equal(1, game.Score);
        Assert.Equal(0, GameFixture.Occupied(game));
        game.Tick(TimeSpan.FromMilliseconds(500));
        Assert.Equal(4, GameFixture.Occupied(game));
    }

    [Fact]
    public void LockedPieceIsFollowedByThePreviewedKindAtSpawnOrientation()
    {
        var game = GameFixture.Create(new(Tetromino.T, 2, 1, 8));
        var expectedNext = game.Next[0];

        game.HardDrop();

        Assert.Equal(new ActivePiece(expectedNext, 0, 3, -1), game.Active);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 300)]
    [InlineData(3, 500)]
    [InlineData(4, 800)]
    public void ClearsRowsAndScoresSinglesDoublesTriplesAndTetrises(int count, int expectedScore)
    {
        var game = GameFixture.Create(new(Tetromino.I, 1, 2, 16), board =>
        {
            for (var y = 20 - count; y < 20; y++)
            {
                GameFixture.FillRow(board, y, 4);
            }

            board[0, 15] = 7;
        });

        game.HardDrop();

        Assert.Equal(count, game.Lines);
        Assert.Equal(expectedScore, game.Score);
        Assert.Equal(7, game.Board[0, 15 + count]);
        Assert.Equal(5 - count, GameFixture.Occupied(game));
        for (var y = 0; y < count; y++)
        {
            Assert.All(Enumerable.Range(0, 10), x => Assert.Equal(0, game.Board[x, y]));
        }
    }

    [Fact]
    public void NonAdjacentFullRowsCollapseWithoutLosingIntermediateRows()
    {
        var game = GameFixture.Create(new(Tetromino.I, 1, 2, 16), board =>
        {
            GameFixture.FillRow(board, 19, 4);
            GameFixture.FillRow(board, 17, 4);
            board[0, 18] = 7;
            board[1, 16] = 6;
        });

        game.HardDrop();

        Assert.Equal(2, game.Lines);
        Assert.Equal(300, game.Score);
        Assert.Equal(7, game.Board[0, 19]);
        Assert.Equal(6, game.Board[1, 18]);
        Assert.Equal(1, game.Board[4, 19]);
        Assert.Equal(1, game.Board[4, 18]);
    }

    [Theory]
    [InlineData(9, 100, 2, 800)]
    [InlineData(19, 200, 3, 640)]
    public void LevelChangesEveryTenLinesAndScoreUsesThePreviousLevel(int lines, int points, int level, int interval)
    {
        var game = GameFixture.Create(new(Tetromino.I, 1, 2, 16),
            board => GameFixture.FillRow(board, 19, 4), lines);

        game.HardDrop();

        Assert.Equal(lines + 1, game.Lines);
        Assert.Equal(level, game.Level);
        Assert.Equal(points, game.Score);
        var spawnY = game.Active!.Y;
        game.Tick(TimeSpan.FromMilliseconds(interval - 1));
        Assert.Equal(spawnY, game.Active!.Y);
        game.Tick(TimeSpan.FromMilliseconds(1));
        Assert.Equal(spawnY + 1, game.Active!.Y);
        Assert.Equal(points, game.Score);
    }

    [Fact]
    public void SpawnCollisionEndsTheGameAndAllGameplayStops()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, 18), board => GameFixture.FillRow(board, 0, 0));

        game.HardDrop();

        Assert.True(game.IsGameOver);
        Assert.True(game.IsStarted);
        Assert.Null(game.Active);
        Assert.Null(game.Ghost);
        var before = game.SerializeSnapshot();
        game.Move(-1);
        game.Rotate();
        game.SoftDrop();
        game.HardDrop();
        game.TogglePause();
        game.Tick(TimeSpan.FromDays(1));
        Assert.Equal(before, game.SerializeSnapshot());
        game.Start();
        Assert.False(game.IsGameOver);
        Assert.NotNull(game.Active);
    }

    [Fact]
    public void LockingAboveTheCeilingEndsTheGameWithoutPartiallyWritingAPiece()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, -1), board => board[4, 1] = 7);

        game.Tick(TimeSpan.FromMilliseconds(500));

        Assert.True(game.IsGameOver);
        Assert.Equal(1, GameFixture.Occupied(game));
        Assert.Null(game.Active);
    }
}
