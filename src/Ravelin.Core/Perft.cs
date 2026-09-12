namespace Ravelin.Core;

/// <summary>
/// Perft ("performance test") walks the legal move tree to a fixed depth and counts leaf nodes.
/// Comparing those counts against published reference values is the standard correctness gate for
/// move generation: a single mis-generated or missing move at any depth shifts the total.
/// </summary>
public static class Perft
{
    /// <summary>Counts leaf nodes at <paramref name="depth"/> plies. The position is left unchanged.</summary>
    public static long Run(ref Position position, int depth)
    {
        if (depth <= 0) return 1;

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        int count = MoveGenerator.GenerateLegalMoves(ref position, moves);

        // At depth 1 the move count is the node count, so there is no need to make them.
        if (depth == 1) return count;

        long nodes = 0;
        for (int i = 0; i < count; i++)
        {
            Undo undo = position.MakeMove(moves[i]);
            nodes += Run(ref position, depth - 1);
            position.UnmakeMove(moves[i], undo);
        }

        return nodes;
    }

    public static long Run(string fen, int depth)
    {
        Position position = Position.FromFen(fen);
        return Run(ref position, depth);
    }

    /// <summary>
    /// Node counts broken down by first move, in generation order. When a perft total is wrong,
    /// comparing a divide against a reference engine pinpoints which move subtree is at fault.
    /// </summary>
    public static List<(string Move, long Nodes)> Divide(ref Position position, int depth)
    {
        var results = new List<(string Move, long Nodes)>();
        if (depth <= 0) return results;

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        int count = MoveGenerator.GenerateLegalMoves(ref position, moves);

        for (int i = 0; i < count; i++)
        {
            Undo undo = position.MakeMove(moves[i]);
            results.Add((moves[i].ToString(), Run(ref position, depth - 1)));
            position.UnmakeMove(moves[i], undo);
        }

        return results;
    }

    public static List<(string Move, long Nodes)> Divide(string fen, int depth)
    {
        Position position = Position.FromFen(fen);
        return Divide(ref position, depth);
    }
}
