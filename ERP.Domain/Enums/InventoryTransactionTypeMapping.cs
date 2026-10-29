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
        transactionType is TransactionType.Move or TransactionType.StatusChange
            ? quantity
            : transactionType.GetSign() * quantity;
}

public static class InventoryLedgerCalculationRules
{
    /// <summary>
    /// Quy tắc chuẩn hóa đối soát sổ cái (F-013):
    /// Một dòng <see cref="Entities.InventoryTransaction"/> được tính vào tổng đại số sổ cái (Check A / Check B)
    /// khi và chỉ khi <c>t.TransactionType != TransactionType.Reversal || t.CorrectiveTransactionId == null</c>.
    /// - Các dòng <c>TransactionType.Reversal</c> có <c>CorrectiveTransactionId != null</c> là bản ghi đánh dấu
    ///   (marker) của <c>InventoryReversalService</c> đi kèm một dòng bút toán sửa sai riêng biệt -> loại trừ (0m).
    /// - Các dòng <c>TransactionType.Reversal</c> có <c>CorrectiveTransactionId == null</c> là bút toán đảo trực tiếp
    ///   (Kitting / Cross-Dock / Break-Bulk Reversal) -> tính trực tiếp theo dấu của <c>Quantity</c>.
    /// </summary>
    public static bool IsIncludedInAlgebraicLedgerSum(TransactionType transactionType, int? correctiveTransactionId) =>
        transactionType != TransactionType.Reversal || correctiveTransactionId == null;

    public static bool IsIncludedInAlgebraicLedgerSum(Entities.InventoryTransaction transaction) =>
        transaction is not null && IsIncludedInAlgebraicLedgerSum(transaction.TransactionType, transaction.CorrectiveTransactionId);

    public static decimal ComputeAlgebraicDelta(TransactionType transactionType, decimal quantity, int? correctiveTransactionId) =>
        !IsIncludedInAlgebraicLedgerSum(transactionType, correctiveTransactionId)
            ? 0m
            : transactionType == TransactionType.Reversal
                ? quantity
                : transactionType.ApplySign(quantity);

    public static decimal ComputeAlgebraicDelta(Entities.InventoryTransaction transaction) =>
        transaction is null
            ? 0m
            : ComputeAlgebraicDelta(transaction.TransactionType, transaction.Quantity, transaction.CorrectiveTransactionId);
}
