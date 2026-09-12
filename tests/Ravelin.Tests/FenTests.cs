using Ravelin.Core;

namespace Ravelin.Tests;

public class FenTests
{
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position3)]
    [InlineData(PerftTests.Position4)]
    [InlineData(PerftTests.Position5)]
    [InlineData(PerftTests.Position6)]
    [InlineData("8/8/8/3pP3/8/8/8/K6k w - d6 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 b - - 99 137")]
    public void FenRoundTrips(string fen)
    {
        Assert.Equal(fen, Position.FromFen(fen).ToFen());
    }

    [Fact]
    public void StartingPositionParsesEveryField()
    {
        Position position = Position.StartingPosition();

        Assert.Equal(Color.White, position.SideToMove);
        Assert.Equal(CastlingRights.All, position.Castling);
        Assert.Equal(Squares.None, position.EnPassantSquare);
        Assert.Equal(0, position.HalfmoveClock);
        Assert.Equal(1, position.FullmoveNumber);
        Assert.Equal(32, Bitboard.PopCount(position.AllOccupancy));
        Assert.Equal(Piece.WhiteKing, position.PieceAt(Squares.E1));
        Assert.Equal(Piece.BlackRook, position.PieceAt(Squares.A8));
        Assert.Equal(Piece.None, position.PieceAt(Squares.E4));
    }

    /// <summary>The hand-written starting masks in <see cref="Bitboard"/> must agree with the FEN parser.</summary>
    [Fact]
    public void StartingPositionMasksMatchTheParsedBoard()
    {
        Position position = Position.StartingPosition();

        for (int piece = 0; piece < Pieces.Count; piece++)
            Assert.Equal(Bitboard.StartingPosition[piece], position.Bitboards[piece]);
    }

    [Fact]
    public void OccupancyStaysConsistentWithThePieceBitboards()
    {
        Position position = Position.FromFen(PerftTests.Kiwipete);

        ulong white = 0, black = 0;
        for (int piece = 0; piece < 6; piece++) white |= position.Bitboards[piece];
        for (int piece = 6; piece < 12; piece++) black |= position.Bitboards[piece];

        Assert.Equal(white, position.OccupancyOf(Color.White));
        Assert.Equal(black, position.OccupancyOf(Color.Black));
        Assert.Equal(white | black, position.AllOccupancy);
        Assert.Equal(0UL, white & black);
    }

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")]              // missing fields
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]          // only 7 ranks
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNRR w KQkq - 0 1")] // rank overflows
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBXR w KQkq - 0 1")] // unknown piece
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")] // bad side to move
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQxq - 0 1")] // bad castling
    public void MalformedFenIsRejected(string fen)
    {
        Assert.Throws<FormatException>(() => Position.FromFen(fen));
    }

    [Theory]
    [InlineData("a1", 0)]
    [InlineData("h1", 7)]
    [InlineData("e4", 28)]
    [InlineData("d6", 43)]
    [InlineData("a8", 56)]
    [InlineData("h8", 63)]
    public void SquaresParseAndNameAgree(string name, int square)
    {
        Assert.Equal(square, Squares.Parse(name));
        Assert.Equal(name, Squares.Name(square));
    }
}
