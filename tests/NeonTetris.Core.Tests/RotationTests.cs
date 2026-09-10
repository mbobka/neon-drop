using NeonTetris.Core;
using Xunit;

namespace NeonTetris.Core.Tests;

public sealed class RotationTests
{
    [Theory]
    [InlineData(Tetromino.I)]
    [InlineData(Tetromino.O)]
    [InlineData(Tetromino.T)]
    [InlineData(Tetromino.S)]
    [InlineData(Tetromino.Z)]
    [InlineData(Tetromino.J)]
    [InlineData(Tetromino.L)]
    public void FourRotationsReturnEveryShapeToItsInitialCells(Tetromino kind)
    {
        var game = GameFixture.Create(new(kind, 0, 3, 5));
        var original = game.Active;
        var originalCells = game.GetCells(original!).ToHashSet();

        for (var rotation = 1; rotation <= 4; rotation++)
        {
            game.Rotate();
            Assert.Equal(rotation % 4, game.Active!.Rotation);
            var cells = game.GetCells(game.Active).ToHashSet();
            Assert.Equal(4, cells.Count);
            Assert.All(cells, cell => Assert.InRange(cell.X, 0, 9));
            if (kind == Tetromino.O)
            {
                Assert.True(originalCells.SetEquals(cells));
            }
        }

        Assert.Equal(original, game.Active);
        Assert.True(originalCells.SetEquals(game.GetCells(game.Active!)));
        game.Rotate(-1);
        Assert.Equal(3, game.Active!.Rotation);
        game.Rotate();
        Assert.Equal(original, game.Active);
    }

    [Theory]
    [InlineData(Tetromino.T, 1, -1, 5, -1, 0, 0, 5)]
    [InlineData(Tetromino.T, 3, 8, 5, 1, 0, 7, 5)]
    [InlineData(Tetromino.T, 0, 3, 18, 1, 1, 2, 17)]
    [InlineData(Tetromino.T, 0, 3, 18, -1, 3, 4, 17)]
    [InlineData(Tetromino.I, 1, -2, 5, -1, 0, 0, 5)]
    [InlineData(Tetromino.I, 3, 8, 5, 1, 0, 6, 5)]
    [InlineData(Tetromino.I, 0, 3, 18, 1, 1, 4, 16)]
    [InlineData(Tetromino.I, 0, 3, 18, -1, 3, 2, 16)]
    public void SrsUsesTheExpectedWallAndFloorKick(Tetromino kind, int rotation, int x, int y,
        int direction, int expectedRotation, int expectedX, int expectedY)
    {
        var game = GameFixture.Create(new(kind, rotation, x, y));

        game.Rotate(direction);

        Assert.Equal(new ActivePiece(kind, expectedRotation, expectedX, expectedY), game.Active);
        Assert.All(game.GetCells(game.Active!), cell =>
        {
            Assert.InRange(cell.X, 0, 9);
            Assert.InRange(cell.Y, 0, 19);
        });
    }

    [Theory]
    [InlineData(Tetromino.J)]
    [InlineData(Tetromino.L)]
    [InlineData(Tetromino.S)]
    [InlineData(Tetromino.Z)]
    public void OtherThreeByThreeShapesUseTheSameLeftWallKick(Tetromino kind)
    {
        var game = GameFixture.Create(new(kind, 1, -1, 5));

        game.Rotate(-1);

        Assert.Equal(new ActivePiece(kind, 0, 0, 5), game.Active);
    }

    [Fact]
    public void RotationIsRejectedWhenAllKickPositionsAreObstructed()
    {
        var piece = new ActivePiece(Tetromino.T, 0, 3, 5);
        var cells = new GameEngine().GetCells(piece).ToHashSet();
        var game = GameFixture.Create(piece, board =>
        {
            for (var y = 2; y < 11; y++)
            {
                for (var x = 0; x < 10; x++)
                {
                    board[x, y] = cells.Contains(new Cell(x, y)) ? 0 : 7;
                }
            }
        });
        var before = game.SerializeSnapshot();

        game.Rotate();
        game.Rotate(-1);

        Assert.Equal(before, game.SerializeSnapshot());
    }

    [Fact]
    public void GetCellsUsesSrsSpawnCoordinatesAndAllowsHiddenCells()
    {
        var game = new GameEngine();

        var cells = game.GetCells(new(Tetromino.T, 0, 3, -1)).ToHashSet();

        Assert.True(cells.SetEquals([new(4, -1), new(3, 0), new(4, 0), new(5, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.GetCells(new((Tetromino)7, 0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.GetCells(new(Tetromino.I, 4, 0, 0)));
    }
}
