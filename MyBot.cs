using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    //Pre-Generated PSTs and constants.
    int bigNumber = 99999999;
    int[] mgPieceValues = { 82, 337, 365, 477, 1025, 32000 }; //Pawn, Knight, Bishop, Rook, Queen, King
    static ulong[,] compressedMGPSTs = {
        { 0x0000000000000000, 0xDDFFECE9F11826EA, 0xE6FCFCF6030321F4, 0xE5FEFB0C11060AE7, 0xF20D0615170C11E9, 0xFA071A1F413819EC, 0x627F3D5F447E22F5, 0x0000000000000000 }, //mgPawnTable.
        { 0x97EBC6DFEFE4EDE9, 0xE3CBF4FDFF12F2ED, 0xE9F70C0A131119F0, 0xF304100D1C1315F8, 0xF711133525451216, 0xD13C25415481492C, 0xB7D74824173E07EF, 0x81A7DECF3D9FF195 }, //mgKnightTable.
        { 0xDFFDF2EBF3F4D9EB, 0x040F100007152101, 0x000F0F0F0E1B120A, 0xFA0D0D1A220C0A04, 0xFC051332252507FE, 0xF0252B28233225FE, 0xE610EEF31E3B12D1, 0xE304AEDBE7D607F8 }, //mgBishopTable.
        { 0xEDF301111007DBE6, 0xD4F0ECF7FF0BFAB9, 0xD3E7F0EF0300FBDF, 0xDCE6F4FF09F906E9, 0xE8F5071A1823F8EC, 0xFB131A24112D3D10, 0x1B203A3E50431A2C, 0x202A20333F091F2B }, //mgRookTable.
        { 0xFFEEF70AF1E7E1CE, 0xDDF80B02080FFD01, 0xF202F5FEFB020E05, 0xF7E6F7F6FEFC03FD, 0xE5E5F0F0FF11FE01, 0xF3EF07081D382F39, 0xE8D9FB01F0391C36, 0xE4001D0C3B2C2B2D }, //mgQueenTable.
        { 0xF1240CCA08E4180E, 0x0107F8C0D5F00908, 0xF2F2EAD2D4E2F1E5, 0xCFFFE5D9D2D4DFCD, 0xEFECF4E5E2E7F2DC, 0xF71802F0EC0616EA, 0x1DFFECF9F8FCDAE3, 0xBF1710F1C8DE020D }, //mgKingTable.
    };

    int botDepth = 4;
    Move bestMove;

    public Move Think(Board board, Timer timer)
    {
        SearchABNegaMax(board, botDepth, -bigNumber, bigNumber, board.IsWhiteToMove ? 1 : -1);
        return bestMove;
    }

    public int EvaluatePosition(Board board)
    {
        int evaluation = 0;
        for (int i = 0; ++i < 7;)
        {
            PieceList whitePieceList = board.GetPieceList((PieceType)i, true);
            foreach (Piece whitePiece in whitePieceList)
            {
                //Material + piece placement
                evaluation += mgPieceValues[i - 1] + GetMGPSTValue((PieceType)i, whitePiece.Square.Index, true);
            }

            PieceList blackPieceList = board.GetPieceList((PieceType)i, false);
            foreach (Piece blackPiece in blackPieceList)
            {
                //Material + piece placement
                evaluation -= mgPieceValues[i - 1] + GetMGPSTValue((PieceType)i, blackPiece.Square.Index, false);
            }

            //Detect bishop pair, give 50 centipawns.
            if (i == 3)
                evaluation += (50 * Convert.ToInt16(whitePieceList.Count == 2)) - (50 * Convert.ToInt16(blackPieceList.Count == 2));
        }

        return evaluation;
    }

    public int SearchABNegaMax(Board board, int depth, int alpha, int beta, int color)
    {
        Move[] legalMoves;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100)
            return 0;

        if (depth == 0 || (legalMoves = board.GetLegalMoves()).Length == 0)
        {
            if (board.IsInCheckmate())
                return -bigNumber;

            return color * EvaluatePosition(board);
        }

        int bestEvaluation = -bigNumber;
        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            int evaluation = -SearchABNegaMax(board, depth - 1, -beta, -alpha, -color);
            board.UndoMove(move);

            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                if (depth == botDepth)
                    bestMove = move;
            }
            alpha = Math.Max(alpha, bestEvaluation);
            if (alpha >= beta) break;
        }

        return bestEvaluation;
    }

    public int GetMGPSTValue(PieceType pieceType, int square, bool white)
    {
        //Flip PSTs for opposite colors.
        int firstIndex = white ? (sbyte)(square / 8) : (7 - (sbyte)(square / 8));
        int secondIndex = white ? (7 - (square % 8)) : square % 8;
        //Get the corresponding positional value for each square of each pieceType, flip if black.
        return BitConverter.GetBytes(compressedMGPSTs[(byte)pieceType - 1, firstIndex])[secondIndex];
    }
}
