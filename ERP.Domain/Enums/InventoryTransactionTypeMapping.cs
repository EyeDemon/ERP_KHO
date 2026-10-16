namespace ERP.Domain.Enums;

public static class InventoryTransactionTypeMapping
{
    public static bool HasCanonicalSign(this TransactionType transactionType) => transactionType is
        TransactionType.Import or
        TransactionType.AdjustmentIncrease or
        TransactionType.TransferIn or
        TransactionType.Export or
        TransactionType.Ship or
        TransactionType.AdjustmentDecrease or
        TransactionType.TransferOut or
        TransactionType.Move or
        TransactionType.StatusChange or
        TransactionType.Reversal;

    public static int GetSign(this TransactionType transactionType) => transactionType switch
    {
        TransactionType.Import or
        TransactionType.AdjustmentIncrease or
        TransactionType.TransferIn => 1,

        TransactionType.Export or
        TransactionType.Ship or
        TransactionType.AdjustmentDecrease or
        TransactionType.TransferOut => -1,

        TransactionType.Move or
        TransactionType.StatusChange or
        TransactionType.Reversal => 0,

        TransactionType.TransferAdjustment => throw new ArgumentOutOfRangeException(
            nameof(transactionType),
            transactionType,
            "Legacy TransferAdjustment has no canonical sign; callers must check HasCanonicalSign() before computing signed ledger deltas."),

        _ => throw new ArgumentOutOfRangeException(
            nameof(transactionType),
            transactionType,
            "Unsupported inventory transaction type.")
    };

    public static decimal ApplySign(this TransactionType transactionType, decimal quantity) =>
        transactionType.GetSign() * quantity;
}
