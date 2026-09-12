using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ravelin.Core;

/// <summary>
/// A bitboard is a plain <see cref="ulong"/> where bit <c>n</c> corresponds to square <c>n</c>
/// (see <see cref="Squares"/>: a1 = bit 0, h8 = bit 63). This class holds the shared masks and
/// the bit-twiddling helpers used throughout move generation.
/// </summary>
public static class Bitboard
{
    public const ulong Empty = 0UL;
    public const ulong Full = ulong.MaxValue;

    public const ulong FileA = 0x0101010101010101UL;
    public const ulong FileB = FileA << 1;
    public const ulong FileC = FileA << 2;
    public const ulong FileD = FileA << 3;
    public const ulong FileE = FileA << 4;
    public const ulong FileF = FileA << 5;
    public const ulong FileG = FileA << 6;
    public const ulong FileH = FileA << 7;

    public const ulong Rank1 = 0x00000000000000FFUL;
    public const ulong Rank2 = Rank1 << (8 * 1);
    public const ulong Rank3 = Rank1 << (8 * 2);
    public const ulong Rank4 = Rank1 << (8 * 3);
    public const ulong Rank5 = Rank1 << (8 * 4);
    public const ulong Rank6 = Rank1 << (8 * 5);
    public const ulong Rank7 = Rank1 << (8 * 6);
    public const ulong Rank8 = Rank1 << (8 * 7);

    /// <summary>Squares where (file + rank) is even, a1 among them.</summary>
    public const ulong DarkSquares = 0xAA55AA55AA55AA55UL;

    public const ulong LightSquares = ~DarkSquares;

    public const ulong NotFileA = ~FileA;
    public const ulong NotFileH = ~FileH;

    /// <summary>Masks indexed by file 0..7 (a..h).</summary>
    public static readonly ulong[] Files = [FileA, FileB, FileC, FileD, FileE, FileF, FileG, FileH];

    /// <summary>Masks indexed by rank 0..7 (rank 1..8).</summary>
    public static readonly ulong[] Ranks = [Rank1, Rank2, Rank3, Rank4, Rank5, Rank6, Rank7, Rank8];

    /// <summary>Starting-position occupancy for each <see cref="Piece"/>, indexed by piece value.</summary>
    public static readonly ulong[] StartingPosition =
    [
        /* WhitePawn   */ Rank2,
        /* WhiteKnight */ Bit(Squares.B1) | Bit(Squares.G1),
        /* WhiteBishop */ Bit(Squares.C1) | Bit(Squares.F1),
        /* WhiteRook   */ Bit(Squares.A1) | Bit(Squares.H1),
        /* WhiteQueen  */ Bit(Squares.D1),
        /* WhiteKing   */ Bit(Squares.E1),
        /* BlackPawn   */ Rank7,
        /* BlackKnight */ Bit(Squares.B8) | Bit(Squares.G8),
        /* BlackBishop */ Bit(Squares.C8) | Bit(Squares.F8),
        /* BlackRook   */ Bit(Squares.A8) | Bit(Squares.H8),
        /* BlackQueen  */ Bit(Squares.D8),
        /* BlackKing   */ Bit(Squares.E8),
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bit(int square) => 1UL << square;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSet(ulong board, int square) => (board & Bit(square)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Set(ulong board, int square) => board | Bit(square);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Clear(ulong board, int square) => board & ~Bit(square);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount(ulong board) => BitOperations.PopCount(board);

    /// <summary>Index of the least significant set bit. Undefined for an empty board.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LsbIndex(ulong board) => BitOperations.TrailingZeroCount(board);

    /// <summary>Removes and returns the least significant set bit's square index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopLsb(ref ulong board)
    {
        int square = BitOperations.TrailingZeroCount(board);
        board &= board - 1;
        return square;
    }

    // Directional shifts. Each masks off the file that would wrap around the board edge.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong North(ulong b) => b << 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong South(ulong b) => b >> 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong East(ulong b) => (b & NotFileH) << 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong West(ulong b) => (b & NotFileA) >> 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong NorthEast(ulong b) => (b & NotFileH) << 9;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong NorthWest(ulong b) => (b & NotFileA) << 7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SouthEast(ulong b) => (b & NotFileH) >> 7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SouthWest(ulong b) => (b & NotFileA) >> 9;

    /// <summary>Renders a bitboard as 8 lines of 8 characters, rank 8 first. For debugging.</summary>
    public static string ToDisplayString(ulong board)
    {
        var sb = new System.Text.StringBuilder(72);
        for (int rank = 7; rank >= 0; rank--)
        {
            for (int file = 0; file < 8; file++)
                sb.Append(IsSet(board, Squares.Of(file, rank)) ? '1' : '.');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
