namespace Ravelin.Core;

/// <summary>
/// Zobrist hashing: each position component owns a random 64-bit key, and a position's key is the
/// XOR of the keys for everything present. Because XOR is its own inverse, a move updates the key
/// incrementally by XOR-ing out what it removes and XOR-ing in what it adds.
/// </summary>
public static class Zobrist
{
    /// <summary>Keys indexed by [piece][square].</summary>
    public static readonly ulong[][] Piece = new ulong[Pieces.Count][];

    /// <summary>XOR-ed in when it is Black to move, so the two sides never share a key.</summary>
    public static readonly ulong BlackToMove;

    /// <summary>Keys indexed by the <see cref="CastlingRights"/> bitmask, 0..15.</summary>
    public static readonly ulong[] Castling = new ulong[16];

    /// <summary>Keys indexed by file. Only the file matters, since the rank is implied by the side to move.</summary>
    public static readonly ulong[] EnPassantFile = new ulong[8];

    static Zobrist()
    {
        // A fixed seed keeps keys stable across runs, which matters for reproducing a search and
        // for any future opening book or endgame table keyed on these values.
        ulong state = 0x9E3779B97F4A7C15UL;

        for (int piece = 0; piece < Pieces.Count; piece++)
        {
            Piece[piece] = new ulong[Squares.Count];
            for (int square = 0; square < Squares.Count; square++)
                Piece[piece][square] = Next(ref state);
        }

        BlackToMove = Next(ref state);
        for (int i = 0; i < Castling.Length; i++) Castling[i] = Next(ref state);
        for (int i = 0; i < EnPassantFile.Length; i++) EnPassantFile[i] = Next(ref state);
    }

    /// <summary>SplitMix64 — small, fast, and good enough spectral properties for hash keys.</summary>
    private static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
