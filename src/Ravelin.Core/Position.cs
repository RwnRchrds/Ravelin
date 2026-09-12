using System.Runtime.CompilerServices;
using System.Text;

namespace Ravelin.Core;

/// <summary>One bitboard per <see cref="Piece"/>, indexed by its numeric value.</summary>
[InlineArray(Pieces.Count)]
public struct PieceBitboardArray
{
    private ulong _element0;
}

/// <summary>One occupancy bitboard per <see cref="Color"/>.</summary>
[InlineArray(2)]
public struct ColorBitboardArray
{
    private ulong _element0;
}

/// <summary>Piece-per-square lookup, so <see cref="Position.PieceAt"/> costs one load.</summary>
[InlineArray(Squares.Count)]
public struct MailboxArray
{
    private byte _element0;
}

/// <summary>
/// State captured before a move is made, so <see cref="Position.UnmakeMove"/> can restore what
/// the move itself does not encode.
/// </summary>
public readonly struct Undo(Piece captured, CastlingRights castling, int enPassantSquare, int halfmoveClock, ulong key)
{
    public readonly Piece Captured = captured;
    public readonly CastlingRights Castling = castling;
    public readonly int EnPassantSquare = enPassantSquare;
    public readonly int HalfmoveClock = halfmoveClock;

    /// <summary>
    /// The Zobrist key before the move. Restoring it is cheaper and far less error-prone than
    /// unwinding each XOR the move applied.
    /// </summary>
    public readonly ulong Key = key;
}

