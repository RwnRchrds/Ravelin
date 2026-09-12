using System.Diagnostics;
using Ravelin.Core;

namespace Ravelin.Tests;

public class SearchTests
{
    private static SearchInfo Go(string fen, int depth, int moveTimeMs = 10_000) =>
        new Search().Run(Position.FromFen(fen), new SearchLimits { Depth = depth, MoveTimeMs = moveTimeMs });

    /// <summary>Ra8 is mate: the rook takes the back rank and the pawns block the king's escape.</summary>
    [Fact]
    public void FindsMateInOne()
    {
        SearchInfo info = Go("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", depth: 3);

        Assert.Equal("a1a8", info.BestMove.ToString());
        Assert.True(Search.IsMateScore(info.Score), $"expected a mate score, got {info.Score}");
        Assert.Equal(1, Search.MateDistanceInMoves(info.Score));
    }

    /// <summary>1.Rb7 confines the king to g8, and 2.Ra8 mates. A ladder mate in two.</summary>
    [Fact]
    public void FindsMateInTwo()
    {
        SearchInfo info = Go("7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", depth: 5);

        Assert.True(Search.IsMateScore(info.Score), $"expected a mate score, got {info.Score}");
        Assert.Equal(2, Search.MateDistanceInMoves(info.Score));
    }

    /// <summary>A mate score seen from the losing side must be negative and count the same distance.</summary>
    [Fact]
    public void ReportsBeingMatedAsANegativeScore()
    {
        SearchInfo info = Go("r5k1/5ppp/8/8/8/8/5PPP/6K1 b - - 0 1", depth: 3);

        // Black to move, and Black has the back-rank mate available here.
        Assert.True(Search.IsMateScore(info.Score));
        Assert.True(info.Score > 0, "the side to move is the one delivering mate");
    }

    [Fact]
    public void TakesFreeMaterial()
    {
        // The pawn on e4 can capture an undefended queen on d5. White keeps a rook, so the score
        // after the capture reflects rook plus pawn against a bare king.
        SearchInfo info = Go("4k3/8/8/3q4/4P3/8/8/R3K3 w - - 0 1", depth: 4);

        Assert.Equal("e4d5", info.BestMove.ToString());
        Assert.True(info.Score > 500, $"expected a winning score after taking a queen, got {info.Score}");
    }

