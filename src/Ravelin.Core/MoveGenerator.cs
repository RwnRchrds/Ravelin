namespace Ravelin.Core;

/// <summary>
/// Move generation. The strategy here is deliberately correct-first: generate every pseudo-legal
/// move, then filter by making each one and asking whether it leaves our own king attacked.
/// That is slower than tracking pins and check evasions directly, but it is hard to get wrong,
/// which is what the perft suite is there to confirm before search and evaluation land on top.
/// </summary>
public static class MoveGenerator
{
    /// <summary>Upper bound on moves in any legal chess position (the real maximum known is 218).</summary>
    public const int MaxMoves = 256;

    /// <summary>Legal moves for the side to move. Allocates; use the span overload in hot loops.</summary>
    public static List<Move> GenerateLegalMoves(Position position)
    {
        Span<Move> buffer = stackalloc Move[MaxMoves];
        int count = GenerateLegalMoves(ref position, buffer);

        var moves = new List<Move>(count);
        for (int i = 0; i < count; i++) moves.Add(buffer[i]);
        return moves;
    }

    /// <summary>
    /// Writes the legal moves into <paramref name="moves"/> and returns how many were written.
    /// <paramref name="position"/> is restored exactly before returning.
    /// </summary>
    public static int GenerateLegalMoves(ref Position position, Span<Move> moves)
    {
        Span<Move> pseudo = stackalloc Move[MaxMoves];
        int pseudoCount = GeneratePseudoLegalMoves(ref position, pseudo);

        Color us = position.SideToMove;
        Color them = us.Opponent();
        int count = 0;

        for (int i = 0; i < pseudoCount; i++)
        {
            Move move = pseudo[i];
            Undo undo = position.MakeMove(move);

            // The king may have moved, so re-read its square after the move.
            if (!position.IsSquareAttacked(position.KingSquare(us), them))
                moves[count++] = move;

            position.UnmakeMove(move, undo);
        }

        return count;
    }

    /// <summary>
    /// Writes every pseudo-legal move into <paramref name="moves"/> and returns the count. Moves
    /// that leave the mover's king in check are included; castling is the exception, since its
    /// transit-square rules cannot be checked after the fact.
    /// </summary>
    public static int GeneratePseudoLegalMoves(ref Position position, Span<Move> moves)
    {
        int count = 0;
        Color us = position.SideToMove;

        GeneratePawnMoves(ref position, us, moves, ref count);
        GeneratePieceMoves(ref position, us, PieceType.Knight, moves, ref count);
        GeneratePieceMoves(ref position, us, PieceType.Bishop, moves, ref count);
        GeneratePieceMoves(ref position, us, PieceType.Rook, moves, ref count);
        GeneratePieceMoves(ref position, us, PieceType.Queen, moves, ref count);
        GeneratePieceMoves(ref position, us, PieceType.King, moves, ref count);
        GenerateCastlingMoves(ref position, us, moves, ref count);

        return count;
    }

    private static void GeneratePawnMoves(ref Position position, Color us, Span<Move> moves, ref int count)
    {
        ulong pawns = position.PiecesOf(us, PieceType.Pawn);
        if (pawns == 0) return;

        ulong empty = ~position.AllOccupancy;
        ulong enemies = position.OccupancyOf(us.Opponent());
        bool white = us == Color.White;

        // Rank the pawn lands on after a double push, and the rank it promotes on.
        ulong doublePushRank = white ? Bitboard.Rank3 : Bitboard.Rank6;
        ulong promotionRank = white ? Bitboard.Rank8 : Bitboard.Rank1;
        int up = white ? 8 : -8;

        ulong singles = (white ? Bitboard.North(pawns) : Bitboard.South(pawns)) & empty;
        ulong doubles = (white
            ? Bitboard.North(singles & doublePushRank)
            : Bitboard.South(singles & doublePushRank)) & empty;

        ulong quietPushes = singles & ~promotionRank;
        while (quietPushes != 0)
        {
            int to = Bitboard.PopLsb(ref quietPushes);
            moves[count++] = new Move(to - up, to, MoveFlags.Quiet);
        }

        ulong promotionPushes = singles & promotionRank;
        while (promotionPushes != 0)
        {
            int to = Bitboard.PopLsb(ref promotionPushes);
            AddPromotions(to - up, to, capture: false, moves, ref count);
        }

        while (doubles != 0)
        {
            int to = Bitboard.PopLsb(ref doubles);
            moves[count++] = new Move(to - 2 * up, to, MoveFlags.DoublePawnPush);
        }

        // Capture directions, named for White's point of view; the deltas flip for Black.
        ulong capturesEast = (white ? Bitboard.NorthEast(pawns) : Bitboard.SouthEast(pawns)) & enemies;
        ulong capturesWest = (white ? Bitboard.NorthWest(pawns) : Bitboard.SouthWest(pawns)) & enemies;
        int eastDelta = white ? 9 : -7;
        int westDelta = white ? 7 : -9;

        AddPawnCaptures(capturesEast, eastDelta, promotionRank, moves, ref count);
        AddPawnCaptures(capturesWest, westDelta, promotionRank, moves, ref count);

        if (position.EnPassantSquare != Squares.None)
        {
            // Pawns that could capture onto the en passant square are exactly those an enemy pawn
            // standing there would attack.
            ulong capturers = Attacks.Pawn[(int)us.Opponent()][position.EnPassantSquare] & pawns;
            while (capturers != 0)
            {
                int from = Bitboard.PopLsb(ref capturers);
                moves[count++] = new Move(from, position.EnPassantSquare, MoveFlags.EnPassant);
            }
        }
    }

