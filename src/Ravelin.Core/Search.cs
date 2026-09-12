using System.Diagnostics;

namespace Ravelin.Core;

/// <summary>
/// Iterative-deepening negamax with alpha-beta pruning, a quiescence search over tactical moves,
/// and MVV-LVA move ordering. No transposition table yet, so every node is searched afresh.
/// </summary>
public sealed class Search
{
    /// <summary>Maximum plies from the root. Also bounds the check extension.</summary>
    public const int MaxPly = 64;

    /// <summary>Score for mate delivered at the current node; distance is subtracted per ply.</summary>
    public const int MateScore = 30_000;

    /// <summary>Any score at least this large is a forced mate rather than a material judgement.</summary>
    public const int MateThreshold = MateScore - MaxPly;

    private const int Infinity = 31_000;
    private const int DrawScore = 0;

    /// <summary>Nodes between clock checks. Frequent enough to be responsive, rare enough to be cheap.</summary>
    private const int StopCheckInterval = 2048;

    /// <summary>Milliseconds held back so the reply still arrives before the flag falls.</summary>
    private const int MoveOverheadMs = 50;

    private readonly Move[][] _pv = CreatePvTable();
    private readonly int[] _pvLength = new int[MaxPly];
    private readonly Move[] _previousPv = new Move[MaxPly];
    private int _previousPvLength;
    private bool _followPv;

    // Keys of every position along the path from the start of the game to the current node.
    private ulong[] _repetition = new ulong[1024];
    private int _repetitionCount;

    private readonly TranspositionTable? _table;
    private readonly Stopwatch _stopwatch = new();
    private Position _position;
    private SearchLimits _limits = new();
    private CancellationToken _cancellation;
    private long _nodes;
    private bool _aborted;
    private int _softLimitMs;
    private int _hardLimitMs;

    /// <param name="table">
    /// Shared across searches so results carry between moves. Pass null to search without one.
    /// </param>
    public Search(TranspositionTable? table = null) => _table = table;

    /// <summary>Table fill in permille, for UCI's <c>hashfull</c>.</summary>
    public int HashFull => _table?.PermilleFull() ?? 0;

    private static Move[][] CreatePvTable()
    {
        var table = new Move[MaxPly][];
        for (int i = 0; i < MaxPly; i++) table[i] = new Move[MaxPly];
        return table;
    }

    public static bool IsMateScore(int score) => Math.Abs(score) >= MateThreshold;

    /// <summary>Mate distance in moves (not plies), as UCI reports it.</summary>
    public static int MateDistanceInMoves(int score)
    {
        int plies = MateScore - Math.Abs(score);
        int moves = (plies + 1) / 2;
        return score > 0 ? moves : -moves;
    }

    /// <summary>
    /// Searches <paramref name="position"/> and returns the best line found.
    /// </summary>
    /// <param name="gameHistory">
    /// Zobrist keys of the positions already played, oldest first, so repetitions that began before
    /// the search are detected. The current position's key may be included or not.
    /// </param>
    /// <param name="onIterationComplete">Invoked after each completed depth, for UCI info output.</param>
    public SearchInfo Run(
        Position position,
        SearchLimits limits,
        IReadOnlyList<ulong>? gameHistory = null,
        Action<SearchInfo>? onIterationComplete = null,
        CancellationToken cancellationToken = default)
    {
        _position = position;
        _limits = limits;
        _cancellation = cancellationToken;
        _nodes = 0;
        _aborted = false;
        _previousPvLength = 0;
        _stopwatch.Restart();

        _table?.NewSearch();
        SeedRepetitionHistory(gameHistory, position.Key);
        ComputeTimeBudget();

        List<Move> rootMoves = MoveGenerator.GenerateLegalMoves(position);
        if (rootMoves.Count == 0)
        {
            int terminal = position.IsInCheck() ? -MateScore : DrawScore;
            return new SearchInfo(0, terminal, 0, _stopwatch.Elapsed, []);
        }

        // Guarantees a legal reply even if the very first iteration is cut short.
        var best = new SearchInfo(0, 0, 0, TimeSpan.Zero, [rootMoves[0]]);

        int maxDepth = Math.Clamp(limits.Depth, 1, MaxPly - 1);
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            _followPv = true;
            int score = Negamax(depth, -Infinity, Infinity, 0);

            // An aborted iteration has a half-searched root, so its result is discarded.
            if (_aborted) break;

            best = new SearchInfo(depth, score, _nodes, _stopwatch.Elapsed, ExtractPrincipalVariation());
            onIterationComplete?.Invoke(best);

            RememberPrincipalVariation(best.PrincipalVariation);

            // A forced mate will not get better, and there is no point spending the clock on it.
            if (IsMateScore(score)) break;

            // Starting a deeper iteration that cannot finish wastes the remaining time.
            if (!limits.Infinite && _stopwatch.ElapsedMilliseconds >= _softLimitMs) break;

            if (limits.MaxNodes > 0 && _nodes >= limits.MaxNodes) break;
        }

