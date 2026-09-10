using NeonTetris.Core;
using Xunit;

namespace NeonTetris.Core.Tests;

public sealed class TimingTests
{
    [Fact]
    public void GravityPreservesFractionalTimeWithoutAwardingDropPoints()
    {
        var game = GameFixture.Create(new(Tetromino.T, 0, 3, 5));

        game.Tick(TimeSpan.FromMilliseconds(999));
        Assert.Equal(5, game.Active!.Y);
        game.Tick(TimeSpan.FromMilliseconds(2501));
        Assert.Equal(8, game.Active!.Y);
        game.Tick(TimeSpan.FromMilliseconds(500));
        Assert.Equal(9, game.Active!.Y);
        Assert.Equal(0, game.Score);
    }

    [Fact]
    public void LockDelayStartsAtLandingWithinTheCurrentTick()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, 17));

        game.Tick(TimeSpan.FromMilliseconds(1499));

        Assert.Equal(18, game.Active!.Y);
        Assert.Equal(0, GameFixture.Occupied(game));
        game.Tick(TimeSpan.FromMilliseconds(1));
        Assert.Equal(4, GameFixture.Occupied(game));
        Assert.Equal(-1, game.Active!.Y);
        game.Tick(TimeSpan.FromMilliseconds(1000));
        Assert.Equal(0, game.Active!.Y);
    }

    [Fact]
    public void TimeRemainingAfterLockAdvancesTheNextPiece()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, 18));

        game.Tick(TimeSpan.FromMilliseconds(1700));

        Assert.Equal(4, GameFixture.Occupied(game));
        Assert.Equal(0, game.Active!.Y);
        game.Tick(TimeSpan.FromMilliseconds(800));
        Assert.Equal(1, game.Active!.Y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulGroundedManipulationResetsLockDelay(bool rotate)
    {
        var game = GameFixture.Create(new(Tetromino.T, 1, 3, 17));
        game.Tick(TimeSpan.FromMilliseconds(400));

        if (rotate)
        {
            game.Rotate();
        }
        else
        {
            game.Move(1);
        }

        game.Tick(TimeSpan.FromMilliseconds(499));
        Assert.Equal(0, GameFixture.Occupied(game));
        game.Tick(TimeSpan.FromMilliseconds(1));
        Assert.Equal(4, GameFixture.Occupied(game));
    }

    [Fact]
    public void FailedAndZeroCommandsDoNotExtendLockDelay()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, -1, 18));
        game.Tick(TimeSpan.FromMilliseconds(400));

        game.Move(-1);
        game.Move(0);
        game.Rotate(0);
        game.SoftDrop();
        game.Tick(TimeSpan.FromMilliseconds(100));

        Assert.Equal(4, GameFixture.Occupied(game));
    }

    [Fact]
    public void SixteenthManipulationCannotPostponeLocking()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, 18));
        for (var i = 0; i < 15; i++)
        {
            game.Tick(TimeSpan.FromMilliseconds(400));
            game.Move(i % 2 == 0 ? 1 : -1);
            Assert.Equal(0, GameFixture.Occupied(game));
        }

        game.Tick(TimeSpan.FromMilliseconds(400));
        game.Move(-1);
        game.Tick(TimeSpan.FromMilliseconds(100));

        Assert.Equal(4, GameFixture.Occupied(game));
    }

    [Fact]
    public void LeavingALedgePausesButDoesNotResetExhaustedLockDelay()
    {
        var game = GameFixture.Create(new(Tetromino.O, 0, 3, 16), board => board[4, 18] = 7);
        game.Tick(TimeSpan.FromMilliseconds(400));
        game.RestoreSnapshot(GameFixture.ChangeSnapshot(game, snapshot => snapshot["LockResets"] = 15));

        game.Move(1);
        game.Tick(TimeSpan.FromMilliseconds(2000));

        Assert.Equal(18, game.Active!.Y);
        Assert.Equal(1, GameFixture.Occupied(game));
        game.Tick(TimeSpan.FromMilliseconds(99));
        Assert.Equal(1, GameFixture.Occupied(game));
        game.Tick(TimeSpan.FromMilliseconds(1));
        Assert.Equal(5, GameFixture.Occupied(game));
    }

    [Fact]
    public void OneLongTickAndManyShortTicksProduceTheSameStateAcrossLocks()
    {
        var first = GameFixture.Create(new(Tetromino.O, 0, 3, 17));
        var second = new GameEngine();
        second.RestoreSnapshot(first.SerializeSnapshot());

        first.Tick(TimeSpan.FromSeconds(55));
        for (var i = 0; i < 5500; i++)
        {
            second.Tick(TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(first.SerializeSnapshot(), second.SerializeSnapshot());
        Assert.True(GameFixture.Occupied(first) >= 8);
    }

    [Fact]
    public void NegativeTimeIsRejectedAndZeroTimeDoesNothing()
    {
        var game = new GameEngine(42);
        game.Start();
        var before = game.SerializeSnapshot();

        Assert.Throws<ArgumentOutOfRangeException>(() => game.Tick(TimeSpan.FromTicks(-1)));
        game.Tick(TimeSpan.Zero);

        Assert.Equal(before, game.SerializeSnapshot());
    }

    [Fact]
    public void ExtremelyLongElapsedTimeFinishesAtGameOver()
    {
        var game = new GameEngine(42);
        game.Start();

        game.Tick(TimeSpan.MaxValue);

        Assert.True(game.IsGameOver);
        Assert.Null(game.Active);
    }
}