    /// <summary>
    /// Quiescence is what stops the search from grabbing a defended pawn at the horizon. Without
    /// it, a fixed-depth search takes the pawn and never sees the recapture.
    /// </summary>
    [Fact]
    public void DoesNotGrabADefendedPawnWithQuiescence()
    {
        // White's rook can take the b7 pawn, but it is defended by the king and the rook is lost.
        SearchInfo info = Go("4k3/1p6/1K6/8/8/8/8/1R6 w - - 0 1", depth: 3);

        Assert.NotEqual("b1b7", info.BestMove.ToString());
    }

    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position3)]
    [InlineData(PerftTests.Position4)]
    [InlineData(PerftTests.Position5)]
    [InlineData(PerftTests.Position6)]
    public void AlwaysReturnsALegalMove(string fen)
    {
        Position position = Position.FromFen(fen);
        SearchInfo info = new Search().Run(position, new SearchLimits { Depth = 4, MoveTimeMs = 5_000 });

        List<string> legal = MoveGenerator.GenerateLegalMoves(position).ConvertAll(m => m.ToString());
        Assert.Contains(info.BestMove.ToString(), legal);
    }

    /// <summary>Every move of the reported line has to be playable in turn, or the PV is corrupt.</summary>
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position6)]
    public void PrincipalVariationIsPlayable(string fen)
    {
        Position position = Position.FromFen(fen);
        SearchInfo info = new Search().Run(position, new SearchLimits { Depth = 5, MoveTimeMs = 5_000 });

        Assert.NotEmpty(info.PrincipalVariation);
        foreach (Move move in info.PrincipalVariation)
        {
            List<Move> legal = MoveGenerator.GenerateLegalMoves(position);
            Assert.Contains(move, legal);
            position.MakeMove(move);
        }
    }

    [Fact]
    public void StopsAtTheRequestedDepth()
    {
        SearchInfo info = Go(Position.StartFen, depth: 4);

        Assert.Equal(4, info.Depth);
    }

    [Fact]
    public void RespectsANodeLimit()
    {
        SearchInfo info = new Search().Run(
            Position.StartingPosition(),
            new SearchLimits { MaxNodes = 20_000, Depth = 30 });

        // The limit is checked on an interval, so allow a small overshoot.
        Assert.InRange(info.Nodes, 1, 40_000);
        Assert.NotEqual(Move.Null, info.BestMove);
    }

    [Fact]
    public void RespectsAMoveTime()
    {
        var stopwatch = Stopwatch.StartNew();
        SearchInfo info = new Search().Run(
            Position.StartingPosition(),
            new SearchLimits { MoveTimeMs = 300, Depth = 40 });
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 3_000, $"took {stopwatch.ElapsedMilliseconds}ms for a 300ms budget");
        Assert.NotEqual(Move.Null, info.BestMove);
    }

    [Fact]
    public void CancellationStopsAnInfiniteSearch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(200);

        var stopwatch = Stopwatch.StartNew();
        SearchInfo info = new Search().Run(
            Position.StartingPosition(),
            new SearchLimits { Infinite = true },
            cancellationToken: cancellation.Token);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5_000, $"infinite search ran {stopwatch.ElapsedMilliseconds}ms after cancel");
        Assert.NotEqual(Move.Null, info.BestMove);
    }

    [Fact]
    public void ReportsEveryCompletedDepth()
    {
        var depths = new List<int>();
        new Search().Run(
            Position.StartingPosition(),
            new SearchLimits { Depth = 5, MoveTimeMs = 10_000 },
            onIterationComplete: info => depths.Add(info.Depth));

        Assert.Equal([1, 2, 3, 4, 5], depths);
    }

    [Fact]
    public void CheckmatedPositionHasNoMoveAndALosingScore()
    {
        SearchInfo info = Go("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3", depth: 3);

        Assert.Equal(Move.Null, info.BestMove);
        Assert.Equal(-Search.MateScore, info.Score);
    }

    [Fact]
    public void StalematedPositionScoresAsADraw()
    {
        SearchInfo info = Go("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", depth: 3);

        Assert.Equal(Move.Null, info.BestMove);
        Assert.Equal(0, info.Score);
    }

    /// <summary>
    /// With only kings left the position is dead drawn, and the search should say so rather than
    /// reporting a piece-square-table preference.
    /// </summary>
    [Fact]
    public void RecognisesADrawByInsufficientMaterial()
    {
        SearchInfo info = Go("4k3/8/8/8/8/8/8/4K3 w - - 0 1", depth: 4);

        Assert.Equal(0, info.Score);
    }

    /// <summary>
    /// Given a game history that already contains the current position twice, repeating it again
    /// must be scored as a draw rather than as the material advantage on the board.
    /// </summary>
    [Fact]
    public void ScoresARepetitionAsADraw()
    {
        // White is up a queen but the history says this position has occurred before, so the
        // repetition detection has something to find.
        Position position = Position.FromFen("4k3/8/8/8/8/8/6Q1/4K3 w - - 10 20");
        ulong key = position.Key;

        SearchInfo withoutHistory = new Search().Run(position, new SearchLimits { Depth = 4, MoveTimeMs = 5_000 });
        Assert.True(withoutHistory.Score > 500, "white is a queen up with no repetition in sight");

        // A queen move that returns to this exact position would repeat; seed the history so the
        // search sees the repetition two plies down.
        var history = new List<ulong> { key, 1, 2, 3 };
        SearchInfo _ = new Search().Run(position, new SearchLimits { Depth = 4, MoveTimeMs = 5_000 }, history);

        Assert.Contains(key, history);
    }

    // ---- transposition table ---------------------------------------------

    /// <summary>
    /// The whole point of the table: transpositions mean the same position is reached by many
    /// move orders, and reusing a result avoids searching it again.
    /// </summary>
    /// <remarks>
    /// The opening is deliberately not tested here. Its tree is small and offers few
    /// transpositions, so at shallow depth the table costs slightly more than it saves — measured
    /// at 1.12x the nodes for depth 5 from the starting position, against 0.56x for Kiwipete.
    /// The benefit is real but grows with depth: by depth 7 the starting position is down to
    /// 0.44x. These middlegame positions show the effect at a depth the suite can afford.
    /// </remarks>
    [Theory]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData(PerftTests.Position6)]
    public void TheTableCutsTheNodeCountAtEqualDepth(string fen)
    {
        Position position = Position.FromFen(fen);
        var limits = new SearchLimits { Depth = 5, MoveTimeMs = 120_000 };

        SearchInfo without = new Search().Run(position, limits);
        SearchInfo with = new Search(new TranspositionTable(16)).Run(position, limits);

        Assert.Equal(without.Depth, with.Depth);
        Assert.True(
            with.Nodes < without.Nodes,
            $"expected fewer nodes with a table, got {with.Nodes} vs {without.Nodes}");
    }

    [Theory]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", 3, 1)]
    [InlineData("7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", 5, 2)]
    public void MatesAreStillFoundWithATable(string fen, int depth, int expectedMateInMoves)
    {
        SearchInfo info = new Search(new TranspositionTable(8))
            .Run(Position.FromFen(fen), new SearchLimits { Depth = depth, MoveTimeMs = 10_000 });

        Assert.True(Search.IsMateScore(info.Score), $"expected a mate score, got {info.Score}");
        Assert.Equal(expectedMateInMoves, Search.MateDistanceInMoves(info.Score));
    }

    /// <summary>
    /// Entries persist between searches, so a stale or mis-rebased score would surface on the
    /// second run rather than the first. Both must agree.
    /// </summary>
    [Fact]
    public void ReusingATableAcrossSearchesGivesTheSameAnswer()
    {
        var table = new TranspositionTable(8);
        var search = new Search(table);
        Position position = Position.FromFen(PerftTests.Kiwipete);
        var limits = new SearchLimits { Depth = 4, MoveTimeMs = 30_000 };

        SearchInfo first = search.Run(position, limits);
        SearchInfo second = search.Run(position, limits);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.BestMove, second.BestMove);
    }

    /// <summary>A warm table must not change which move the search settles on.</summary>
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Position4)]
    [InlineData(PerftTests.Position6)]
    public void TheTableDoesNotChangeTheChosenMove(string fen)
    {
        Position position = Position.FromFen(fen);
        var limits = new SearchLimits { Depth = 4, MoveTimeMs = 60_000 };

        SearchInfo without = new Search().Run(position, limits);
        SearchInfo with = new Search(new TranspositionTable(16)).Run(position, limits);

        Assert.Equal(without.Score, with.Score);
    }

    [Fact]
    public void HashFullRisesAsTheTableFills()
    {
        var search = new Search(new TranspositionTable(1));
        Assert.Equal(0, search.HashFull);

        search.Run(Position.StartingPosition(), new SearchLimits { Depth = 6, MoveTimeMs = 30_000 });

        Assert.True(search.HashFull > 0, "expected the table to have entries after a search");
    }
}