/// <summary>
/// A chess position. This is a mutable struct: <see cref="MakeMove"/> updates it in place and
/// returns an <see cref="Undo"/> that <see cref="UnmakeMove"/> consumes to restore it exactly.
/// Pass it by <c>ref</c> in search and perft; copying it by value is legal (all state is inline,
/// no shared references) but wasteful in hot loops.
/// </summary>
public struct Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>
    /// Rights surviving a move touching each square. Applied as
    /// <c>Castling &amp;= mask[from] &amp; mask[to]</c>, which handles king moves, rook moves,
    /// and rooks captured on their home square in one step.
    /// </summary>
    private static readonly byte[] CastlingMask = BuildCastlingMask();

    public PieceBitboardArray Bitboards;
    public ColorBitboardArray Occupancy;
    public ulong AllOccupancy;
    public MailboxArray Mailbox;

    public Color SideToMove;
    public CastlingRights Castling;

    /// <summary>The square a pawn may capture onto, or <see cref="Squares.None"/>.</summary>
    public int EnPassantSquare;

    /// <summary>Plies since the last capture or pawn move, for the fifty-move rule.</summary>
    public int HalfmoveClock;

    public int FullmoveNumber;

    /// <summary>
    /// Zobrist key for the current position, maintained incrementally by <see cref="MakeMove"/>.
    /// Used for repetition detection, and later for transposition table lookups.
    /// </summary>
    public ulong Key;

    private static byte[] BuildCastlingMask()
    {
        var mask = new byte[Squares.Count];
        Array.Fill(mask, (byte)CastlingRights.All);

        mask[Squares.E1] = (byte)(CastlingRights.All & ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide));
        mask[Squares.A1] = (byte)(CastlingRights.All & ~CastlingRights.WhiteQueenSide);
        mask[Squares.H1] = (byte)(CastlingRights.All & ~CastlingRights.WhiteKingSide);
        mask[Squares.E8] = (byte)(CastlingRights.All & ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide));
        mask[Squares.A8] = (byte)(CastlingRights.All & ~CastlingRights.BlackQueenSide);
        mask[Squares.H8] = (byte)(CastlingRights.All & ~CastlingRights.BlackKingSide);

        return mask;
    }

    /// <summary>An empty board with no pieces, White to move and no castling rights.</summary>
    public static Position Empty()
    {
        var position = default(Position);
        for (int square = 0; square < Squares.Count; square++)
            position.Mailbox[square] = (byte)Piece.None;
        position.EnPassantSquare = Squares.None;
        position.FullmoveNumber = 1;
        return position;
    }

    public static Position StartingPosition() => FromFen(StartFen);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Piece PieceAt(int square) => (Piece)Mailbox[square];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ulong PiecesOf(Color color, PieceType type) => Bitboards[(int)Pieces.Make(color, type)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ulong OccupancyOf(Color color) => Occupancy[(int)color];

    public readonly int KingSquare(Color color)
    {
        ulong king = PiecesOf(color, PieceType.King);
        return king == 0 ? Squares.None : Bitboard.LsbIndex(king);
    }

    // ---- Piece placement -------------------------------------------------

    public void AddPiece(Piece piece, int square)
    {
        ulong bit = Bitboard.Bit(square);
        Bitboards[(int)piece] |= bit;
        Occupancy[(int)Pieces.ColorOf(piece)] |= bit;
        AllOccupancy |= bit;
        Mailbox[square] = (byte)piece;
        Key ^= Zobrist.Piece[(int)piece][square];
    }

    public void RemovePiece(Piece piece, int square)
    {
        ulong bit = Bitboard.Bit(square);
        Bitboards[(int)piece] &= ~bit;
        Occupancy[(int)Pieces.ColorOf(piece)] &= ~bit;
        AllOccupancy &= ~bit;
        Mailbox[square] = (byte)Piece.None;
        Key ^= Zobrist.Piece[(int)piece][square];
    }

    public void MovePiece(Piece piece, int from, int to)
    {
        ulong delta = Bitboard.Bit(from) | Bitboard.Bit(to);
        Bitboards[(int)piece] ^= delta;
        Occupancy[(int)Pieces.ColorOf(piece)] ^= delta;
        AllOccupancy ^= delta;
        Mailbox[from] = (byte)Piece.None;
        Mailbox[to] = (byte)piece;
        Key ^= Zobrist.Piece[(int)piece][from] ^ Zobrist.Piece[(int)piece][to];
    }

    // ---- Attack queries --------------------------------------------------

    /// <summary>True if <paramref name="color"/> attacks <paramref name="square"/>, occupied or not.</summary>
    public readonly bool IsSquareAttacked(int square, Color color)
    {
        // A pawn of `color` attacks `square` exactly when a pawn of the other colour standing on
        // `square` would attack that pawn, so the opposing attack table is the right lookup.
        if ((Attacks.Pawn[(int)color.Opponent()][square] & PiecesOf(color, PieceType.Pawn)) != 0) return true;
        if ((Attacks.Knight[square] & PiecesOf(color, PieceType.Knight)) != 0) return true;
        if ((Attacks.King[square] & PiecesOf(color, PieceType.King)) != 0) return true;

        ulong queens = PiecesOf(color, PieceType.Queen);
        if ((Attacks.Bishop(square, AllOccupancy) & (PiecesOf(color, PieceType.Bishop) | queens)) != 0) return true;
        if ((Attacks.Rook(square, AllOccupancy) & (PiecesOf(color, PieceType.Rook) | queens)) != 0) return true;

        return false;
    }

    public readonly bool IsInCheck(Color color)
    {
        int king = KingSquare(color);
        return king != Squares.None && IsSquareAttacked(king, color.Opponent());
    }

    public readonly bool IsInCheck() => IsInCheck(SideToMove);

    // ---- Draw conditions -------------------------------------------------

    /// <summary>True once 50 full moves have passed with no capture and no pawn move.</summary>
    public readonly bool IsFiftyMoveDraw => HalfmoveClock >= 100;

    /// <summary>
    /// True when neither side has enough material to deliver mate: bare kings, king and a single
    /// minor, or bishops confined to one colour complex. King and two knights is excluded, since
    /// mate is possible there even though it cannot be forced.
    /// </summary>
    public readonly bool IsInsufficientMaterial()
    {
        // Any pawn, rook or queen can mate or promote, so material is sufficient by definition.
        if ((PiecesOf(Color.White, PieceType.Pawn) | PiecesOf(Color.Black, PieceType.Pawn)
             | PiecesOf(Color.White, PieceType.Rook) | PiecesOf(Color.Black, PieceType.Rook)
             | PiecesOf(Color.White, PieceType.Queen) | PiecesOf(Color.Black, PieceType.Queen)) != 0)
            return false;

        ulong knights = PiecesOf(Color.White, PieceType.Knight) | PiecesOf(Color.Black, PieceType.Knight);
        ulong bishops = PiecesOf(Color.White, PieceType.Bishop) | PiecesOf(Color.Black, PieceType.Bishop);

        int minors = Bitboard.PopCount(knights | bishops);
        if (minors <= 1) return true;
        if (knights != 0) return false;

        // Bishops that all share a colour complex can never attack the squares the other king uses.
        return (bishops & Bitboard.LightSquares) == 0 || (bishops & Bitboard.DarkSquares) == 0;
    }

    // ---- Zobrist keys ----------------------------------------------------

    /// <summary>
    /// The en passant contribution to the key, which is included only when a pawn can actually
    /// make the capture.
    /// </summary>
    /// <remarks>
    /// FEN records an en passant square after every double push, whether or not a capture is
    /// available. Hashing that square unconditionally would give two otherwise identical positions
    /// different keys, which breaks repetition detection and wastes transposition table entries.
    /// Keying it only when a capture exists makes the hash reflect what is actually playable.
    /// </remarks>
    private readonly ulong EnPassantKey()
    {
        if (EnPassantSquare == Squares.None) return 0;

        ulong capturers = Attacks.Pawn[(int)SideToMove.Opponent()][EnPassantSquare]
                          & PiecesOf(SideToMove, PieceType.Pawn);

        return capturers == 0 ? 0 : Zobrist.EnPassantFile[Squares.FileOf(EnPassantSquare)];
    }

    /// <summary>
    /// Recomputes the key from scratch. <see cref="Key"/> is maintained incrementally, so this is
    /// for initialisation and for asserting the incremental updates stayed in step.
    /// </summary>
    public readonly ulong ComputeKey()
    {
        ulong key = 0;

        for (int piece = 0; piece < Pieces.Count; piece++)
        {
            ulong board = Bitboards[piece];
            while (board != 0) key ^= Zobrist.Piece[piece][Bitboard.PopLsb(ref board)];
        }

        key ^= Zobrist.Castling[(int)Castling];
        if (SideToMove == Color.Black) key ^= Zobrist.BlackToMove;
        key ^= EnPassantKey();

        return key;
    }

    // ---- Make / unmake ---------------------------------------------------

    /// <summary>
    /// Applies a pseudo-legal move in place. The caller is responsible for legality; use the
    /// returned <see cref="Undo"/> with <see cref="UnmakeMove"/> to revert.
    /// </summary>
    public Undo MakeMove(Move move)
    {
        int from = move.From;
        int to = move.To;
        Color us = SideToMove;
        Piece moving = PieceAt(from);
        PieceType movingType = Pieces.TypeOf(moving);
        Piece captured = Piece.None;
        ulong previousKey = Key;

        // Drop the old en passant and castling contributions while the state they describe is
        // still intact; the new ones go back in once the move has been applied.
        Key ^= EnPassantKey();
        Key ^= Zobrist.Castling[(int)Castling];

        if (move.IsEnPassant)
        {
            // The captured pawn sits beside the destination, not on it.
            int capturedSquare = us == Color.White ? to - 8 : to + 8;
            captured = PieceAt(capturedSquare);
            RemovePiece(captured, capturedSquare);
        }
        else if (move.IsCapture)
        {
            captured = PieceAt(to);
            RemovePiece(captured, to);
        }

        var undo = new Undo(captured, Castling, EnPassantSquare, HalfmoveClock, previousKey);

        MovePiece(moving, from, to);

        if (move.IsPromotion)
        {
            RemovePiece(moving, to);
            AddPiece(Pieces.Make(us, move.PromotionPiece), to);
        }
        else if (move.IsCastle)
        {
            bool kingSide = move.Flags == MoveFlags.KingCastle;
            int rookFrom = kingSide ? to + 1 : to - 2;
            int rookTo = kingSide ? to - 1 : to + 1;
            MovePiece(Pieces.Make(us, PieceType.Rook), rookFrom, rookTo);
        }

        Castling &= (CastlingRights)(CastlingMask[from] & CastlingMask[to]);
        EnPassantSquare = move.IsDoublePawnPush ? (from + to) / 2 : Squares.None;
        HalfmoveClock = movingType == PieceType.Pawn || move.IsCapture ? 0 : HalfmoveClock + 1;

        if (us == Color.Black) FullmoveNumber++;
        SideToMove = us.Opponent();

        Key ^= Zobrist.Castling[(int)Castling];
        Key ^= Zobrist.BlackToMove;
        Key ^= EnPassantKey(); // evaluated against the new side to move and the new board

        return undo;
    }

    /// <summary>Reverts the move applied by <see cref="MakeMove"/>, given the <see cref="Undo"/> it returned.</summary>
    public void UnmakeMove(Move move, Undo undo)
    {
        Color us = SideToMove.Opponent();
        SideToMove = us;
        if (us == Color.Black) FullmoveNumber--;

        Castling = undo.Castling;
        EnPassantSquare = undo.EnPassantSquare;
        HalfmoveClock = undo.HalfmoveClock;

        int from = move.From;
        int to = move.To;
        Piece moved = PieceAt(to);

        if (move.IsPromotion)
        {
            RemovePiece(moved, to);
            moved = Pieces.Make(us, PieceType.Pawn);
            AddPiece(moved, to);
        }
        else if (move.IsCastle)
        {
            bool kingSide = move.Flags == MoveFlags.KingCastle;
            int rookFrom = kingSide ? to + 1 : to - 2;
            int rookTo = kingSide ? to - 1 : to + 1;
            MovePiece(Pieces.Make(us, PieceType.Rook), rookTo, rookFrom);
        }

        MovePiece(moved, to, from);

        if (move.IsEnPassant)
        {
            int capturedSquare = us == Color.White ? to - 8 : to + 8;
            AddPiece(undo.Captured, capturedSquare);
        }
        else if (move.IsCapture)
        {
            AddPiece(undo.Captured, to);
        }

        // The placement helpers above have been XOR-ing the piece keys as they go; overwriting
        // with the saved key discards all of that and restores the exact prior value.
        Key = undo.Key;
    }

    // ---- FEN -------------------------------------------------------------

    /// <summary>
    /// Parses Forsyth-Edwards Notation. The halfmove clock and fullmove number are optional and
    /// default to 0 and 1; everything else is required.
    /// </summary>
    public static Position FromFen(string fen)
    {
        ArgumentNullException.ThrowIfNull(fen);

        string[] fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4)
            throw new FormatException($"FEN needs at least 4 fields, got {fields.Length}: '{fen}'.");

        Position position = Empty();

        int rank = 7;
        int file = 0;
        foreach (char c in fields[0])
        {
            if (c == '/')
            {
                if (file != 8) throw new FormatException($"Rank {rank + 1} describes {file} squares, expected 8.");
                rank--;
                file = 0;
                if (rank < 0) throw new FormatException("FEN describes more than 8 ranks.");
            }
            else if (c is >= '1' and <= '8')
            {
                file += c - '0';
                if (file > 8) throw new FormatException($"Rank {rank + 1} overflows past the h-file.");
            }
            else
            {
                Piece piece = Pieces.FromChar(c);
                if (piece == Piece.None) throw new FormatException($"Unknown piece character '{c}' in FEN.");
                if (file > 7) throw new FormatException($"Rank {rank + 1} overflows past the h-file.");
                position.AddPiece(piece, Squares.Of(file, rank));
                file++;
            }
        }

        if (rank != 0 || file != 8) throw new FormatException("FEN placement field does not cover all 64 squares.");

        position.SideToMove = fields[1] switch
        {
            "w" => Color.White,
            "b" => Color.Black,
            _ => throw new FormatException($"Side to move must be 'w' or 'b', got '{fields[1]}'."),
        };

        position.Castling = CastlingRights.None;
        if (fields[2] != "-")
        {
            foreach (char c in fields[2])
            {
                position.Castling |= c switch
                {
                    'K' => CastlingRights.WhiteKingSide,
                    'Q' => CastlingRights.WhiteQueenSide,
                    'k' => CastlingRights.BlackKingSide,
                    'q' => CastlingRights.BlackQueenSide,
                    _ => throw new FormatException($"Unknown castling character '{c}' in FEN."),
                };
            }
        }

        position.EnPassantSquare = Squares.Parse(fields[3]);
        position.HalfmoveClock = fields.Length > 4 ? int.Parse(fields[4]) : 0;
        position.FullmoveNumber = fields.Length > 5 ? int.Parse(fields[5]) : 1;

        position.Key = position.ComputeKey();

        return position;
    }

    /// <summary>Renders the position as FEN. Round-trips with <see cref="FromFen"/>.</summary>
    public readonly string ToFen()
    {
        var sb = new StringBuilder(90);

        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                Piece piece = PieceAt(Squares.Of(file, rank));
                if (piece == Piece.None)
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }
                sb.Append(Pieces.ToChar(piece));
            }

            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }

        sb.Append(SideToMove == Color.White ? " w " : " b ");

        if (Castling == CastlingRights.None)
        {
            sb.Append('-');
        }
        else
        {
            if (Castling.HasFlag(CastlingRights.WhiteKingSide)) sb.Append('K');
            if (Castling.HasFlag(CastlingRights.WhiteQueenSide)) sb.Append('Q');
            if (Castling.HasFlag(CastlingRights.BlackKingSide)) sb.Append('k');
            if (Castling.HasFlag(CastlingRights.BlackQueenSide)) sb.Append('q');
        }

        sb.Append(' ').Append(Squares.Name(EnPassantSquare));
        sb.Append(' ').Append(HalfmoveClock);
        sb.Append(' ').Append(FullmoveNumber);

        return sb.ToString();
    }

    /// <summary>An 8x8 board diagram with the FEN underneath. For debugging and test failure output.</summary>
    public override readonly string ToString()
    {
        var sb = new StringBuilder(256);
        for (int rank = 7; rank >= 0; rank--)
        {
            sb.Append(rank + 1).Append("  ");
            for (int file = 0; file < 8; file++)
                sb.Append(Pieces.ToChar(PieceAt(Squares.Of(file, rank)))).Append(' ');
            sb.Append('\n');
        }
        sb.Append("\n   a b c d e f g h\n\n").Append(ToFen());
        return sb.ToString();
    }
}
