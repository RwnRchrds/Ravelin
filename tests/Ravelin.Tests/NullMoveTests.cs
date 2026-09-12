using Ravelin.Core;

namespace Ravelin.Tests;

public class NullMoveTests
{
    [Fact]
    public void PassingFlipsTheSideToMove()
    {
        Position position = Position.StartingPosition();

        position.MakeNullMove();

        Assert.Equal(Color.Black, position.SideToMove);
    }

    /// <summary>The en passant square belongs to the move that created it and cannot survive a pass.</summary>
    [Fact]
    public void PassingClearsTheEnPassantSquare()
    {
        Position position = Position.FromFen("8/8/8/3pP3/8/8/8/K6k w - d6 0 1");

        position.MakeNullMove();

        Assert.Equal(Squares.None, position.EnPassantSquare);
    }

    [Fact]
    public void PassingChangesTheKey()
    {
        Position position = Position.StartingPosition();
        ulong before = position.Key;

        position.MakeNullMove();

        Assert.NotEqual(before, position.Key);
        Assert.Equal(position.ComputeKey(), position.Key);
    }

    /// <summary>
    /// The search passes and un-passes constantly, so anything left behind would corrupt the tree.
    /// </summary>
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position4)]
    [InlineData("8/8/8/3pP3/8/8/8/K6k w - d6 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 b - - 41 97")]
    public void PassingAndUnpassingRestoresEverything(string fen)
    {
        Position position = Position.FromFen(fen);
        ulong key = position.Key;

        Undo undo = position.MakeNullMove();
        position.UnmakeNullMove(undo);

        Assert.Equal(fen, position.ToFen());
        Assert.Equal(key, position.Key);
        Assert.Equal(position.ComputeKey(), position.Key);
    }

    [Fact]
    public void PassingTwiceReturnsToTheOriginalSide()
    {
        Position position = Position.FromFen(PerftTests.Kiwipete);

        Undo first = position.MakeNullMove();
        Undo second = position.MakeNullMove();
        position.UnmakeNullMove(second);
        position.UnmakeNullMove(first);

        Assert.Equal(PerftTests.Kiwipete, position.ToFen());
    }

    /// <summary>
    /// The zugzwang guard. Null move assumes passing is worse than moving, which is exactly false
    /// in king and pawn endings, so those positions must be excluded.
    /// </summary>
    [Theory]
    [InlineData("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", false)]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", false)]
    [InlineData("4k3/8/8/8/8/8/4N3/4K3 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/4B3/4K3 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/4R3/4K3 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/4Q3/4K3 w - - 0 1", true)]
    [InlineData(Position.StartFen, true)]
    public void NonPawnMaterialIsDetected(string fen, bool expected)
    {
        Assert.Equal(expected, Position.FromFen(fen).HasNonPawnMaterial(Color.White));
    }

    [Fact]
    public void EachSideIsAssessedSeparately()
    {
        // White has a rook, Black has only pawns.
        Position position = Position.FromFen("4k3/4p3/8/8/8/8/8/4KR2 w - - 0 1");

        Assert.True(position.HasNonPawnMaterial(Color.White));
        Assert.False(position.HasNonPawnMaterial(Color.Black));
    }

    /// <summary>
    /// Pruning must not cost tactics. These are the same mates the search finds without it.
    /// </summary>
    [Theory]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", 4, 1)]
    [InlineData("7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", 6, 2)]
    public void MatesAreStillFoundWithPruning(string fen, int depth, int expectedMateInMoves)
    {
        SearchInfo info = new Search(new TranspositionTable(8))
            .Run(Position.FromFen(fen), new SearchLimits { Depth = depth, MoveTimeMs = 20_000 });

        Assert.True(Search.IsMateScore(info.Score), $"expected a mate score, got {info.Score}");
        Assert.Equal(expectedMateInMoves, Search.MateDistanceInMoves(info.Score));
    }

    /// <summary>
    /// A king and pawn ending is where the guard earns its keep. The search must still play it
    /// sensibly rather than pruning itself into a wrong answer.
    /// </summary>
    [Fact]
    public void PawnEndingsAreStillSearchedSensibly()
    {
        // White is a pawn up with the opposition and should know it is better.
        Position position = Position.FromFen("8/8/8/4k3/8/4K3/4P3/8 w - - 0 1");
        SearchInfo info = new Search(new TranspositionTable(8))
            .Run(position, new SearchLimits { Depth = 8, MoveTimeMs = 20_000 });

        Assert.Contains(info.BestMove.ToString(), MoveGenerator.GenerateLegalMoves(position).ConvertAll(m => m.ToString()));
        Assert.True(info.Score > 0, $"expected White to be better with an extra pawn, got {info.Score}");
    }
}