    private static void AddPawnCaptures(ulong targets, int delta, ulong promotionRank, Span<Move> moves, ref int count)
    {
        ulong promotions = targets & promotionRank;
        ulong plain = targets & ~promotionRank;

        while (plain != 0)
        {
            int to = Bitboard.PopLsb(ref plain);
            moves[count++] = new Move(to - delta, to, MoveFlags.Capture);
        }

        while (promotions != 0)
        {
            int to = Bitboard.PopLsb(ref promotions);
            AddPromotions(to - delta, to, capture: true, moves, ref count);
        }
    }

    private static void AddPromotions(int from, int to, bool capture, Span<Move> moves, ref int count)
    {
        int baseFlag = capture ? MoveFlags.KnightPromotionCapture : MoveFlags.KnightPromotion;
        moves[count++] = new Move(from, to, baseFlag + 0); // knight
        moves[count++] = new Move(from, to, baseFlag + 1); // bishop
        moves[count++] = new Move(from, to, baseFlag + 2); // rook
        moves[count++] = new Move(from, to, baseFlag + 3); // queen
    }

    private static void GeneratePieceMoves(
        ref Position position, Color us, PieceType type, Span<Move> moves, ref int count)
    {
        ulong pieces = position.PiecesOf(us, type);
        ulong ours = position.OccupancyOf(us);
        ulong enemies = position.OccupancyOf(us.Opponent());

        while (pieces != 0)
        {
            int from = Bitboard.PopLsb(ref pieces);
            ulong targets = Attacks.For(type, us, from, position.AllOccupancy) & ~ours;

            ulong captures = targets & enemies;
            while (captures != 0)
            {
                int to = Bitboard.PopLsb(ref captures);
                moves[count++] = new Move(from, to, MoveFlags.Capture);
            }

            ulong quiets = targets & ~enemies;
            while (quiets != 0)
            {
                int to = Bitboard.PopLsb(ref quiets);
                moves[count++] = new Move(from, to, MoveFlags.Quiet);
            }
        }
    }

    private static void GenerateCastlingMoves(ref Position position, Color us, Span<Move> moves, ref int count)
    {
        bool white = us == Color.White;
        Color them = us.Opponent();

        CastlingRights kingSide = white ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        CastlingRights queenSide = white ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        if ((position.Castling & (kingSide | queenSide)) == 0) return;

        int kingFrom = white ? Squares.E1 : Squares.E8;
        ulong rooks = position.PiecesOf(us, PieceType.Rook);

        // Castling out of check is illegal, and it is cheaper to test once than per side.
        if (position.IsSquareAttacked(kingFrom, them)) return;

        if ((position.Castling & kingSide) != 0)
        {
            int rookFrom = kingFrom + 3;
            ulong between = Bitboard.Bit(kingFrom + 1) | Bitboard.Bit(kingFrom + 2);
            if ((position.AllOccupancy & between) == 0
                && Bitboard.IsSet(rooks, rookFrom)
                && !position.IsSquareAttacked(kingFrom + 1, them))
            {
                // The destination square is validated by the legality filter in GenerateLegalMoves.
                moves[count++] = new Move(kingFrom, kingFrom + 2, MoveFlags.KingCastle);
            }
        }

        if ((position.Castling & queenSide) != 0)
        {
            int rookFrom = kingFrom - 4;
            ulong between = Bitboard.Bit(kingFrom - 1) | Bitboard.Bit(kingFrom - 2) | Bitboard.Bit(kingFrom - 3);
            if ((position.AllOccupancy & between) == 0
                && Bitboard.IsSet(rooks, rookFrom)
                && !position.IsSquareAttacked(kingFrom - 1, them))
            {
                moves[count++] = new Move(kingFrom, kingFrom - 2, MoveFlags.QueenCastle);
            }
        }
    }
}
