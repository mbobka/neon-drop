using System.Text.Json;
using System.Text.Json.Nodes;
using NeonTetris.Core;
using Xunit;

namespace NeonTetris.Core.Tests;

public sealed class SnapshotTests
{
    [Fact]
    public void SnapshotWorksWithJsonReflectionDisabled()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        var original = GameFixture.Create(new(Tetromino.I, 1, 2, 16),
            board => GameFixture.FillRow(board, 19, 4), lines: 9);
        original.HardDrop();
        original.Tick(TimeSpan.FromMilliseconds(300));
        var restored = new GameEngine();

        restored.RestoreSnapshot(original.SerializeSnapshot());

        Assert.Equal(10, restored.Lines);
        Assert.Equal(2, restored.Level);
        Assert.Equal(100, restored.Score);
        Assert.Equal(original.SerializeSnapshot(), restored.SerializeSnapshot());
        original.Tick(TimeSpan.FromMilliseconds(500));
        restored.Tick(TimeSpan.FromMilliseconds(500));
        Assert.Equal(original.SerializeSnapshot(), restored.SerializeSnapshot());
    }

    [Fact]
    public void SnapshotRestoresPauseBoardAndFutureBagsExactly()
    {
        var original = new GameEngine(123);
        original.Start();
        original.Move(-2);
        original.HardDrop();
        original.Rotate(-1);
        original.SoftDrop();
        original.Tick(TimeSpan.FromMilliseconds(723));
        original.TogglePause();
        var saved = original.SerializeSnapshot();
        var restored = new GameEngine(999);
        var boardReference = restored.Board;

        restored.RestoreSnapshot(saved);

        Assert.Same(boardReference, restored.Board);
        Assert.True(restored.IsPaused);
        Assert.Equal(saved, restored.SerializeSnapshot());
        original.TogglePause();
        restored.TogglePause();
        for (var i = 0; i < 100; i++)
        {
            original.Move(i % 3 - 1);
            restored.Move(i % 3 - 1);
            original.Rotate(i % 2 == 0 ? 1 : -1);
            restored.Rotate(i % 2 == 0 ? 1 : -1);
            original.Tick(TimeSpan.FromMilliseconds(277));
            restored.Tick(TimeSpan.FromMilliseconds(277));
            original.HardDrop();
            restored.HardDrop();
            Assert.Equal(original.SerializeSnapshot(), restored.SerializeSnapshot());
            Array.Clear(original.Board);
            Array.Clear(restored.Board);
        }

        original.Start();
        restored.Start();
        Assert.Equal(original.SerializeSnapshot(), restored.SerializeSnapshot());
    }

    [Fact]
    public void SnapshotWithFieldsOfRemovedFeaturesIsRestoredWithoutThem()
    {
        var game = GameFixture.Create(new(Tetromino.T, 0, 3, 5));
        var expected = game.SerializeSnapshot();
        // Снимки прежних версий хранили резерв; лишние поля просто игнорируются.
        var legacy = GameFixture.ChangeSnapshot(game, snapshot =>
        {
            snapshot["Held"] = 3;
            snapshot["HoldUsed"] = true;
        });

        game.RestoreSnapshot(legacy);

        Assert.Equal(expected, game.SerializeSnapshot());
    }

    [Fact]
    public void SnapshotPreservesLockTimerAndResetLimit()
    {
        var original = GameFixture.Create(new(Tetromino.O, 0, 3, 18));
        for (var i = 0; i < 15; i++)
        {
            original.Move(i % 2 == 0 ? 1 : -1);
        }

        original.Tick(TimeSpan.FromMilliseconds(400));
        var restored = new GameEngine();
        restored.RestoreSnapshot(original.SerializeSnapshot());

        restored.Move(-1);
        restored.Tick(TimeSpan.FromMilliseconds(99));
        Assert.Equal(0, GameFixture.Occupied(restored));
        restored.Tick(TimeSpan.FromMilliseconds(1));
        Assert.Equal(4, GameFixture.Occupied(restored));
    }

    [Fact]
    public void UnstartedAndGameOverStatesRoundTrip()
    {
        var original = new GameEngine(42);
        var restored = new GameEngine();
        restored.RestoreSnapshot(original.SerializeSnapshot());
        Assert.Equal(original.SerializeSnapshot(), restored.SerializeSnapshot());
        original.Start();
        original.Tick(TimeSpan.MaxValue);

        restored.RestoreSnapshot(original.SerializeSnapshot());

        Assert.True(restored.IsGameOver);
        Assert.Equal(original.SerializeSnapshot(), restored.SerializeSnapshot());
    }

    [Theory]
    [InlineData("Version", "2")]
    [InlineData("Board", "[]")]
    [InlineData("Board", "null")]
    [InlineData("Queue", "[]")]
    [InlineData("Queue", "[0,1,2,3,7]")]
    [InlineData("Queue", "null")]
    [InlineData("Active", "null")]
    [InlineData("Active", "{\"Kind\":7,\"Rotation\":0,\"X\":3,\"Y\":0}")]
    [InlineData("Active", "{\"Kind\":1,\"Rotation\":4,\"X\":3,\"Y\":0}")]
    [InlineData("Active", "{\"Kind\":1,\"Rotation\":0,\"X\":2147483647,\"Y\":0}")]
    [InlineData("Active", "{\"Kind\":1,\"Rotation\":0,\"X\":3,\"Y\":-5}")]
    [InlineData("Score", "-1")]
    [InlineData("Lines", "-1")]
    [InlineData("Lines", "-2147483648")]
    [InlineData("RandomState", "0")]
    [InlineData("InitialRandomState", "0")]
    [InlineData("GravityTicks", "10000000")]
    [InlineData("GravityTicks", "-1")]
    [InlineData("LockTicks", "5000000")]
    [InlineData("LockTicks", "-1")]
    [InlineData("LockResets", "16")]
    [InlineData("IsStarted", "false")]
    [InlineData("IsGameOver", "true")]
    public void InvalidSnapshotIsRejectedWithoutChangingTheRunningGame(string property, string value)
    {
        var game = GameFixture.Create(new(Tetromino.T, 0, 3, 5));
        game.Tick(TimeSpan.FromMilliseconds(123));
        var before = game.SerializeSnapshot();
        var malformed = GameFixture.ChangeSnapshot(game, snapshot => snapshot[property] = JsonNode.Parse(value));

        Assert.Throws<ArgumentException>(() => game.RestoreSnapshot(malformed));

        Assert.Equal(before, game.SerializeSnapshot());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(int.MaxValue)]
    public void InvalidBoardColorsAreRejected(int color)
    {
        var game = GameFixture.Create(new(Tetromino.T, 0, 3, 5));
        var before = game.SerializeSnapshot();
        var malformed = GameFixture.ChangeSnapshot(game, snapshot => snapshot["Board"]![199] = color);

        Assert.Throws<ArgumentException>(() => game.RestoreSnapshot(malformed));
        Assert.Equal(before, game.SerializeSnapshot());
    }

    [Fact]
    public void ActivePieceOverlappingTheBoardIsRejected()
    {
        var game = GameFixture.Create(new(Tetromino.T, 0, 3, 5));
        var before = game.SerializeSnapshot();
        var malformed = GameFixture.ChangeSnapshot(game, snapshot => snapshot["Board"]![5 * 10 + 4] = 7);

        Assert.Throws<ArgumentException>(() => game.RestoreSnapshot(malformed));
        Assert.Equal(before, game.SerializeSnapshot());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void MalformedJsonIsRejectedAtomically(string json)
    {
        var game = new GameEngine(42);
        game.Start();
        var before = game.SerializeSnapshot();

        Assert.Throws<ArgumentException>(() => game.RestoreSnapshot(json));

        Assert.Equal(before, game.SerializeSnapshot());
    }
}
