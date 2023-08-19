using ChessChallenge.API;
using System;
using System.Linq;

/*
    Chess Engine made by NotManyIdeas, for the SebLague TinyChessBot Challenge. See it in action at https://chess.stjo.dev/
    NotManyIdeas, 2023 (c)
 
    INFO:
    Tables follow this order: P, N, B, R, Q, K

    ** Search Function **
    alpha = Best already explored option along path to the root for maximizer.
    beta = Best already explored option along path to the root for minimizer.
 */

public class MyBot : IChessBot
{
    enum TTFlags : byte { INVALID, ALL_NODE, CUT_NODE, PV_NODE };
    record struct Transposition(ulong ZobristKey, Move Move, int Evaluation, int Depth, TTFlags Flag);

    Board board;
    Timer timer;
    Move bestMoveRoot;

    Transposition[] TTable = new Transposition[0x700000];
    readonly int[] phase_weight = { 0, 1, 1, 2, 4, 0 };
    private readonly short[] pvm = { 82, 337, 365, 477, 1025, 20000, 94, 281, 297, 512, 936, 20000 };
    private readonly decimal[] PackedPestoTables = {
        63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
        77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
        2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
        77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
        75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
        75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
        73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
        68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
    };

    private readonly int[][] UnpackedPestoTables = new int[64][];
    public Move Think(Board boardInput, Timer timerInput)
    {
        board = boardInput;
        timer = timerInput;

        bestMoveRoot = Move.NullMove;
        for (int depth = 0; depth <= 50; depth++)
        {
            Search(depth, -30000, 30000, 0);
            if (Timeout()) {
                Console.WriteLine($"Depth: {depth}");
                break;
            };
        }

        return bestMoveRoot.IsNull ? board.GetLegalMoves()[0] : bestMoveRoot;
    }

    int Evaluate()
    {
        int middlegame = 0, endgame = 0, gamephase = 0, sideToMove = 2;
        for (; --sideToMove >= 0;)
        {
            for (int piece = -1, square; ++piece < 6;)
                for (ulong mask = board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                {
                    // Gamephase, middlegame -> endgame
                    gamephase += phase_weight[piece];

                    // Material and square evaluation
                    square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                    middlegame += UnpackedPestoTables[square][piece];
                    endgame += UnpackedPestoTables[square][piece + 6];
                }
            middlegame = -middlegame;
            endgame = -endgame;
        }
        return (middlegame * gamephase + endgame * (24 - gamephase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }

    bool Timeout() {
        int timeLeft = timer.MillisecondsRemaining;
        int timeForThisMove = timeLeft / 30 + timer.IncrementMilliseconds / 2;
        if (timeForThisMove >= timeLeft)
            timeForThisMove = timeLeft - 500;
        if (timeForThisMove < 0)
            timeForThisMove = 100;

        return timer.MillisecondsElapsedThisTurn > timeForThisMove;
    } 

    int Search(int depth, int alpha, int beta, int ply)
    {
        ulong zKey = board.ZobristKey;
        bool qSearch = depth <= 0;
        bool notRoot = ply > 0;     
        int bestEvaluation = -30000;

        if (notRoot && board.IsRepeatedPosition())
            return 0;

        Transposition tp = TTable[zKey & 0x6FFFFF];
        if (notRoot && tp.ZobristKey == zKey && tp.Depth >= depth &&
            (tp.Flag == TTFlags.PV_NODE ||
            (tp.Flag == TTFlags.CUT_NODE && tp.Evaluation >= beta) ||
            (tp.Flag == TTFlags.ALL_NODE && tp.Evaluation <= alpha)
        )) return tp.Evaluation;

        int eval = Evaluate();
        if (qSearch)
        {
            bestEvaluation = eval;
            if (bestEvaluation >= beta) return bestEvaluation;
            alpha = Math.Max(alpha, bestEvaluation);
        }

        Move[] moves = board.GetLegalMoves(qSearch);
        OrderMoves(tp, ref moves);

        int originalAlpha = alpha;
        Move bestMove = Move.NullMove;     
        
        foreach (Move move in moves)
        {
            if (Timeout()) return 30000;

            board.MakeMove(move);
            int evaluation = -Search(depth - 1, -beta, -alpha, ply + 1);
            board.UndoMove(move);

            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                bestMove = move;
                if (ply == 0) bestMoveRoot = move;
                alpha = Math.Max(alpha, evaluation);
                if (alpha >= beta) break;
            }
        }

        if (!qSearch && moves.Length == 0) return board.IsInCheck() ? -50000 + ply : 0;
        TTable[zKey & 0x6FFFFF] = new Transposition(zKey, bestMove, bestEvaluation, depth, bestEvaluation >= beta ? TTFlags.CUT_NODE : bestEvaluation > originalAlpha ? TTFlags.ALL_NODE : TTFlags.PV_NODE);
        
        return bestEvaluation;
    }

    void OrderMoves(Transposition tp, ref Move[] moves)
    {
        int[] moveScores = new int[moves.Length];
        for (byte m = 0; ++m < moves.Length;) moveScores[m] = GetMoveScore(tp, moves[m]);
        Array.Sort(moveScores, moves);
        Array.Reverse(moves);
    }

    int GetMoveScore(Transposition tp, Move move)
    {
        if (tp.Move == move && tp.ZobristKey == board.ZobristKey)
            return 999999;
        if (move.IsCapture)
            return 100 * (int)move.CapturePieceType - (int)move.MovePieceType;

        return 0;
    }

    public MyBot()
    {
        UnpackedPestoTables = PackedPestoTables.Select(packedTable =>
        {
            int pieceType = 0;
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(c => BitConverter.GetBytes(c)
                    .Select(square => (int)((sbyte)square * 1.461) + pvm[pieceType++]))
                .ToArray();
        }).ToArray();
    }
}