        return best;
    }

    // ---- Core search -----------------------------------------------------

    private int Negamax(int depth, int alpha, int beta, int ply)
    {
        if (_aborted) return DrawScore;

        _pvLength[ply] = 0;

        // The root is the position we have to move in, so it is never a draw claim.
        if (ply > 0 && IsDraw()) return DrawScore;

        if (ply >= MaxPly - 1) return Evaluation.Evaluate(in _position);

        bool inCheck = _position.IsInCheck();

        // Extending checks stops the search from scoring a forcing line as if it were quiet.
        if (inCheck) depth++;

        if (depth <= 0) return Quiescence(alpha, beta, ply);

        if ((++_nodes & (StopCheckInterval - 1)) == 0 && ShouldStop())
        {
            _aborted = true;
            return DrawScore;
        }

        // Transposition probe. The root never takes a cutoff: it has to search its moves to
        // produce a best move and a principal variation to report.
        Move tableMove = Move.Null;
        if (_table is not null && _table.TryProbe(
                _position.Key, ply, out int storedDepth, out int storedScore, out Bound storedBound, out Move storedMove))
        {
            tableMove = storedMove;

            if (ply > 0 && storedDepth >= depth)
            {
                bool cutoff = storedBound switch
                {
                    Bound.Exact => true,
                    Bound.Lower => storedScore >= beta,
                    Bound.Upper => storedScore <= alpha,
                    _ => false,
                };

                if (cutoff) return storedScore;
            }
        }

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        int count = MoveGenerator.GenerateLegalMoves(ref _position, moves);

        // No legal reply means mate or stalemate. Scoring mate by ply makes the search prefer the
        // shortest mate and the longest defence.
        if (count == 0) return inCheck ? -MateScore + ply : DrawScore;

        OrderMoves(moves[..count], ply, tableMove);

        int originalAlpha = alpha;
        int best = -Infinity;
        Move bestMove = Move.Null;

        for (int i = 0; i < count; i++)
        {
            Move move = moves[i];
            Undo undo = _position.MakeMove(move);
            PushRepetition(_position.Key);

            int score = -Negamax(depth - 1, -beta, -alpha, ply + 1);

            _repetitionCount--;
            _position.UnmakeMove(move, undo);

            if (_aborted) return DrawScore;

            if (score <= best) continue;
            best = score;
            bestMove = move;

            if (score > alpha)
            {
                alpha = score;
                UpdatePrincipalVariation(move, ply);
            }

            // The opponent already has a better option earlier in the tree, so this node is moot.
            if (alpha >= beta) break;
        }

        // Which side of the window the result fell on is what makes the score reusable later.
        Bound bound = best <= originalAlpha ? Bound.Upper
            : best >= beta ? Bound.Lower
            : Bound.Exact;

        _table?.Store(_position.Key, ply, depth, best, bound, bestMove);

        return best;
    }

    /// <summary>
    /// Searches only forcing moves, so the static evaluation is never applied to a position with
    /// captures still hanging. Without this the engine trades into losses it cannot see past.
    /// </summary>
    private int Quiescence(int alpha, int beta, int ply)
    {
        if (_aborted) return DrawScore;

        if ((++_nodes & (StopCheckInterval - 1)) == 0 && ShouldStop())
        {
            _aborted = true;
            return DrawScore;
        }

        if (ply >= MaxPly - 1) return Evaluation.Evaluate(in _position);

        bool inCheck = _position.IsInCheck();
        int best;

        if (inCheck)
        {
            // In check there is no option to stand still, so every evasion must be considered.
            best = -Infinity;
        }
        else
        {
            // Standing pat: the side to move can always decline to capture, so the static score is
            // a lower bound on what this node is worth.
            best = Evaluation.Evaluate(in _position);
            if (best >= beta) return best;
            if (best > alpha) alpha = best;
        }

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        int count = MoveGenerator.GenerateLegalMoves(ref _position, moves);

        if (count == 0) return inCheck ? -MateScore + ply : DrawScore;

        if (!inCheck) count = KeepTacticalMoves(moves, count);

        OrderMoves(moves[..count], ply, Move.Null);

        for (int i = 0; i < count; i++)
        {
            Move move = moves[i];
            Undo undo = _position.MakeMove(move);
            int score = -Quiescence(-beta, -alpha, ply + 1);
            _position.UnmakeMove(move, undo);

            if (_aborted) return DrawScore;

            if (score <= best) continue;
            best = score;

            if (score > alpha) alpha = score;
            if (alpha >= beta) break;
        }

        return best;
    }

    /// <summary>Compacts captures and promotions to the front and returns how many there are.</summary>
    private static int KeepTacticalMoves(Span<Move> moves, int count)
    {
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            if (moves[i].IsCapture || moves[i].IsPromotion)
                moves[kept++] = moves[i];
        }

        return kept;
    }

    // ---- Move ordering ---------------------------------------------------

    /// <summary>
    /// Sorts moves so the most likely to cause a cutoff come first, which is what makes alpha-beta
    /// pay: the best move from the previous iteration, then captures by MVV-LVA, then the rest.
    /// </summary>
    private void OrderMoves(Span<Move> moves, int ply, Move tableMove)
    {
        Span<int> scores = stackalloc int[moves.Length];

        Move preferred = Move.Null;
        if (_followPv && ply < _previousPvLength)
        {
            preferred = _previousPv[ply];
        }

        bool foundPreferred = false;
        for (int i = 0; i < moves.Length; i++)
        {
            // The table move is the best this position was known to have, so it goes first.
            if (tableMove != Move.Null && moves[i] == tableMove)
            {
                scores[i] = int.MaxValue;
            }
            else if (preferred != Move.Null && moves[i] == preferred)
            {
                scores[i] = int.MaxValue - 1;
                foundPreferred = true;
            }
            else
            {
                scores[i] = ScoreMove(moves[i]);
            }
        }

        // Once the previous best line diverges there is nothing left to follow.
        if (preferred != Move.Null && !foundPreferred) _followPv = false;

        // Insertion sort: move lists are short, and this keeps equal scores in generation order.
        for (int i = 1; i < moves.Length; i++)
        {
            Move move = moves[i];
            int score = scores[i];
            int j = i - 1;
            while (j >= 0 && scores[j] < score)
            {
                moves[j + 1] = moves[j];
                scores[j + 1] = scores[j];
                j--;
            }
            moves[j + 1] = move;
            scores[j + 1] = score;
        }
    }

    /// <summary>
    /// MVV-LVA: capturing a valuable piece with a cheap one is the most promising thing to try,
    /// so the victim dominates the score and the attacker breaks ties in reverse.
    /// </summary>
    private int ScoreMove(Move move)
    {
        int score = 0;

        if (move.IsCapture)
        {
            // En passant takes a pawn that is not standing on the destination square.
            PieceType victim = move.IsEnPassant
                ? PieceType.Pawn
                : Pieces.TypeOf(_position.PieceAt(move.To));

            PieceType attacker = Pieces.TypeOf(_position.PieceAt(move.From));

            score += 1_000_000
                     + Evaluation.PieceValues[(int)victim] * 16
                     - Evaluation.PieceValues[(int)attacker];
        }

        if (move.IsPromotion) score += 900_000 + Evaluation.PieceValues[(int)move.PromotionPiece];

        return score;
    }

    // ---- Principal variation ---------------------------------------------

    private void UpdatePrincipalVariation(Move move, int ply)
    {
        _pv[ply][0] = move;

        int childLength = _pvLength[ply + 1];
        for (int i = 0; i < childLength; i++) _pv[ply][i + 1] = _pv[ply + 1][i];

        _pvLength[ply] = childLength + 1;
    }

    private Move[] ExtractPrincipalVariation()
    {
        var line = new Move[_pvLength[0]];
        Array.Copy(_pv[0], line, line.Length);
        return line;
    }

    private void RememberPrincipalVariation(IReadOnlyList<Move> line)
    {
        _previousPvLength = Math.Min(line.Count, MaxPly);
        for (int i = 0; i < _previousPvLength; i++) _previousPv[i] = line[i];
    }

    // ---- Draws -----------------------------------------------------------

    private void SeedRepetitionHistory(IReadOnlyList<ulong>? gameHistory, ulong currentKey)
    {
        int needed = (gameHistory?.Count ?? 0) + MaxPly + 8;
        if (_repetition.Length < needed) _repetition = new ulong[needed * 2];

        _repetitionCount = 0;
        if (gameHistory is not null)
        {
            foreach (ulong key in gameHistory) _repetition[_repetitionCount++] = key;
        }

        // The history may or may not already end with the position we are about to search.
        if (_repetitionCount == 0 || _repetition[_repetitionCount - 1] != currentKey)
            _repetition[_repetitionCount++] = currentKey;
    }

    private void PushRepetition(ulong key)
    {
        if (_repetitionCount == _repetition.Length) Array.Resize(ref _repetition, _repetition.Length * 2);
        _repetition[_repetitionCount++] = key;
    }

    private bool IsDraw() =>
        _position.IsFiftyMoveDraw || IsRepetition() || _position.IsInsufficientMaterial();

    /// <summary>
    /// A single repetition inside the tree is treated as a draw. Waiting for a third occurrence
    /// would let the search walk into a repetition believing it is still winning.
    /// </summary>
    private bool IsRepetition()
    {
        ulong key = _position.Key;

        // Anything before the last irreversible move cannot recur, and the halfmove clock counts
        // exactly the plies since then.
        int earliest = Math.Max(0, _repetitionCount - 1 - _position.HalfmoveClock);

        // The same position implies the same side to move, so only every other ply can match.
        for (int i = _repetitionCount - 3; i >= earliest; i -= 2)
        {
            if (_repetition[i] == key) return true;
        }

        return false;
    }

    // ---- Time management -------------------------------------------------

    private void ComputeTimeBudget()
    {
        _softLimitMs = int.MaxValue;
        _hardLimitMs = int.MaxValue;

        if (_limits.Infinite) return;

        if (_limits.MoveTimeMs > 0)
        {
            _softLimitMs = _limits.MoveTimeMs;
            _hardLimitMs = _limits.MoveTimeMs;
            return;
        }

        bool white = _position.SideToMove == Color.White;
        int remaining = white ? _limits.WhiteTimeMs : _limits.BlackTimeMs;
        int increment = white ? _limits.WhiteIncrementMs : _limits.BlackIncrementMs;

        if (remaining <= 0) return;

        // Without a movestogo the clock is sudden death, so budget as if the game has a way to run.
        int movesToGo = _limits.MovesToGo > 0 ? _limits.MovesToGo : 30;
        int budget = remaining / movesToGo + increment * 3 / 4;

        // Never commit the whole clock: the reply still has to reach the GUI.
        int ceiling = Math.Max(1, remaining - MoveOverheadMs);

        _softLimitMs = Math.Min(budget, ceiling);

        // The hard limit allows an iteration already in progress to overshoot the soft budget.
        _hardLimitMs = Math.Min(budget * 3, ceiling);
    }

    private bool ShouldStop()
    {
        if (_cancellation.IsCancellationRequested) return true;
        if (_limits.MaxNodes > 0 && _nodes >= _limits.MaxNodes) return true;
        return _stopwatch.ElapsedMilliseconds >= _hardLimitMs;
    }
}
