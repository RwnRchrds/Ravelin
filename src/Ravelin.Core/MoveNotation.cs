namespace Ravelin.Core;

/// <summary>
/// Conversion between moves and UCI long algebraic notation ("e2e4", "e7e8q", "e1g1").
/// </summary>
public static class MoveNotation
{
    /// <summary>
    /// Resolves UCI notation against a position. The notation carries only the origin, the
    /// destination and an optional promotion piece, so the flags a <see cref="Move"/> needs are
    /// recovered by matching against the legal moves rather than re-derived from the board. That
    /// makes castling, en passant and promotion fall out for free, and rejects illegal input.
    /// </summary>
    public static bool TryParse(ref Position position, ReadOnlySpan<char> text, out Move move)
    {
        move = Move.Null;
        if (text.Length is not (4 or 5)) return false;

        Span<Move> candidates = stackalloc Move[MoveGenerator.MaxMoves];
        int count = MoveGenerator.GenerateLegalMoves(ref position, candidates);

        for (int i = 0; i < count; i++)
        {
            if (text.Equals(candidates[i].ToString(), StringComparison.OrdinalIgnoreCase))
            {
                move = candidates[i];
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves UCI notation, throwing if the move is not legal in this position.</summary>
    public static Move Parse(ref Position position, ReadOnlySpan<char> text)
    {
        if (TryParse(ref position, text, out Move move)) return move;
        throw new FormatException($"'{text}' is not a legal move in position '{position.ToFen()}'.");
    }
}
