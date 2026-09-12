using System.Runtime.CompilerServices;

namespace Ravelin.Core;

/// <summary>
/// Attack-set generation. Leaper attacks (pawn, knight, king) are precomputed into tables.
/// Slider attacks (bishop, rook, queen) are walked ray-by-ray on demand: correct and simple,
/// and the natural seam to swap for magic bitboards later without touching callers.
/// </summary>
public static class Attacks
{
    /// <summary>Squares a pawn of the given colour attacks from a square. Indexed [colour][square].</summary>
    public static readonly ulong[][] Pawn = new ulong[2][];

    public static readonly ulong[] Knight = new ulong[Squares.Count];
    public static readonly ulong[] King = new ulong[Squares.Count];

    // (file delta, rank delta) pairs.
    private static readonly (int File, int Rank)[] BishopDirections =
        [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    private static readonly (int File, int Rank)[] RookDirections =
        [(1, 0), (-1, 0), (0, 1), (0, -1)];

    static Attacks()
    {
        Pawn[(int)Color.White] = new ulong[Squares.Count];
        Pawn[(int)Color.Black] = new ulong[Squares.Count];

        for (int square = 0; square < Squares.Count; square++)
        {
            ulong bit = Bitboard.Bit(square);

            Pawn[(int)Color.White][square] = Bitboard.NorthEast(bit) | Bitboard.NorthWest(bit);
            Pawn[(int)Color.Black][square] = Bitboard.SouthEast(bit) | Bitboard.SouthWest(bit);

            Knight[square] = StepsFrom(square,
                [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)]);

            King[square] = StepsFrom(square,
                [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)]);
        }
    }

    /// <summary>Collects the on-board destinations reachable by single (file, rank) offsets.</summary>
    private static ulong StepsFrom(int square, ReadOnlySpan<(int File, int Rank)> steps)
    {
        int file = Squares.FileOf(square);
        int rank = Squares.RankOf(square);
        ulong result = 0;

        foreach ((int df, int dr) in steps)
        {
            int f = file + df;
            int r = rank + dr;
            if ((uint)f <= 7 && (uint)r <= 7)
                result |= Bitboard.Bit(Squares.Of(f, r));
        }

        return result;
    }

    /// <summary>
    /// Walks each direction until it runs off the board or hits an occupied square.
    /// The blocking square itself is included, so the result covers captures as well as quiet moves.
    /// </summary>
    private static ulong RayAttacks(int square, ulong occupancy, (int File, int Rank)[] directions)
    {
        int file = Squares.FileOf(square);
        int rank = Squares.RankOf(square);
        ulong result = 0;

        foreach ((int df, int dr) in directions)
        {
            int f = file + df;
            int r = rank + dr;
            while ((uint)f <= 7 && (uint)r <= 7)
            {
                int target = Squares.Of(f, r);
                result |= Bitboard.Bit(target);
                if (Bitboard.IsSet(occupancy, target)) break;
                f += df;
                r += dr;
            }
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bishop(int square, ulong occupancy) => RayAttacks(square, occupancy, BishopDirections);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Rook(int square, ulong occupancy) => RayAttacks(square, occupancy, RookDirections);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Queen(int square, ulong occupancy) => Bishop(square, occupancy) | Rook(square, occupancy);

    /// <summary>Attack set for any piece type from a square, given the current occupancy.</summary>
    public static ulong For(PieceType type, Color color, int square, ulong occupancy) => type switch
    {
        PieceType.Pawn => Pawn[(int)color][square],
        PieceType.Knight => Knight[square],
        PieceType.Bishop => Bishop(square, occupancy),
        PieceType.Rook => Rook(square, occupancy),
        PieceType.Queen => Queen(square, occupancy),
        PieceType.King => King[square],
        _ => 0UL,
    };
}
