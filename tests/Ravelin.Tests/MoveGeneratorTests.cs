using Ravelin.Core;

namespace Ravelin.Tests;

/// <summary>
/// Targeted coverage for the rules that perft totals prove but do not localise. When a perft
/// number drifts, these narrow the search before a divide is needed.
/// </summary>
public class MoveGeneratorTests
{
    private static List<Move> MovesFrom(string fen) => MoveGenerator.GenerateLegalMoves(Position.FromFen(fen));

    private static List<string> UciMovesFrom(string fen) => MovesFrom(fen).ConvertAll(m => m.ToString());

    [Fact]
    public void StartingPositionHasTwentyMoves()
    {
        List<string> moves = UciMovesFrom(Position.StartFen);

        Assert.Equal(20, moves.Count);
        Assert.Contains("e2e4", moves);
        Assert.Contains("e2e3", moves);
        Assert.Contains("g1f3", moves);
        Assert.Contains("b1a3", moves);
    }

    [Fact]
    public void BothCastlesAreGeneratedWhenTheLanesAreClear()
    {
        List<string> moves = UciMovesFrom("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        Assert.Contains("e1g1", moves);
        Assert.Contains("e1c1", moves);
    }

    [Fact]
    public void CastlingIsBlockedByAPieceInTheLane()
    {
        List<string> moves = UciMovesFrom("r3k2r/8/8/8/8/8/8/R2QK2R w KQkq - 0 1");

        Assert.Contains("e1g1", moves);
        Assert.DoesNotContain("e1c1", moves);
    }

    [Fact]
    public void CastlingThroughAnAttackedSquareIsIllegal()
    {
        // The bishop on g2 covers f1, the square the king would cross going king-side.
        List<string> moves = UciMovesFrom("r3k2r/8/8/8/8/8/6b1/R3K2R w KQkq - 0 1");

        Assert.DoesNotContain("e1g1", moves);
        Assert.Contains("e1c1", moves);
    }

    [Fact]
    public void CastlingOutOfCheckIsIllegal()
    {
        List<string> moves = UciMovesFrom("r3k2r/8/8/8/8/8/4r3/R3K2R w KQkq - 0 1");

        Assert.DoesNotContain("e1g1", moves);
        Assert.DoesNotContain("e1c1", moves);
    }

    [Fact]
    public void CastlingIsNotGeneratedWithoutTheRight()
    {
        List<string> moves = UciMovesFrom("r3k2r/8/8/8/8/8/8/R3K2R w - - 0 1");

        Assert.DoesNotContain("e1g1", moves);
        Assert.DoesNotContain("e1c1", moves);
    }

    [Fact]
    public void CastlingMovesTheRookAsWell()
    {
        Position position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Move castle = MoveGenerator.GenerateLegalMoves(position).Find(m => m.ToString() == "e1c1");

        position.MakeMove(castle);

        Assert.Equal(Piece.WhiteKing, position.PieceAt(Squares.C1));
        Assert.Equal(Piece.WhiteRook, position.PieceAt(Squares.D1));
        Assert.Equal(Piece.None, position.PieceAt(Squares.A1));
        Assert.Equal(Piece.None, position.PieceAt(Squares.E1));
        // Castling forfeits both rights for the side that castled.
        Assert.Equal(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide, position.Castling);
    }

    [Fact]
    public void EnPassantCaptureIsGeneratedAndRemovesTheRightPawn()
    {
        Position position = Position.FromFen("8/8/8/3pP3/8/8/8/K6k w - d6 0 1");
        Move enPassant = MoveGenerator.GenerateLegalMoves(position).Find(m => m.ToString() == "e5d6");

        Assert.True(enPassant.IsEnPassant);

        position.MakeMove(enPassant);

        Assert.Equal(Piece.WhitePawn, position.PieceAt(Squares.D6));   // d6
        Assert.Equal(Piece.None, position.PieceAt(Squares.D5));        // d5, the captured pawn
        Assert.Equal(Piece.None, position.PieceAt(Squares.E5));        // e5
    }

    /// <summary>
    /// The classic en passant trap: capturing would expose the king along the fifth rank, so the
    /// move is pseudo-legal but not legal. Only a make-then-test filter catches this cheaply.
    /// </summary>
    [Fact]
    public void EnPassantIsRejectedWhenItExposesTheKingOnTheRank()
    {
        List<string> moves = UciMovesFrom("8/8/8/K2pP2r/8/8/8/7k w - d6 0 1");

        Assert.DoesNotContain("e5d6", moves);
    }

    [Fact]
    public void DoublePawnPushSetsTheEnPassantSquare()
    {
        Position position = Position.StartingPosition();
        Move push = MoveGenerator.GenerateLegalMoves(position).Find(m => m.ToString() == "e2e4");

        Assert.True(push.IsDoublePawnPush);

        position.MakeMove(push);

        Assert.Equal(Squares.E3, position.EnPassantSquare); // e3
        Assert.Equal(Color.Black, position.SideToMove);
    }

    [Fact]
    public void PromotionGeneratesAllFourPieces()
    {
        List<string> moves = UciMovesFrom("8/P6k/8/8/8/8/8/K7 w - - 0 1");

        Assert.Contains("a7a8q", moves);
        Assert.Contains("a7a8r", moves);
        Assert.Contains("a7a8b", moves);
        Assert.Contains("a7a8n", moves);
    }

    [Fact]
    public void PromotionCaptureGeneratesAllFourPieces()
    {
        List<string> moves = UciMovesFrom("1n5k/P7/8/8/8/8/8/K7 w - - 0 1");

        Assert.Contains("a7b8q", moves);
        Assert.Contains("a7b8r", moves);
        Assert.Contains("a7b8b", moves);
        Assert.Contains("a7b8n", moves);
    }

    [Fact]
    public void PromotionPlacesThePromotedPiece()
    {
        Position position = Position.FromFen("8/P6k/8/8/8/8/8/K7 w - - 0 1");
        Move promotion = MoveGenerator.GenerateLegalMoves(position).Find(m => m.ToString() == "a7a8n");

        position.MakeMove(promotion);

        Assert.Equal(Piece.WhiteKnight, position.PieceAt(Squares.A8));
        Assert.Equal(0UL, position.PiecesOf(Color.White, PieceType.Pawn));
    }

    [Fact]
    public void PinnedPiecesCannotAbandonTheKing()
    {
        // The knight on e2 is pinned to the king on e1 by the rook on e8.
        List<Move> moves = MovesFrom("4r2k/8/8/8/8/8/4N3/4K3 w - - 0 1");

        Assert.DoesNotContain(moves, m => m.From == Squares.E2); // nothing moves off e2
    }

    [Fact]
    public void CheckmateHasNoLegalMoves()
    {
        Position position = Position.FromFen("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");

        Assert.True(position.IsInCheck());
        Assert.Empty(MoveGenerator.GenerateLegalMoves(position));
    }

    [Fact]
    public void StalemateHasNoLegalMovesAndNoCheck()
    {
        Position position = Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.False(position.IsInCheck());
        Assert.Empty(MoveGenerator.GenerateLegalMoves(position));
    }

    [Fact]
    public void CapturingARookRemovesTheMatchingCastlingRight()
    {
        Position position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Move capture = MoveGenerator.GenerateLegalMoves(position).Find(m => m.ToString() == "a1a8");

        position.MakeMove(capture);

        Assert.False(position.Castling.HasFlag(CastlingRights.BlackQueenSide));
        Assert.True(position.Castling.HasFlag(CastlingRights.BlackKingSide));
        Assert.False(position.Castling.HasFlag(CastlingRights.WhiteQueenSide));
    }

    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position4)]
    [InlineData("8/8/8/3pP3/8/8/8/K6k w - d6 0 1")]
    [InlineData("8/P6k/8/8/8/8/8/K7 w - - 0 1")]
    public void MakeThenUnmakeRestoresEveryFieldOfThePosition(string fen)
    {
        Position position = Position.FromFen(fen);

        foreach (Move move in MoveGenerator.GenerateLegalMoves(position))
        {
            Position before = position;
            Undo undo = position.MakeMove(move);
            position.UnmakeMove(move, undo);

            Assert.Equal(before.ToFen(), position.ToFen());
            Assert.Equal(before.AllOccupancy, position.AllOccupancy);
            for (int piece = 0; piece < Pieces.Count; piece++)
                Assert.Equal(before.Bitboards[piece], position.Bitboards[piece]);
        }
    }

    [Fact]
    public void EveryGeneratedMoveLeavesTheOwnKingSafe()
    {
        Position position = Position.FromFen(PerftTests.Kiwipete);
        Color us = position.SideToMove;

        foreach (Move move in MoveGenerator.GenerateLegalMoves(position))
        {
            Undo undo = position.MakeMove(move);
            Assert.False(position.IsInCheck(us), $"{move} leaves the king in check");
            position.UnmakeMove(move, undo);
        }
    }
}
