using System.Diagnostics;
using Ravelin.Core;
using Ravelin.Uci;

namespace Ravelin.Tests;

public class UciEngineTests
{
    /// <summary>Runs commands against a fresh engine and returns everything it wrote.</summary>
    private static (UciEngine Engine, Func<string> Output) NewEngine()
    {
        var writer = new StringWriter();
        return (new UciEngine(writer), () => writer.ToString());
    }

    private static string Run(params string[] lines)
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        foreach (string line in lines) engine.Execute(line);
        return output();
    }

    // The carriage return has to come off before empty lines are filtered: on Windows the writer
    // emits CRLF, so a blank line splits to "\r", which is not empty and would survive the filter.
    private static string[] Lines(string output) =>
        output.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();

    [Fact]
    public void UciCommandIdentifiesTheEngine()
    {
        string[] lines = Lines(Run("uci"));

        Assert.Equal($"id name {UciEngine.Name} {UciEngine.Version}", lines[0]);
        Assert.StartsWith("id author ", lines[1]);
        Assert.Equal("uciok", lines[^1]);
    }

    [Fact]
    public void IsReadyIsAnswered()
    {
        Assert.Equal("readyok", Lines(Run("isready"))[0]);
    }

    [Fact]
    public void QuitStopsTheLoop()
    {
        (UciEngine engine, _) = NewEngine();

        Assert.True(engine.Execute("isready"));
        Assert.False(engine.Execute("quit"));
    }

    [Fact]
    public void RunStopsReadingAfterQuit()
    {
        var writer = new StringWriter();
        using var engine = new UciEngine(writer);

        engine.Run(new StringReader("isready\nquit\nisready\n"));

        // Only the first isready should have been answered.
        Assert.Single(Lines(writer.ToString()), "readyok");
    }

    [Fact]
    public void UnknownCommandsAreIgnored()
    {
        Assert.Equal(string.Empty, Run("frobnicate the board", ""));
    }

    /// <summary>The spec requires unknown leading tokens to be skipped rather than fail the line.</summary>
    [Fact]
    public void UnknownLeadingTokensAreSkipped()
    {
        Assert.Equal("readyok", Lines(Run("joho isready"))[0]);
    }

    [Fact]
    public void PositionStartposIsTheStartingPosition()
    {
        (UciEngine engine, _) = NewEngine();

        engine.Execute("position startpos");

        Assert.Equal(Position.StartFen, engine.Position.ToFen());
    }

    [Fact]
    public void PositionStartposAppliesTheMoveList()
    {
        (UciEngine engine, _) = NewEngine();

        engine.Execute("position startpos moves e2e4 e7e5 g1f3");

        Assert.Equal("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", engine.Position.ToFen());
    }

    [Fact]
    public void PositionAcceptsAFenString()
    {
        (UciEngine engine, _) = NewEngine();

        engine.Execute($"position fen {PerftTests.Kiwipete}");

        Assert.Equal(PerftTests.Kiwipete, engine.Position.ToFen());
    }

    [Fact]
    public void PositionAcceptsAFenStringWithMoves()
    {
        (UciEngine engine, _) = NewEngine();

        engine.Execute($"position fen {PerftTests.Kiwipete} moves e1g1");

        Assert.Equal(Piece.WhiteKing, engine.Position.PieceAt(Squares.G1));
        Assert.Equal(Piece.WhiteRook, engine.Position.PieceAt(Squares.F1));
    }

    /// <summary>Castling, en passant and promotion all have to survive the notation round trip.</summary>
    [Theory]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1", "r3k2r/8/8/8/8/8/8/2KR3R b kq - 1 1")]
    [InlineData("8/8/8/3pP3/8/8/8/K6k w - d6 0 1", "e5d6", "8/8/3P4/8/8/8/8/K6k b - - 0 1")]
    [InlineData("8/P6k/8/8/8/8/8/K7 w - - 0 1", "a7a8q", "Q7/7k/8/8/8/8/8/K7 b - - 0 1")]
    public void SpecialMovesRoundTripThroughNotation(string fen, string move, string expected)
    {
        (UciEngine engine, _) = NewEngine();

        engine.Execute($"position fen {fen} moves {move}");

        Assert.Equal(expected, engine.Position.ToFen());
    }

    [Fact]
    public void PromotionNotationSelectsTheNamedPiece()
    {
        (UciEngine engine, _) = NewEngine();

        engine.Execute("position fen 8/P6k/8/8/8/8/8/K7 w - - 0 1 moves a7a8n");

        Assert.Equal(Piece.WhiteKnight, engine.Position.PieceAt(Squares.A8));
    }

    [Fact]
    public void AnIllegalMoveRejectsTheWholeCommand()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos moves e2e4");
        string before = engine.Position.ToFen();

        engine.Execute("position startpos moves e2e4 e7e5 e2e4");

        Assert.Contains("info string illegal move 'e2e4'", output());
        Assert.Equal(before, engine.Position.ToFen());
    }

    [Fact]
    public void AnInvalidFenIsReportedAndLeavesThePositionAlone()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos moves e2e4");
        string before = engine.Position.ToFen();

        engine.Execute("position fen not/a/real/fen w - - 0 1");

        Assert.Contains("info string invalid fen", output());
        Assert.Equal(before, engine.Position.ToFen());
    }

    [Fact]
    public void UciNewGameResetsThePosition()
    {
        (UciEngine engine, _) = NewEngine();
        engine.Execute("position startpos moves e2e4 e7e5");

        engine.Execute("ucinewgame");

        Assert.Equal(Position.StartFen, engine.Position.ToFen());
    }

    [Fact]
    public void GoRepliesWithALegalMove()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos");

        engine.Execute("go depth 4");
        engine.WaitForSearch();

        string best = Lines(output())[^1];
        Assert.StartsWith("bestmove ", best);

        string move = best["bestmove ".Length..];
        Assert.Contains(move, MoveGenerator.GenerateLegalMoves(engine.Position).ConvertAll(m => m.ToString()));
    }

    [Fact]
    public void GoRepliesWithTheNullMoveWhenCheckmated()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position fen rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");

        engine.Execute("go depth 3");
        engine.WaitForSearch();

        Assert.Equal("bestmove 0000", Lines(output())[^1]);
    }

    [Fact]
    public void GoPerftDividesAndTotals()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos");

        engine.Execute("go perft 3");

        string[] lines = Lines(output());
        Assert.Equal("Nodes searched: 8902", lines[^1]);

        // One divide line per legal root move, each "<move>: <count>".
        string[] divide = lines[..^1];
        Assert.Equal(20, divide.Length);
        Assert.Contains("e2e4: 600", divide);
        Assert.Equal(8902, divide.Sum(l => long.Parse(l.Split(':')[1])));
    }

    /// <summary>
    /// On Windows the writer emits CRLF. Forcing that here reproduces a platform-specific parsing
    /// bug without needing a Windows runner: splitting on '\n' leaves a stray "\r" for the blank
    /// separator line, which then survives an empty-entry filter.
    /// </summary>
    [Fact]
    public void GoPerftOutputParsesWithWindowsLineEndings()
    {
        var writer = new StringWriter { NewLine = "\r\n" };
        using var engine = new UciEngine(writer);
        engine.Execute("position startpos");

        engine.Execute("go perft 3");

        string[] lines = Lines(writer.ToString());
        Assert.Equal("Nodes searched: 8902", lines[^1]);
        Assert.Equal(20, lines[..^1].Length);
    }

    [Fact]
    public void GoPerftWorksFromAFenPosition()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute($"position fen {PerftTests.Kiwipete}");

        engine.Execute("go perft 3");

        Assert.Equal("Nodes searched: 97862", Lines(output())[^1]);
    }

    [Fact]
    public void GoPerftRejectsAMissingDepth()
    {
        Assert.Contains("info string perft needs", Run("position startpos", "go perft"));
    }

    [Fact]
    public void DisplayCommandRendersTheBoard()
    {
        string output = Run("position startpos", "d");

        Assert.Contains("a b c d e f g h", output);
        Assert.Contains(Position.StartFen, output);
    }

    /// <summary>A full handshake in the order a GUI actually sends it.</summary>
    [Fact]
    public void HandlesATypicalGuiHandshake()
    {
        var writer = new StringWriter();
        using var engine = new UciEngine(writer);

        engine.Run(new StringReader(string.Join('\n',
            "uci",
            "isready",
            "ucinewgame",
            "position startpos moves d2d4 d7d5",
            "go movetime 100",
            "quit")));
        engine.WaitForSearch();

        string[] lines = Lines(writer.ToString());
        Assert.Contains("uciok", lines);
        Assert.Contains("readyok", lines);
        Assert.StartsWith("bestmove ", lines[^1]);
    }

    // ---- search integration ---------------------------------------------

    [Fact]
    public void GoEmitsAnInfoLineForEachCompletedDepth()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos");

        engine.Execute("go depth 4");
        engine.WaitForSearch();

        string[] info = Lines(output()).Where(l => l.StartsWith("info depth")).ToArray();
        Assert.Equal(4, info.Length);

        foreach (string line in info)
        {
            Assert.Matches(@"^info depth \d+ score (cp -?\d+|mate -?\d+) nodes \d+ nps \d+ time \d+ pv \w", line);
        }
    }

    [Fact]
    public void GoReportsAMateScoreInMoves()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position fen 6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1");

        engine.Execute("go depth 3");
        engine.WaitForSearch();

        Assert.Contains("score mate 1", output());
        Assert.Equal("bestmove a1a8", Lines(output())[^1]);
    }

    /// <summary>An infinite search must keep running until stopped, then answer with a move.</summary>
    [Fact]
    public void StopEndsAnInfiniteSearchWithABestmove()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos");
        engine.Execute("go infinite");

        Thread.Sleep(150);
        Assert.DoesNotContain("bestmove", output());

        var stopwatch = Stopwatch.StartNew();
        engine.Execute("stop");
        engine.WaitForSearch();
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5_000, $"stop took {stopwatch.ElapsedMilliseconds}ms");
        Assert.StartsWith("bestmove ", Lines(output())[^1]);
    }

    /// <summary>The reader thread stays responsive while the search thread works.</summary>
    [Fact]
    public void IsReadyIsAnsweredDuringASearch()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos");
        engine.Execute("go infinite");

        var stopwatch = Stopwatch.StartNew();
        engine.Execute("isready");
        stopwatch.Stop();

        Assert.Contains("readyok", output());
        Assert.True(stopwatch.ElapsedMilliseconds < 1_000, $"isready blocked for {stopwatch.ElapsedMilliseconds}ms");

        engine.Execute("stop");
        engine.WaitForSearch();
    }

    [Fact]
    public void GoRespectsAMoveTimeBudget()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos");

        var stopwatch = Stopwatch.StartNew();
        engine.Execute("go movetime 200");
        engine.WaitForSearch();
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5_000, $"a 200ms search took {stopwatch.ElapsedMilliseconds}ms");
        Assert.StartsWith("bestmove ", Lines(output())[^1]);
    }

    /// <summary>
    /// The position command has to hand the search the keys of the whole game, or a repetition
    /// that started before the search began would be invisible to it.
    /// </summary>
    [Fact]
    public void PositionCommandRecordsTheGameHistoryForRepetitionDetection()
    {
        (UciEngine engine, _) = NewEngine();

        engine.Execute("position startpos moves g1f3 g8f6 f3g1 f6g8");

        // Back at the starting position by transposition, so the key must match.
        Assert.Equal(Position.StartingPosition().Key, engine.Position.Key);
    }

    [Fact]
    public void AnotherGoWhileSearchingReplacesTheFirstSearch()
    {
        (UciEngine engine, Func<string> output) = NewEngine();
        engine.Execute("position startpos");
        engine.Execute("go infinite");
        Thread.Sleep(100);

        engine.Execute("go depth 3");
        engine.WaitForSearch();

        // One bestmove per go, and the engine is left idle rather than still thinking.
        Assert.Equal(2, Lines(output()).Count(l => l.StartsWith("bestmove ")));
    }
}
