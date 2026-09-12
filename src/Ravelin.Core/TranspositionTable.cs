using System.Runtime.CompilerServices;

namespace Ravelin.Core;

/// <summary>
/// What a stored score tells us about the true value of a position.
/// </summary>
public enum Bound : byte
{
    /// <summary>Nothing stored. Also the marker for an empty slot.</summary>
    None = 0,

    /// <summary>The search returned a value inside the window, so the score is exact.</summary>
    Exact = 1,

    /// <summary>A beta cutoff: the true score is at least this high.</summary>
    Lower = 2,

    /// <summary>No move beat alpha: the true score is at most this high.</summary>
    Upper = 3,
}

/// <summary>
/// A hash table of positions already searched, keyed by Zobrist key. Transpositions are extremely
/// common in chess, so reusing a previous result — or at minimum its best move for ordering —
/// is the single largest gain available to a plain alpha-beta search.
/// </summary>
/// <remarks>
/// Entries are 16 bytes so four share a cache line. The table is a power of two in size and
/// indexed by masking the key, with the full key stored for verification.
/// </remarks>
public sealed class TranspositionTable
{
    public const int DefaultSizeMegabytes = 16;
    public const int MinSizeMegabytes = 1;
    public const int MaxSizeMegabytes = 4096;

    private Entry[] _entries = [];
    private ulong _mask;
    private byte _generation;

    public TranspositionTable(int megabytes = DefaultSizeMegabytes) => Resize(megabytes);

    /// <summary>Number of slots in the table.</summary>
    public int Capacity => _entries.Length;

    /// <summary>
    /// Resizes and clears the table. The slot count is rounded down to a power of two so that
    /// indexing is a mask rather than a division.
    /// </summary>
    public void Resize(int megabytes)
    {
        megabytes = Math.Clamp(megabytes, MinSizeMegabytes, MaxSizeMegabytes);

        long bytes = (long)megabytes * 1024 * 1024;
        long wanted = bytes / Unsafe.SizeOf<Entry>();

        int slots = 1;
        while (slots * 2L <= wanted) slots *= 2;

        _entries = new Entry[slots];
        _mask = (ulong)(slots - 1);
        _generation = 0;
    }

    public void Clear()
    {
        Array.Clear(_entries);
        _generation = 0;
    }

    /// <summary>
    /// Marks the start of a new search. Entries from earlier searches stay readable but become
    /// first in line for replacement.
    /// </summary>
    public void NewSearch() => _generation = (byte)((_generation + 1) & 0x3F);

    /// <summary>
    /// Looks up a position. <paramref name="score"/> is returned relative to the node that is
    /// probing, so mate distances are corrected for the current ply.
    /// </summary>
    public bool TryProbe(ulong key, int ply, out int depth, out int score, out Bound bound, out Move move)
    {
        ref Entry entry = ref _entries[key & _mask];

        if (entry.Bound == Bound.None || entry.Key != key)
        {
            depth = 0;
            score = 0;
            bound = Bound.None;
            move = Move.Null;
            return false;
        }

        depth = entry.Depth;
        bound = entry.Bound;
        move = Move.FromRaw(entry.Move);
        score = ScoreFromTable(entry.Score, ply);
        return true;
    }

    /// <summary>
    /// Stores a result. <paramref name="score"/> is given relative to the storing node; mate
    /// distances are rewritten to be independent of where in the tree this was found.
    /// </summary>
    public void Store(ulong key, int ply, int depth, int score, Bound bound, Move move)
    {
        ref Entry entry = ref _entries[key & _mask];

        // Keep a deeper result from the current search; anything older is fair game. Re-storing
        // the same position always wins, since the newer search knows at least as much.
        bool replace = entry.Bound == Bound.None
                       || entry.Key == key
                       || entry.Generation != _generation
                       || depth >= entry.Depth;

        if (!replace) return;

        // A position whose best move we do not know is still worth storing for its score, but
        // an existing move for the same position is better than none.
        ushort storedMove = move.IsNull && entry.Key == key ? entry.Move : move.Raw;

        entry.Key = key;
        entry.Move = storedMove;
        entry.Score = (short)ScoreToTable(score, ply);
        entry.Depth = (sbyte)Math.Clamp(depth, sbyte.MinValue, sbyte.MaxValue);
        entry.Bound = bound;
        entry.Generation = _generation;
    }

    /// <summary>Approximate fill level in permille, as UCI's <c>hashfull</c> reports it.</summary>
    public int PermilleFull()
    {
        int sampled = Math.Min(1000, _entries.Length);
        if (sampled == 0) return 0;

        int used = 0;
        for (int i = 0; i < sampled; i++)
        {
            if (_entries[i].Bound != Bound.None) used++;
        }

        return used * 1000 / sampled;
    }

    /// <summary>
    /// A mate score means "mate in N plies from the node that found it". Storing it verbatim would
    /// make it wrong for every other node that later reads the same entry at a different depth, so
    /// the distance is made absolute on the way in and relative again on the way out. Getting this
    /// backwards is the classic transposition table bug.
    /// </summary>
    private static int ScoreToTable(int score, int ply)
    {
        if (score >= Search.MateThreshold) return score + ply;
        if (score <= -Search.MateThreshold) return score - ply;
        return score;
    }

    private static int ScoreFromTable(int score, int ply)
    {
        if (score >= Search.MateThreshold) return score - ply;
        if (score <= -Search.MateThreshold) return score + ply;
        return score;
    }

    /// <summary>
    /// 16 bytes: the key, the best move, the score, the depth it was searched to, and a byte
    /// packing the bound with the generation that wrote it.
    /// </summary>
    private struct Entry
    {
        public ulong Key;
        public ushort Move;
        public short Score;
        public sbyte Depth;
        private byte _boundAndGeneration;

        public Bound Bound
        {
            readonly get => (Bound)(_boundAndGeneration & 0x3);
            set => _boundAndGeneration = (byte)((_boundAndGeneration & 0xFC) | (byte)value);
        }

        public byte Generation
        {
            readonly get => (byte)(_boundAndGeneration >> 2);
            set => _boundAndGeneration = (byte)((_boundAndGeneration & 0x3) | (value << 2));
        }
    }
}
