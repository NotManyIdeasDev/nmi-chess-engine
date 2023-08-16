using ChessChallenge.API;
using System;
using System.Collections.Generic;

public enum TTFlags : byte { INVALID = 0, PV_NODE = 1, CUT_NODE = 2, ALL_NODE = 3 };
public struct Transposition {
    public ulong zobristKey;
    public Move move;
    public int evaluation;
    public byte depth;
    public TTFlags flag;

    public Transposition(ulong z, Move m, int e, byte d, TTFlags f)
    {
        zobristKey = z;
        move = m;
        evaluation = e;
        depth = d;
        flag = f;
    }
}

public class MyBot : IChessBot
{
    const int infinity = 99999999;
    int[] mgPieceValues = { 82, 337, 365, 477, 1025, 32000 }; //P, N, B, R, Q, K
    ulong[,] compressedMGPSTs = {
        { 0x7F7F7F7F7F7F7F7F, 0xDDFFECE9F11826EA, 0xE6FCFCF6030321F4, 0xE5FEFB0C11060AE7, 0xF20D0615170C11E9, 0xFA071A1F413819EC, 0x627F3D5F447E22F5, 0x0000000000000000 },
        { 0x97EBC6DFEFE4EDE9, 0xE3CBF4FDFF12F2ED, 0xE9F70C0A131119F0, 0xF304100D1C1315F8, 0xF711133525451216, 0xD13C25415481492C, 0xB7D74824173E07EF, 0x81A7DECF3D9FF195 },
        { 0xDFFDF2EBF3F4D9EB, 0x040F100007152101, 0x000F0F0F0E1B120A, 0xFA0D0D1A220C0A04, 0xFC051332252507FE, 0xF0252B28233225FE, 0xE610EEF31E3B12D1, 0xE304AEDBE7D607F8 },
        { 0xEDF301111007DBE6, 0xD4F0ECF7FF0BFAB9, 0xD3E7F0EF0300FBDF, 0xDCE6F4FF09F906E9, 0xE8F5071A1823F8EC, 0xFB131A24112D3D10, 0x1B203A3E50431A2C, 0x202A20333F091F2B },
        { 0xFFEEF70AF1E7E1CE, 0xDDF80B02080FFD01, 0xF202F5FEFB020E05, 0xF7E6F7F6FEFC03FD, 0xE5E5F0F0FF11FE01, 0xF3EF07081D382F39, 0xE8D9FB01F0391C36, 0xE4001D0C3B2C2B2D },
        { 0xF1240CCA08E4180E, 0x0107F8C0D5F00908, 0xF2F2EAD2D4E2F1E5, 0xCFFFE5D9D2D4DFCD, 0xEFECF4E5E2E7F2DC, 0xF71802F0EC0616EA, 0x1DFFECF9F8FCDAE3, 0xBF1710F1C8DE020D },
    };

    const ulong TTSize = 0x6FFFFF; //2^22 - 1
    Transposition[] TTable = new Transposition[TTSize + 1];

    const byte botDepth = 5;
    Move moveToPlay;

    public Move Think(Board board, Timer timer)
    {
        Search(board, botDepth, -infinity, infinity, board.IsWhiteToMove ? 1 : -1);
        return moveToPlay;
    }

    public int Evaluate(Board board)
    {
        int evaluation = 0;
        for (byte i = 0; ++i < 7;)
        {
            PieceList whitePL = board.GetPieceList((PieceType)i, true);
            foreach (Piece whiteP in whitePL)
                evaluation += mgPieceValues[i - 1] + GetMGPSTValue((PieceType)i, whiteP.Square.Index, true);

            PieceList blackPL = board.GetPieceList((PieceType)i, false);
            foreach (Piece blackP in blackPL)
                evaluation -= mgPieceValues[i - 1] + GetMGPSTValue((PieceType)i, blackP.Square.Index, false);

            if (i == 3)
                evaluation += (50 * Convert.ToInt16(whitePL.Count == 2)) - (50 * Convert.ToInt16(blackPL.Count == 2));
        }

        return evaluation;
    }

    // alpha = best already explored option along path to the root for maximizer
    // beta = best already explored option along path to the root for minimizer
    public int Search(Board board, int depth, int alpha, int beta, int color)
    {
        int originalAlpha = alpha;
        //TT Check Code
        ref Transposition transposition = ref TTable[board.ZobristKey & TTSize];
        if (transposition.flag != TTFlags.INVALID && transposition.zobristKey == board.ZobristKey && transposition.depth >= depth)
        {
            switch (transposition.flag)
            {
                case TTFlags.PV_NODE:
                    return transposition.evaluation;
                case TTFlags.CUT_NODE:
                    alpha = Math.Max(alpha, transposition.evaluation);
                    break;
                case TTFlags.ALL_NODE:
                    beta = Math.Min(beta, transposition.evaluation);
                    break;
            }

            if (alpha >= beta)
                return transposition.evaluation;
        }
        //Draw Condition
        if (board.IsDraw())
            return 0;

        Move[] moves = board.GetLegalMoves();
        OrderMoves(board.ZobristKey, ref moves, depth);

        //Leaf Node Condition
        if (depth == 0 || moves.Length == 0)
            return board.IsInCheckmate() ? (board.PlyCount * 100) - 9999999 : (color * Evaluate(board));


        int maxEvaluation = -infinity;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int evaluation = -Search(board, depth - 1, -beta, -alpha, -color); //negamax
            board.UndoMove(move);

            if (evaluation > maxEvaluation)
            {
                maxEvaluation = evaluation;
                if (depth == botDepth)
                    moveToPlay = move;
            }

            alpha = Math.Max(alpha, evaluation); //Set alpha to the new best path for maximizer
            if (alpha >= beta) break; //Here maximizer will stop searching because minimizer will never choose this option.
        }

        //TT Storing
        transposition.evaluation = maxEvaluation;
        transposition.move = moveToPlay;

        if (maxEvaluation <= originalAlpha)
            transposition.flag = TTFlags.CUT_NODE;
        else if (maxEvaluation >= beta)
            transposition.flag = TTFlags.ALL_NODE;
        else
            transposition.flag = TTFlags.PV_NODE;

        transposition.depth = (byte)depth;

        return maxEvaluation;


    }

    public int GetMovePriority(ulong zobristKey, Move move, int depth)
    {
        int priority = 0;
        Transposition transposition = TTable[zobristKey & TTSize];
        if (transposition.move == move && transposition.zobristKey == zobristKey)
            priority += infinity;
        else if (move.IsCapture)
            priority = 1000 + 10 * (int)move.CapturePieceType - (int)move.MovePieceType;

        return priority;
    }

    public void OrderMoves(ulong zobristKey, ref Move[] moves, int depth)
    {
        List<Tuple<Move, int>> orderedMoves = new();
        foreach (Move move in moves) { orderedMoves.Add(new Tuple<Move, int>(move, GetMovePriority(zobristKey, move, depth))); }

        orderedMoves.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        for (int i = 0; i < moves.Length; i++) moves[i] = orderedMoves[i].Item1;
    }

    public int GetMGPSTValue(PieceType pieceType, int square, bool white)
    {
        int firstIndex = white ? (sbyte)(square / 8) : (7 - (sbyte)(square / 8));
        return (sbyte)BitConverter.GetBytes(compressedMGPSTs[(byte)pieceType - 1, firstIndex])[7 - (square % 8)];
    }
}
