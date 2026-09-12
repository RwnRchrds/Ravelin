namespace Ravelin.Core;

/// <summary>Side to move / piece colour. Used as an index, so the numeric values matter.</summary>
public enum Color : byte
{
    White = 0,
    Black = 1,
}

/// <summary>Piece kind, independent of colour. Used as an index into bitboard arrays.</summary>
public enum PieceType : byte
{
    Pawn = 0,
    Knight = 1,
    Bishop = 2,
    Rook = 3,
    Queen = 4,
    King = 5,
    None = 6,
}

/// <summary>
/// Coloured piece, encoded as <c>colour * 6 + type</c> so it indexes
/// <see cref="Position.Pieces"/> directly. <see cref="None"/> is 12.
/// </summary>
public enum Piece : byte
{
    WhitePawn = 0,
    WhiteKnight = 1,
    WhiteBishop = 2,
    WhiteRook = 3,
    WhiteQueen = 4,
    WhiteKing = 5,
    BlackPawn = 6,
    BlackKnight = 7,
    BlackBishop = 8,
    BlackRook = 9,
    BlackQueen = 10,
    BlackKing = 11,
    None = 12,
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    All = WhiteKingSide | WhiteQueenSide | BlackKingSide | BlackQueenSide,
}

public static class ColorExtensions
{
    public static Color Opponent(this Color c) => (Color)((byte)c ^ 1);
}

public static class Pieces
{
    public const int Count = 12;

    public static Piece Make(Color color, PieceType type) => (Piece)((int)color * 6 + (int)type);

    public static Color ColorOf(Piece p) => (Color)((int)p / 6);

    public static PieceType TypeOf(Piece p) => p == Piece.None ? PieceType.None : (PieceType)((int)p % 6);

    /// <summary>FEN letter for a piece: uppercase for White, lowercase for Black.</summary>
    public static char ToChar(Piece p) => p switch
    {
        Piece.WhitePawn => 'P',
        Piece.WhiteKnight => 'N',
        Piece.WhiteBishop => 'B',
        Piece.WhiteRook => 'R',
        Piece.WhiteQueen => 'Q',
        Piece.WhiteKing => 'K',
        Piece.BlackPawn => 'p',
        Piece.BlackKnight => 'n',
        Piece.BlackBishop => 'b',
        Piece.BlackRook => 'r',
        Piece.BlackQueen => 'q',
        Piece.BlackKing => 'k',
        _ => '.',
    };

    public static Piece FromChar(char c) => c switch
    {
        'P' => Piece.WhitePawn,
        'N' => Piece.WhiteKnight,
        'B' => Piece.WhiteBishop,
        'R' => Piece.WhiteRook,
        'Q' => Piece.WhiteQueen,
        'K' => Piece.WhiteKing,
        'p' => Piece.BlackPawn,
        'n' => Piece.BlackKnight,
        'b' => Piece.BlackBishop,
        'r' => Piece.BlackRook,
        'q' => Piece.BlackQueen,
        'k' => Piece.BlackKing,
        _ => Piece.None,
    };
}

/// <summary>
/// Square indexing helpers. Squares are 0..63 with a1 = 0, h1 = 7, a8 = 56, h8 = 63,
/// i.e. <c>square = rank * 8 + file</c> with rank 0 being White's back rank.
/// </summary>
public static class Squares
{
    public const int Count = 64;
    public const int None = -1;

    public const int A1 = 0, B1 = 1, C1 = 2, D1 = 3, E1 = 4, F1 = 5, G1 = 6, H1 = 7;
    public const int A2 = 8, B2 = 9, C2 = 10, D2 = 11, E2 = 12, F2 = 13, G2 = 14, H2 = 15;
    public const int A3 = 16, B3 = 17, C3 = 18, D3 = 19, E3 = 20, F3 = 21, G3 = 22, H3 = 23;
    public const int A4 = 24, B4 = 25, C4 = 26, D4 = 27, E4 = 28, F4 = 29, G4 = 30, H4 = 31;
    public const int A5 = 32, B5 = 33, C5 = 34, D5 = 35, E5 = 36, F5 = 37, G5 = 38, H5 = 39;
    public const int A6 = 40, B6 = 41, C6 = 42, D6 = 43, E6 = 44, F6 = 45, G6 = 46, H6 = 47;
    public const int A7 = 48, B7 = 49, C7 = 50, D7 = 51, E7 = 52, F7 = 53, G7 = 54, H7 = 55;
    public const int A8 = 56, B8 = 57, C8 = 58, D8 = 59, E8 = 60, F8 = 61, G8 = 62, H8 = 63;

    public static int FileOf(int square) => square & 7;

    public static int RankOf(int square) => square >> 3;

    public static int Of(int file, int rank) => rank * 8 + file;

    public static string Name(int square) =>
        square == None ? "-" : string.Concat((char)('a' + FileOf(square)), (char)('1' + RankOf(square)));

    /// <summary>Parses algebraic square notation such as "e4". Returns <see cref="None"/> for "-".</summary>
    public static int Parse(ReadOnlySpan<char> text)
    {
        if (text.Length == 1 && text[0] == '-') return None;
        if (text.Length != 2) throw new FormatException($"Invalid square '{text}'.");

        int file = text[0] - 'a';
        int rank = text[1] - '1';
        if ((uint)file > 7 || (uint)rank > 7) throw new FormatException($"Invalid square '{text}'.");
        return Of(file, rank);
    }
}
