namespace ERP.Domain.Enums;

public enum ReceiptStatus
{
    Draft = 0,
    Approved = 1,
    Cancelled = 2,
    Dispatched = 3,
    Received = 4,
    ReadyToPost = 5,
    Posted = 6,
    QcPending = 7,
    QcCompleted = 8
}
