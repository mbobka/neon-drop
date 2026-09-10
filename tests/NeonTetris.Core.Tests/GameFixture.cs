using System.Text.Json.Nodes;
using NeonTetris.Core;

namespace NeonTetris.Core.Tests;

internal static class GameFixture
{
    public static GameEngine Create(ActivePiece piece, Action<int[,]>? arrangeBoard = null, int lines = 0)
    {
        var game = new GameEngine(42);
        game.Start();
        arrangeBoard?.Invoke(game.Board);
        var snapshot = JsonNode.Parse(game.SerializeSnapshot())!.AsObject();
        snapshot["Active"] = new JsonObject
        {
            ["Kind"] = (int)piece.Kind,
            ["Rotation"] = piece.Rotation,
            ["X"] = piece.X,
            ["Y"] = piece.Y
        };
        snapshot["Lines"] = lines;
        game.RestoreSnapshot(snapshot.ToJsonString());
        return game;
    }

    public static string ChangeSnapshot(GameEngine game, Action<JsonObject> change)
    {
        var snapshot = JsonNode.Parse(game.SerializeSnapshot())!.AsObject();
        change(snapshot);
        return snapshot.ToJsonString();
    }

    public static void FillRow(int[,] board, int y, params int[] holes)
    {
        for (var x = 0; x < GameEngine.Width; x++)
        {
            board[x, y] = holes.Contains(x) ? 0 : (int)Tetromino.J + 1;
        }
    }

    public static int Occupied(GameEngine game) => game.Board.Cast<int>().Count(value => value != 0);
}
