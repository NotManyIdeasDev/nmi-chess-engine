using ChessChallenge.API;
using System;
using System.Linq;

public class MyBot : IChessBot
{
    record struct Transposition(ulong ZobristKey, Move Move, int Evaluation, int Depth, int Flag);

    Board board;
    Timer timer;
    Move bestMoveRoot;

    Transposition[] TTable = new Transposition[0x600000];
    int[,,] historyHeuristics = new int[2, 7, 64];
    Move[] killers = new Move[2048];
    int[] phaseWeight = { 0, 1, 1, 2, 4, 0 };
    int[] pieceValues = { 82, 337, 365, 477, 1025, 32000, 94, 281, 297, 512, 936, 32000 };
    ulong[,] compressedPSTs = {
        { 0x0000000000000000, 0xDDFFECE9F11826EA, 0xE6FCFCF6030321F4, 0xE5FEFB0C11060AE7, 0xF20D0615170C11E9, 0xFA071A1F413819EC, 0x627F3D5F447E22F5, 0x0000000000000000 },
        { 0x97EBC6DFEFE4EDE9, 0xE3CBF4FDFF12F2ED, 0xE9F70C0A131119F0, 0xF304100D1C1315F8, 0xF711133525451216, 0xD13C25415481492C, 0xB7D74824173E07EF, 0x81A7DECF3D9FF195 },
        { 0xDFFDF2EBF3F4D9EB, 0x040F100007152101, 0x000F0F0F0E1B120A, 0xFA0D0D1A220C0A04, 0xFC051332252507FE, 0xF0252B28233225FE, 0xE610EEF31E3B12D1, 0xE304AEDBE7D607F8 },
        { 0xEDF301111007DBE6, 0xD4F0ECF7FF0BFAB9, 0xD3E7F0EF0300FBDF, 0xDCE6F4FF09F906E9, 0xE8F5071A1823F8EC, 0xFB131A24112D3D10, 0x1B203A3E50431A2C, 0x202A20333F091F2B },
        { 0xFFEEF70AF1E7E1CE, 0xDDF80B02080FFD01, 0xF202F5FEFB020E05, 0xF7E6F7F6FEFC03FD, 0xE5E5F0F0FF11FE01, 0xF3EF07081D382F39, 0xE8D9FB01F0391C36, 0xE4001D0C3B2C2B2D },
        { 0xF1240CCA08E4180E, 0x0107F8C0D5F00908, 0xF2F2EAD2D4E2F1E5, 0xCFFFE5D9D2D4DFCD, 0xEFECF4E5E2E7F2DC, 0xF71802F0EC0616EA, 0x1DFFECF9F8FCDAE3, 0xBF1710F1C8DE020D },
        { 0x0000000000000000, 0x0D08080A0D0002F9, 0x0407FA0100FBFFF8, 0x0D09FDF9F9F803FF, 0x20180D05FE041111, 0x5E64554338355254, 0x7F7F7F7F7F7F7F7F, 0x0000000000000000 },
        { 0xE3CDE9F1EAEECEC0, 0xD6ECF6FBFEECE9D4, 0xE9FDFF0F0AFDECEA, 0xEEFA1019101104EE, 0xEF031616160B08EE, 0xE8EC0A09FFF7EDD7, 0xE7F8E7FEF7E7E8CC, 0xC6DAF3E4E1E5C19D },
        { 0xE9F7E9FBF7F0FBEF, 0xF2EEF9FF04F7F1E5, 0xF4FD080A0D03F9F1, 0xFA030D13070AFDF7, 0xFD090C090E0A0302, 0x02F800FFFE060004, 0xF8FC07F4FDF3FCF2, 0xF2EBF5F8F9F7EFE8 },
        { 0xF70203FFFBF304EC, 0xFAFA0002F7F7F5FD, 0xFC00FBFFF9F4F8F0, 0x03050804FBFAF8F5, 0x04030D010201FF02, 0x0707070504FDFBFD, 0x0B0D0D0BFD030803, 0x0D0A120F0C0C0805 },
        { 0xDFE4EAD5FBE0ECD7, 0xEAE9E2F0F0E9DCE0, 0xF0E50F0609110A05, 0xEE1C132F1F222717, 0x0316182D39283924, 0xEC0609312F231309, 0xEF1420293A191E00, 0xF716161B1B130A14 },
        { 0xCBDEEBF5E4F2E8D5, 0xE5F5040D0E04FBEF, 0xEDFD0B15171007F7, 0xEEFC15181B1709F5, 0xF816181B1A211A03, 0x0A11170F142D2C0D, 0xF4110E111126170B, 0xB6DDEEEEF50F04EF },
    };

