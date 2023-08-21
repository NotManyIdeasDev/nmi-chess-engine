using ChessChallenge.API;
using System;
using System.Linq;

/*
    Chess Engine made by NotManyIdeas and Ciridev, for the SebLague TinyChessBot Challenge. See it in action at https://chess.stjo.dev/
    NotManyIdeas & Ciridev, 2023 (c)
 
    INFO:
    Tables follow this order: P, N, B, R, Q, K
    TTFlags follow this order: 0 = invalid, 1 = exact, 2 = upperbound, 3 = lowerbound

    ** Search Function **
    alpha = Best already explored option along path to the root for maximizer.
    beta = Best already explored option along path to the root for minimizer.
 */

public class MyBot : IChessBot
{ 
    record struct Transposition(ulong ZobristKey, Move Move, int Evaluation, int Depth, int Flag);

    Board board;
    Timer timer;
    Move bestMoveRoot;
    int debugEval = 0;

    Transposition[] TTable = new Transposition[0x400000];
    int[] mgPieceValues = { 82, 337, 365, 477, 1025, 32000 };
    ulong[,] compressedMGPSTs = {
        { 0x0000000000000000, 0xDDFFECE9F11826EA, 0xE6FCFCF6030321F4, 0xE5FEFB0C11060AE7, 0xF20D0615170C11E9, 0xFA071A1F413819EC, 0x627F3D5F447E22F5, 0x0000000000000000 },
        { 0x97EBC6DFEFE4EDE9, 0xE3CBF4FDFF12F2ED, 0xE9F70C0A131119F0, 0xF304100D1C1315F8, 0xF711133525451216, 0xD13C25415481492C, 0xB7D74824173E07EF, 0x81A7DECF3D9FF195 },
        { 0xDFFDF2EBF3F4D9EB, 0x040F100007152101, 0x000F0F0F0E1B120A, 0xFA0D0D1A220C0A04, 0xFC051332252507FE, 0xF0252B28233225FE, 0xE610EEF31E3B12D1, 0xE304AEDBE7D607F8 },
        { 0xEDF301111007DBE6, 0xD4F0ECF7FF0BFAB9, 0xD3E7F0EF0300FBDF, 0xDCE6F4FF09F906E9, 0xE8F5071A1823F8EC, 0xFB131A24112D3D10, 0x1B203A3E50431A2C, 0x202A20333F091F2B },
        { 0xFFEEF70AF1E7E1CE, 0xDDF80B02080FFD01, 0xF202F5FEFB020E05, 0xF7E6F7F6FEFC03FD, 0xE5E5F0F0FF11FE01, 0xF3EF07081D382F39, 0xE8D9FB01F0391C36, 0xE4001D0C3B2C2B2D },
        { 0xF1240CCA08E4180E, 0x0107F8C0D5F00908, 0xF2F2EAD2D4E2F1E5, 0xCFFFE5D9D2D4DFCD, 0xEFECF4E5E2E7F2DC, 0xF71802F0EC0616EA, 0x1DFFECF9F8FCDAE3, 0xBF1710F1C8DE020D },
    };
  
    public Move Think(Board boardInput, Timer timerInput)
    {
        board = boardInput;
        timer = timerInput;

        bestMoveRoot = Move.NullMove;
        for (int depth = 1; depth <= 50; depth++)
        {
            Search(depth, -30000, 30000, board.IsWhiteToMove ? 1 : -1, 0);
            if (Timeout()) break;
        }

        Console.WriteLine(debugEval / 100f);
        return bestMoveRoot.IsNull ? board.GetLegalMoves()[0] : bestMoveRoot;
    }

    int CheckMaterialAndPosition(bool white)
    {
        int sum = 0;

        for (int i = 0; ++i < 7;)
        {
            foreach (Piece piece in board.GetPieceList((PieceType)i, white))
                sum += mgPieceValues[i - 1] + GetPSTValue(compressedMGPSTs, (PieceType)i, piece.Square.Index, white);
        }

        return sum;
    }

    bool Timeout() => timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 30;
    int Evaluate() => CheckMaterialAndPosition(true) - CheckMaterialAndPosition(false);

    int Search(int depth, int alpha, int beta, int color, int ply)
    {
        ulong zKey = board.ZobristKey;
        bool inCheck = board.IsInCheck();
        bool qSearch = depth <= 0;
        bool notRoot = ply > 0;     
        int bestEvaluation = -999999;

        if (notRoot && board.IsRepeatedPosition())
            return 0;

        Transposition tp = TTable[zKey & 0x3FFFFF];
        if (notRoot && tp.ZobristKey == zKey && tp.Depth >= depth &&
            (tp.Flag == 1 ||
            (tp.Flag == 2 && tp.Evaluation <= alpha) ||
            (tp.Flag == 3 && tp.Evaluation >= beta))
        ) return tp.Evaluation;

        int eval = color * Evaluate();
        if (qSearch)
        {
            bestEvaluation = eval;
            if (bestEvaluation >= beta) return bestEvaluation;
            alpha = Math.Max(alpha, bestEvaluation);
        }

        Move[] moves = board.GetLegalMoves(qSearch && !inCheck).OrderByDescending(move =>
            tp.Move == move && tp.ZobristKey == board.ZobristKey ? 999999 : move.IsCapture ? 100 * (int)move.CapturePieceType - (int)move.MovePieceType : 0
        ).ToArray();

        int originalAlpha = alpha;
        Move bestMove = Move.NullMove;     

        foreach (Move move in moves)
        {
            if (Timeout()) return 999999;

            board.MakeMove(move);
            int evaluation = -Search(depth - 1, -beta, -alpha, -color, ply + 1);
            board.UndoMove(move);
             
            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                bestMove = move;
                if (ply == 0)
                {
                    bestMoveRoot = move;
                    debugEval = bestEvaluation;
                }
                alpha = Math.Max(alpha, evaluation);
                if (alpha >= beta) break;
            }
        }

        if (!qSearch && moves.Length == 0) return inCheck ? -50000 + ply : 0;
        TTable[zKey & 0x3FFFFF] = new Transposition(zKey, bestMove, bestEvaluation, depth, bestEvaluation >= beta ? 2 : bestEvaluation >= originalAlpha ? 1 : 3);
        
        return bestEvaluation;
    }

    int GetPSTValue(ulong[,] compressedPSTs, PieceType pieceType, int square, bool white) => (sbyte)BitConverter.GetBytes(compressedPSTs[(byte)pieceType - 1, white ? (sbyte)(square / 8) : (7 - (sbyte)(square / 8))])[7 - (square % 8)];
}
