namespace ERP.Domain.Enums;

public static class InventoryTransactionTypeMapping
{
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

        _ => throw new ArgumentOutOfRangeException(
            nameof(transactionType),
            transactionType,
            "Unsupported inventory transaction type.")
    };

    public static decimal ApplySign(this TransactionType transactionType, decimal quantity) =>
        transactionType.GetSign() * quantity;
}
