namespace Ravelin.Core;

/// <summary>
/// Static evaluation: material plus piece-square tables, scored in centipawns from the side to
/// move's point of view so negamax can negate it directly.
/// </summary>
/// <remarks>
/// The tables are the Chess Programming Wiki's "simplified evaluation function" values. Only the
/// king is tapered between a middlegame and an endgame table, because a single king table is
/// actively wrong at one end or the other: a king that belongs in the corner on move 20 belongs in
/// the centre once the queens come off. Tapering every piece is the obvious next refinement.
/// </remarks>
public static class Evaluation
{
    /// <summary>Centipawn values indexed by <see cref="PieceType"/>. The king is priceless, so it scores 0.</summary>
    public static readonly int[] PieceValues = [100, 320, 330, 500, 900, 0];

    /// <summary>Phase weights per piece type, summing to 24 at the start of a game.</summary>
    private static readonly int[] PhaseWeights = [0, 1, 1, 2, 4, 0];

    private const int MaxPhase = 24;

    // Tables are written rank 8 first so they read like a board, then flipped at startup so that
    // index 0 is a1. Values are from White's point of view.
    private static readonly int[][] MiddlegameTables =
    [
        Flip([ // Pawn
              0,   0,   0,   0,   0,   0,   0,   0,
             50,  50,  50,  50,  50,  50,  50,  50,
             10,  10,  20,  30,  30,  20,  10,  10,
              5,   5,  10,  25,  25,  10,   5,   5,
              0,   0,   0,  20,  20,   0,   0,   0,
              5,  -5, -10,   0,   0, -10,  -5,   5,
              5,  10,  10, -20, -20,  10,  10,   5,
              0,   0,   0,   0,   0,   0,   0,   0,
        ]),
        Flip([ // Knight
            -50, -40, -30, -30, -30, -30, -40, -50,
            -40, -20,   0,   0,   0,   0, -20, -40,
            -30,   0,  10,  15,  15,  10,   0, -30,
            -30,   5,  15,  20,  20,  15,   5, -30,
            -30,   0,  15,  20,  20,  15,   0, -30,
            -30,   5,  10,  15,  15,  10,   5, -30,
            -40, -20,   0,   5,   5,   0, -20, -40,
            -50, -40, -30, -30, -30, -30, -40, -50,
        ]),
        Flip([ // Bishop
            -20, -10, -10, -10, -10, -10, -10, -20,
            -10,   0,   0,   0,   0,   0,   0, -10,
            -10,   0,   5,  10,  10,   5,   0, -10,
            -10,   5,   5,  10,  10,   5,   5, -10,
            -10,   0,  10,  10,  10,  10,   0, -10,
            -10,  10,  10,  10,  10,  10,  10, -10,
            -10,   5,   0,   0,   0,   0,   5, -10,
            -20, -10, -10, -10, -10, -10, -10, -20,
        ]),
        Flip([ // Rook
              0,   0,   0,   0,   0,   0,   0,   0,
              5,  10,  10,  10,  10,  10,  10,   5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
              0,   0,   0,   5,   5,   0,   0,   0,
        ]),
        Flip([ // Queen
            -20, -10, -10,  -5,  -5, -10, -10, -20,
            -10,   0,   0,   0,   0,   0,   0, -10,
            -10,   0,   5,   5,   5,   5,   0, -10,
             -5,   0,   5,   5,   5,   5,   0,  -5,
              0,   0,   5,   5,   5,   5,   0,  -5,
            -10,   5,   5,   5,   5,   5,   0, -10,
            -10,   0,   5,   0,   0,   0,   0, -10,
            -20, -10, -10,  -5,  -5, -10, -10, -20,
        ]),
        Flip([ // King, middlegame: stay tucked away behind the pawns
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -20, -30, -30, -40, -40, -30, -30, -20,
            -10, -20, -20, -20, -20, -20, -20, -10,
             20,  20,   0,   0,   0,   0,  20,  20,
             20,  30,  10,   0,   0,  10,  30,  20,
        ]),
    ];

    private static readonly int[] EndgameKingTable = Flip(
    [   // King, endgame: march to the centre and support the pawns
        -50, -40, -30, -20, -20, -30, -40, -50,
        -30, -20, -10,   0,   0, -10, -20, -30,
        -30, -10,  20,  30,  30,  20, -10, -30,
        -30, -10,  30,  40,  40,  30, -10, -30,
        -30, -10,  30,  40,  40,  30, -10, -30,
        -30, -10,  20,  30,  30,  20, -10, -30,
        -30, -30,   0,   0,   0,   0, -30, -30,
        -50, -30, -30, -30, -30, -30, -30, -50,
    ]);

    /// <summary>Turns a rank-8-first literal into a square-indexed table.</summary>
    private static int[] Flip(int[] readable)
    {
        var table = new int[Squares.Count];
        for (int i = 0; i < Squares.Count; i++)
            table[Squares.Of(i % 8, 7 - i / 8)] = readable[i];
        return table;
    }

    /// <summary>
    /// Scores the position in centipawns, positive meaning the side to move is better.
    /// </summary>
    public static int Evaluate(in Position position)
    {
        int phase = Phase(in position);
        int score = ScoreFor(in position, Color.White, phase) - ScoreFor(in position, Color.Black, phase);

        return position.SideToMove == Color.White ? score : -score;
    }

    /// <summary>24 at the opening material, falling to 0 once only kings and pawns remain.</summary>
    private static int Phase(in Position position)
    {
        int phase = 0;
        for (int type = (int)PieceType.Knight; type <= (int)PieceType.Queen; type++)
        {
            int count = Bitboard.PopCount(
                position.PiecesOf(Color.White, (PieceType)type) | position.PiecesOf(Color.Black, (PieceType)type));
            phase += count * PhaseWeights[type];
        }

        return Math.Min(phase, MaxPhase);
    }

    private static int ScoreFor(in Position position, Color color, int phase)
    {
        int score = 0;
        bool white = color == Color.White;

        for (int type = 0; type <= (int)PieceType.King; type++)
        {
            ulong pieces = position.PiecesOf(color, (PieceType)type);
            score += Bitboard.PopCount(pieces) * PieceValues[type];

            int[] table = MiddlegameTables[type];
            while (pieces != 0)
            {
                int square = Bitboard.PopLsb(ref pieces);

                // Black reads the same tables from the other end of the board.
                int index = white ? square : square ^ 56;

                score += type == (int)PieceType.King
                    ? (table[index] * phase + EndgameKingTable[index] * (MaxPhase - phase)) / MaxPhase
                    : table[index];
            }
        }

        return score;
    }
}
