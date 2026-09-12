using System.Runtime.CompilerServices;

namespace Ravelin.Core;

/// <summary>
/// Move flags occupying bits 12-15 of a packed <see cref="Move"/>. Bit 14 marks a capture and
/// bit 15 marks a promotion, so <c>flags &amp; Capture</c> and <c>flags &amp; Promotion</c> are
/// single-bit tests. The low two bits of a promotion flag select the promoted piece.
/// </summary>
public static class MoveFlags
{
    public const int Quiet = 0;
    public const int DoublePawnPush = 1;
    public const int KingCastle = 2;
    public const int QueenCastle = 3;
    public const int Capture = 4;
    public const int EnPassant = 5;
    public const int KnightPromotion = 8;
    public const int BishopPromotion = 9;
    public const int RookPromotion = 10;
    public const int QueenPromotion = 11;
    public const int KnightPromotionCapture = 12;
    public const int BishopPromotionCapture = 13;
    public const int RookPromotionCapture = 14;
    public const int QueenPromotionCapture = 15;

    public const int CaptureBit = 4;
    public const int PromotionBit = 8;
}

/// <summary>
/// A move packed into 16 bits: destination in bits 0-5, origin in bits 6-11,
/// and a <see cref="MoveFlags"/> value in bits 12-15.
/// </summary>
public readonly struct Move : IEquatable<Move>
{
    private readonly ushort _value;

    /// <summary>The absence of a move, reported over UCI as "0000".</summary>
    public static Move Null => default;

    public Move(int from, int to, int flags)
    {
        _value = (ushort)((flags << 12) | (from << 6) | to);
    }

    private Move(ushort value) => _value = value;

    public static Move FromRaw(ushort raw) => new(raw);

    public ushort Raw
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
    }

    public int To
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value & 0x3F;
    }

    public int From
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_value >> 6) & 0x3F;
    }

    public int Flags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value >> 12;
    }

    public bool IsNull => _value == 0;

    /// <summary>True for ordinary captures, en passant, and promotion captures.</summary>
    public bool IsCapture => (Flags & MoveFlags.CaptureBit) != 0;

    public bool IsPromotion => (Flags & MoveFlags.PromotionBit) != 0;

    public bool IsEnPassant => Flags == MoveFlags.EnPassant;

    public bool IsDoublePawnPush => Flags == MoveFlags.DoublePawnPush;

    public bool IsCastle => Flags is MoveFlags.KingCastle or MoveFlags.QueenCastle;

    /// <summary>The piece a pawn promotes to, or <see cref="PieceType.None"/> for non-promotions.</summary>
    public PieceType PromotionPiece =>
        IsPromotion ? (PieceType)((int)PieceType.Knight + (Flags & 3)) : PieceType.None;

    /// <summary>Long algebraic notation as used by UCI, e.g. "e2e4" or "a7a8q".</summary>
    public override string ToString()
    {
        if (IsNull) return "0000";
        string text = Squares.Name(From) + Squares.Name(To);
        return IsPromotion ? text + char.ToLowerInvariant(Pieces.ToChar(Pieces.Make(Color.Black, PromotionPiece))) : text;
    }

    public bool Equals(Move other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Move other && Equals(other);

    public override int GetHashCode() => _value;

    public static bool operator ==(Move a, Move b) => a._value == b._value;

    public static bool operator !=(Move a, Move b) => a._value != b._value;
}
