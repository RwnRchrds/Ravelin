using System.Diagnostics;
using Ravelin.Core;

namespace Ravelin.Uci;

/// <summary>
/// The UCI protocol loop. Reads commands from a <see cref="TextReader"/> and writes responses to a
/// <see cref="TextWriter"/> so it can be driven directly from tests as well as from stdin/stdout.
/// </summary>
/// <remarks>
/// <c>go</c> starts the search on a background task and returns immediately, which is what lets
/// <c>stop</c>, <c>isready</c> and <c>quit</c> be answered while the engine is thinking. Everything
/// written to the output goes through <see cref="Write"/>, since the reader thread and the search
/// thread both produce output.
/// </remarks>
public sealed class UciEngine : IDisposable
{
    public const string Name = "Ravelin";
    public const string Version = "0.2.0";

    /// <summary>Reported in response to <c>uci</c>.</summary>
    public const string Author = "Rowan Richards";

    private static readonly string[] Commands =
    [
        "uci", "debug", "isready", "setoption", "register", "ucinewgame",
        "position", "go", "stop", "ponderhit", "quit", "d",
    ];

    private readonly TextWriter _output;
    private readonly Lock _writeLock = new();
    private readonly Search _search = new();

    /// <summary>Zobrist keys of every position in the game so far, so the search sees repetitions.</summary>
    private readonly List<ulong> _history = [];

    private Position _position = Position.StartingPosition();
    private CancellationTokenSource? _searchCancellation;
    private Task? _searchTask;
    private bool _debug;

    public UciEngine(TextWriter output)
    {
        _output = output;
        ResetGame(Position.StartingPosition());
    }

    /// <summary>The position the engine currently holds. Exposed for testing.</summary>
    public Position Position => _position;

    /// <summary>Reads and executes commands until <c>quit</c> or end of input.</summary>
    public void Run(TextReader input)
    {
        while (input.ReadLine() is { } line)
        {
            if (!Execute(line)) break;
        }

        StopSearch(wait: true);
    }

    /// <summary>Executes one command line. Returns false when the engine should exit.</summary>
    public bool Execute(string line)
    {
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // The spec says to ignore tokens the engine does not understand and parse the rest, so
        // "joho debug on" has to be read as "debug on".
        int start = Array.FindIndex(tokens, token => Array.IndexOf(Commands, token) >= 0);
        if (start < 0) return true;

        string command = tokens[start];
        ReadOnlySpan<string> args = tokens.AsSpan(start + 1);

        switch (command)
        {
            case "uci":
                Identify();
                break;
            case "debug":
                _debug = args.Length == 0 || !args[0].Equals("off", StringComparison.OrdinalIgnoreCase);
                break;
            case "isready":
                // Answered straight away even mid-search: the GUI uses this as a liveness check.
                Write("readyok");
                break;
            case "ucinewgame":
                StopSearch(wait: true);
                ResetGame(Position.StartingPosition());
                break;
            case "position":
                StopSearch(wait: true);
                SetPosition(args);
                break;
            case "go":
                Go(args);
                break;
            case "stop":
                // The search writes its own bestmove as it unwinds, so there is nothing to wait for.
                StopSearch(wait: false);
                break;
            case "d":
                Write(_position.ToString());
                break;
            case "quit":
                StopSearch(wait: true);
                return false;

            // Accepted and ignored: there is no option set, no registration, and pondering is not
            // implemented, so a ponderhit is simply a no-op.
            case "setoption":
            case "register":
            case "ponderhit":
                break;
        }

        return true;
    }

    /// <summary>Blocks until any running search has finished. Used by <c>quit</c> and by tests.</summary>
    public void WaitForSearch(TimeSpan? timeout = null)
    {
        try
        {
            _searchTask?.Wait(timeout ?? TimeSpan.FromSeconds(30));
        }
        catch (AggregateException)
        {
            // A faulted search is reported where it happens; nothing useful to add here.
        }
    }

    public void Dispose() => StopSearch(wait: true);

    private void Write(string text)
    {
        lock (_writeLock)
        {
            _output.WriteLine(text);
        }
    }

    private void Identify()
    {
        Write($"id name {Name} {Version}");
        Write($"id author {Author}");
        Write("uciok");
    }

    private void ResetGame(Position position)
    {
        _position = position;
        _history.Clear();
        _history.Add(position.Key);
    }

    // ---- position --------------------------------------------------------

    private void SetPosition(ReadOnlySpan<string> args)
    {
        if (args.Length == 0) return;

        int index;
        Position parsed;

        if (args[0].Equals("startpos", StringComparison.OrdinalIgnoreCase))
        {
            parsed = Position.StartingPosition();
            index = 1;
        }
        else if (args[0].Equals("fen", StringComparison.OrdinalIgnoreCase))
        {
            // The FEN runs to the "moves" keyword or the end; it is not a fixed field count,
            // since GUIs sometimes omit the halfmove and fullmove counters.
            int end = 1;
            while (end < args.Length && !args[end].Equals("moves", StringComparison.OrdinalIgnoreCase)) end++;

            string fen = string.Join(' ', args[1..end].ToArray());
            try
            {
                parsed = Position.FromFen(fen);
            }
            catch (FormatException e)
            {
                Write($"info string invalid fen: {e.Message}");
                return;
            }

            index = end;
        }
        else
        {
            return;
        }

        // Built up alongside the moves so a rejected command leaves the real history untouched.
        var history = new List<ulong> { parsed.Key };

        if (index < args.Length && args[index].Equals("moves", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = index + 1; i < args.Length; i++)
            {
                if (!MoveNotation.TryParse(ref parsed, args[i], out Move move))
                {
                    // Applying only part of a move list would leave a position neither side meant,
                    // so reject the whole command and keep what we had.
                    Write($"info string illegal move '{args[i]}' in position command");
                    return;
                }

                parsed.MakeMove(move);
                history.Add(parsed.Key);
            }
        }

        _position = parsed;
        _history.Clear();
        _history.AddRange(history);
    }