    public Move Think(Board boardInput, Timer timerInput)
    {
        board = boardInput;
        timer = timerInput;

        bestMoveRoot = Move.NullMove;
        for (int depth = 1, alpha = -999999, beta = 999999; ;)
        {
            Search(depth, alpha, beta, 0);
            if (Timeout()) break;
            depth++;
        }

        return bestMoveRoot.IsNull ? board.GetLegalMoves()[0] : bestMoveRoot;
    }

    int Evaluate()
    {
        int middlegame = 0, endgame = 0, gamePhase = 0, sideToMove = 2;
        for (; --sideToMove >= 0;)
        {
            for (int i = 0; ++i < 7;)
            {
                foreach (Piece piece in board.GetPieceList((PieceType)i, sideToMove == 1))
                {
                    gamePhase += phaseWeight[i - 1];

                    middlegame += pieceValues[i - 1] + GetPSTValue((PieceType)i, piece.Square.Index, sideToMove == 1);
                    endgame += pieceValues[i + 5] + GetPSTValue((PieceType)(i + 6), piece.Square.Index, sideToMove == 1);
                }
            }

            middlegame = -middlegame;
            endgame = -endgame;
        }

        return (middlegame * gamePhase + endgame * (24 - gamePhase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }
    bool Timeout() => timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 30 + timer.IncrementMilliseconds / 2.5;

    int Search(int depth, int alpha, int beta, int ply)
    {
        ulong zKey = board.ZobristKey;
        bool inCheck = board.IsInCheck();
        bool qSearch = depth <= 0;
        int turn = board.IsWhiteToMove ? 1 : 0;
        bool notRoot = ply > 0;
        int bestEvaluation = -999999;

        if (notRoot && board.IsRepeatedPosition())
            return 0;

        Transposition tp = TTable[zKey & 0x5FFFFF];
        if (notRoot && Math.Abs(tp.Evaluation) < 50000 && tp.ZobristKey == zKey && tp.Depth >= depth &&
            (tp.Flag == 1 ||
            (tp.Flag == 2 && tp.Evaluation <= alpha) ||
            (tp.Flag == 3 && tp.Evaluation >= beta))
        ) return tp.Evaluation;

        int eval = Evaluate();
        if (qSearch)
        {
            bestEvaluation = eval;
            if (bestEvaluation >= beta) return bestEvaluation;
            alpha = Math.Max(alpha, bestEvaluation);
        }

        Move[] moves = board.GetLegalMoves(qSearch && !inCheck).OrderByDescending(move =>
            tp.Move == move && tp.ZobristKey == board.ZobristKey ? 9000000 :
            move.IsCapture ? 1000000 * (int)move.CapturePieceType - (int)move.MovePieceType : 
            killers[ply] == move ? 900000 : historyHeuristics[turn, (int)move.MovePieceType, move.TargetSquare.Index]
        ).ToArray();

        if (!qSearch && moves.Length == 0) return inCheck ? ply - 99999 : 0;

        int originalAlpha = alpha;
        Move bestMove = Move.NullMove;

        foreach (Move move in moves)
        {
            if (Timeout()) return 999999;

            board.MakeMove(move);
            int evaluation = -Search(depth - 1, -beta, -alpha, ply + 1);
            board.UndoMove(move);

            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                bestMove = move;
                if (ply == 0) bestMoveRoot = move;
                alpha = Math.Max(alpha, evaluation);
                if (alpha >= beta)
                {
                    if (!move.IsCapture && !qSearch)
                    {
                        historyHeuristics[turn, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                        killers[ply] = move;
                    }
                    
                    break;
                };
            }
        }

        TTable[zKey & 0x5FFFFF] = new Transposition(zKey, bestMove, bestEvaluation, depth, bestEvaluation >= beta ? 2 : bestEvaluation >= originalAlpha ? 1 : 3);

        return bestEvaluation;
    }

    int GetPSTValue(PieceType pieceType, int square, bool white) => (sbyte)BitConverter.GetBytes(compressedPSTs[(byte)pieceType - 1, white ? (sbyte)(square / 8) : (7 - (sbyte)(square / 8))])[7 - (square % 8)];
}
