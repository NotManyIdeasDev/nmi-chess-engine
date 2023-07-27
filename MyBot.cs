using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    //Compressed PSTs & piece values taken from "https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function".
    int[] mgPieceValues = { 82, 337, 365, 477, 1025, 20000 };
    int[] egPieceValues = { 94, 281, 297, 512, 936, 20000 };

    static ulong[,] compressedMGPSTs = {
        { 0x0000000000000000, 0xDDFFECE9F11826EA, 0xE6FCFCF6030321F4, 0xE5FEFB0C11060AE7, 0xF20D0615170C11E9, 0xFA071A1F413819EC, 0x627F3D5F447E22F5, 0x0000000000000000 }, //mgPawnTable.
        { 0x97EBC6DFEFE4EDE9, 0xE3CBF4FDFF12F2ED, 0xE9F70C0A131119F0, 0xF304100D1C1315F8, 0xF711133525451216, 0xD13C25415481492C, 0xB7D74824173E07EF, 0x81A7DECF3D9FF195 }, //mgKnightTable.
        { 0xDFFDF2EBF3F4D9EB, 0x040F100007152101, 0x000F0F0F0E1B120A, 0xFA0D0D1A220C0A04, 0xFC051332252507FE, 0xF0252B28233225FE, 0xE610EEF31E3B12D1, 0xE304AEDBE7D607F8 }, //mgBishopTable.
        { 0xEDF301111007DBE6, 0xD4F0ECF7FF0BFAB9, 0xD3E7F0EF0300FBDF, 0xDCE6F4FF09F906E9, 0xE8F5071A1823F8EC, 0xFB131A24112D3D10, 0x1B203A3E50431A2C, 0x202A20333F091F2B }, //mgRookTable.
        { 0xFFEEF70AF1E7E1CE, 0xDDF80B02080FFD01, 0xF202F5FEFB020E05, 0xF7E6F7F6FEFC03FD, 0xE5E5F0F0FF11FE01, 0xF3EF07081D382F39, 0xE8D9FB01F0391C36, 0xE4001D0C3B2C2B2D }, //mgQueenTable.
        { 0xF1240CCA08E4180E, 0x0107F8C0D5F00908, 0xF2F2EAD2D4E2F1E5, 0xCFFFE5D9D2D4DFCD, 0xEFECF4E5E2E7F2DC, 0xF71802F0EC0616EA, 0x1DFFECF9F8FCDAE3, 0xBF1710F1C8DE020D }, //mgKingTable.
    };

    int mDepth;
    Move bestMove;

    public Move Think(Board board, Timer timer)
    {
        mDepth = 4;
        Search(board, mDepth, -99999999, 99999999, board.IsWhiteToMove ? 1 : -1);
        return bestMove;
    }


    public sbyte GetMGPSTValue(PieceType pieceType, sbyte square, bool white)
    {
        //Get the corresponding positional value for each square of each pieceType, flip if black.
        return white ? (sbyte)BitConverter.GetBytes(compressedMGPSTs[(byte)pieceType - 1, (sbyte)Math.Floor((double)(square / 8))])[7 - (square % 8)] :
            (sbyte)BitConverter.GetBytes(compressedMGPSTs[(byte)pieceType - 1, 7 - (sbyte)Math.Floor((double)(square / 8))])[square % 8];
    }

    //NegaMax with alpha-beta pruning
    public int Search(Board board, int depth, int alpha, int beta, int color)
    {
        Move[] legalMoves;

        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100)
            return 0;

        if (depth == 0 || ((legalMoves = board.GetLegalMoves()).Length == 0))
        {
            if (board.IsInCheckmate())
                return -9999999;

            return color * Evaluate(board);
        }

        int bestEvaluation = int.MinValue;
        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            int evaluation = -Search(board, depth - 1, -beta, -alpha, -color);
            board.UndoMove(move);

            if (bestEvaluation < evaluation)
            {
                bestEvaluation = evaluation;
                if (depth == mDepth)
                    bestMove = move;
            }
            alpha = Math.Max(alpha, bestEvaluation);
            if (alpha >= beta) break;
        }

        return bestEvaluation;
    }

    public int Evaluate(Board board)
    {
        int materialCount = 0;
        int positionalBalance = 0; 
        for (int i = 0; ++i < 7;)
        {
            //TODO: Fix duplicate code? Find a better way to combine black and white.
            //Get material count and position evaluation for white.
            PieceList whitePieceList = board.GetPieceList((PieceType)i, true); 
            foreach (Piece whitePiece in whitePieceList)
            {
                materialCount += mgPieceValues[i - 1];
                positionalBalance += GetMGPSTValue((PieceType)i, (sbyte)whitePiece.Square.Index, true);
            }

            //Get material count and position evalutaion for black.
            PieceList blackPieceList = board.GetPieceList((PieceType)i, false);
            foreach (Piece blackPiece in board.GetPieceList((PieceType)i, false))
            {
                materialCount -= mgPieceValues[i - 1];
                positionalBalance -= GetMGPSTValue((PieceType)i, (sbyte)blackPiece.Square.Index, false);
            }


            //Detect bishop pair (+-0.50) -- cost: 32 tokens.
            if (i == 3)
            {
                positionalBalance += 50 * Math.Sign(Convert.ToInt16(whitePieceList.Count == 2));
                positionalBalance -= 50 * Math.Sign(Convert.ToInt16(blackPieceList.Count == 2));
            }

        }

        //TODO: Maybe add some sort of weighting to this? Right now it is a plain sum.
        return materialCount + positionalBalance;
    }
}