    // ---- go --------------------------------------------------------------

    private void Go(ReadOnlySpan<string> args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("perft", StringComparison.OrdinalIgnoreCase)) continue;

            if (i + 1 < args.Length && int.TryParse(args[i + 1], out int depth) && depth >= 0)
                RunPerft(depth);
            else
                Write("info string perft needs a non-negative depth");

            return;
        }

        StopSearch(wait: true);

        SearchLimits limits = ParseLimits(args);
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;

        // The search gets its own copy of the position and history, so the reader thread can keep
        // handling commands without either side seeing the other's mutations.
        Position position = _position;
        ulong[] history = [.. _history];

        _searchTask = Task.Run(() =>
        {
            try
            {
                SearchInfo info = _search.Run(position, limits, history, ReportIteration, cancellation.Token);
                Write($"bestmove {(info.BestMove.IsNull ? "0000" : info.BestMove.ToString())}");
            }
            catch (Exception e)
            {
                // A crashed search must still answer, or the GUI waits forever.
                Write($"info string search failed: {e.Message}");
                Write("bestmove 0000");
            }
        });
    }

    private static SearchLimits ParseLimits(ReadOnlySpan<string> args)
    {
        int depth = Search.MaxPly - 1;
        int moveTime = 0, whiteTime = 0, blackTime = 0, whiteIncrement = 0, blackIncrement = 0, movesToGo = 0;
        long nodes = 0;
        bool infinite = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "infinite": infinite = true; break;
                case "depth": depth = ReadInt(args, ref i, depth); break;
                case "movetime": moveTime = ReadInt(args, ref i, moveTime); break;
                case "wtime": whiteTime = ReadInt(args, ref i, whiteTime); break;
                case "btime": blackTime = ReadInt(args, ref i, blackTime); break;
                case "winc": whiteIncrement = ReadInt(args, ref i, whiteIncrement); break;
                case "binc": blackIncrement = ReadInt(args, ref i, blackIncrement); break;
                case "movestogo": movesToGo = ReadInt(args, ref i, movesToGo); break;
                case "nodes": nodes = ReadLong(args, ref i, nodes); break;
            }
        }

        return new SearchLimits
        {
            Depth = Math.Clamp(depth, 1, Search.MaxPly - 1),
            MoveTimeMs = moveTime,
            WhiteTimeMs = whiteTime,
            BlackTimeMs = blackTime,
            WhiteIncrementMs = whiteIncrement,
            BlackIncrementMs = blackIncrement,
            MovesToGo = movesToGo,
            MaxNodes = nodes,
            Infinite = infinite,
        };

        static int ReadInt(ReadOnlySpan<string> args, ref int i, int fallback) =>
            i + 1 < args.Length && int.TryParse(args[i + 1], out int value) ? Advance(ref i, value) : fallback;

        static long ReadLong(ReadOnlySpan<string> args, ref int i, long fallback) =>
            i + 1 < args.Length && long.TryParse(args[i + 1], out long value) ? Advance(ref i, value) : fallback;

        static T Advance<T>(ref int i, T value)
        {
            i++;
            return value;
        }
    }

    private void ReportIteration(SearchInfo info)
    {
        string score = Search.IsMateScore(info.Score)
            ? $"mate {Search.MateDistanceInMoves(info.Score)}"
            : $"cp {info.Score}";

        string line = string.Join(' ', info.PrincipalVariation);

        Write($"info depth {info.Depth} score {score} nodes {info.Nodes} " +
              $"nps {info.NodesPerSecond} time {(long)info.Elapsed.TotalMilliseconds} pv {line}");
    }

    private void StopSearch(bool wait)
    {
        CancellationTokenSource? cancellation = _searchCancellation;
        cancellation?.Cancel();

        if (wait)
        {
            WaitForSearch();
            _searchTask = null;
            _searchCancellation = null;
            cancellation?.Dispose();
        }
    }

    // ---- perft -----------------------------------------------------------

    private void RunPerft(int depth)
    {
        var stopwatch = Stopwatch.StartNew();
        List<(string Move, long Nodes)> divide = Perft.Divide(ref _position, depth);
        stopwatch.Stop();

        long total = 0;
        foreach ((string move, long nodes) in divide)
        {
            Write($"{move}: {nodes}");
            total += nodes;
        }

        // Depth 0 has no moves to divide by, but the node count is still 1.
        if (depth == 0) total = 1;

        Write(string.Empty);
        Write($"Nodes searched: {total}");

        if (_debug)
        {
            double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-6);
            Write($"info string perft {depth} in {stopwatch.ElapsedMilliseconds}ms ({total / seconds / 1e6:F2} Mnps)");
        }
    }
}
