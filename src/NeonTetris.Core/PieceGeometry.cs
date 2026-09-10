namespace NeonTetris.Core;

internal static class PieceGeometry
{
    private static readonly Cell[][][] Shapes = BuildShapes();

    public static Cell[] Shape(Tetromino kind, int rotation) => Shapes[(int)kind][rotation];

    public static Cell[] Kicks(Tetromino kind, int from, int to)
    {
        if (kind == Tetromino.O)
        {
            return [new(0, 0)];
        }

        // Таблицы SRS переведены в координаты экрана: Y растёт вниз.
        if (kind == Tetromino.I)
        {
            return (from, to) switch
            {
                (0, 1) or (3, 2) => [new(0, 0), new(-2, 0), new(1, 0), new(-2, 1), new(1, -2)],
                (1, 0) or (2, 3) => [new(0, 0), new(2, 0), new(-1, 0), new(2, -1), new(-1, 2)],
                (1, 2) or (0, 3) => [new(0, 0), new(-1, 0), new(2, 0), new(-1, -2), new(2, 1)],
                (2, 1) or (3, 0) => [new(0, 0), new(1, 0), new(-2, 0), new(1, 2), new(-2, -1)],
                _ => throw new ArgumentOutOfRangeException(nameof(to))
            };
        }

        return (from, to) switch
        {
            (0, 1) or (2, 1) => [new(0, 0), new(-1, 0), new(-1, -1), new(0, 2), new(-1, 2)],
            (1, 0) or (1, 2) => [new(0, 0), new(1, 0), new(1, 1), new(0, -2), new(1, -2)],
            (2, 3) or (0, 3) => [new(0, 0), new(1, 0), new(1, -1), new(0, 2), new(1, 2)],
            (3, 2) or (3, 0) => [new(0, 0), new(-1, 0), new(-1, 1), new(0, -2), new(-1, -2)],
            _ => throw new ArgumentOutOfRangeException(nameof(to))
        };
    }

    private static Cell[][][] BuildShapes()
    {
        Cell[][] spawn =
        [
            [new(0, 1), new(1, 1), new(2, 1), new(3, 1)],
            [new(1, 0), new(2, 0), new(1, 1), new(2, 1)],
            [new(1, 0), new(0, 1), new(1, 1), new(2, 1)],
            [new(1, 0), new(2, 0), new(0, 1), new(1, 1)],
            [new(0, 0), new(1, 0), new(1, 1), new(2, 1)],
            [new(0, 0), new(0, 1), new(1, 1), new(2, 1)],
            [new(2, 0), new(0, 1), new(1, 1), new(2, 1)]
        ];

        var result = new Cell[7][][];
        for (var kind = 0; kind < result.Length; kind++)
        {
            result[kind] = new Cell[4][];
            result[kind][0] = spawn[kind];
            for (var rotation = 1; rotation < 4; rotation++)
            {
                var size = kind == (int)Tetromino.I ? 4 : 3;
                result[kind][rotation] = kind == (int)Tetromino.O
                    ? spawn[kind]
                    : result[kind][rotation - 1].Select(cell => new Cell(size - 1 - cell.Y, cell.X)).ToArray();
            }
        }

        return result;
    }
}
