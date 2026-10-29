namespace ERP.Domain.Enums
{
    public enum StocktakeStatus
    {
        Draft = 0,
        Approved = 1,
        Cancelled = 2,
        InProgress = 3,
        CountCompleted = 4,
        PendingApproval = 5,
        Completed = 6,
        Posted = 6,
        Rejected = 7,
        PendingRecount = 8
    }
}

namespace ERP.Domain.Exceptions
{
    public class ConflictException : ConcurrencyException
    {
        public ConflictException(string message, string errorCode = "CONCURRENCY_CONFLICT")
            : base(message, errorCode)
        {
        }

        public ConflictException(string message, System.Exception innerException, string errorCode = "CONCURRENCY_CONFLICT")
            : base(message, innerException, errorCode)
        {
        }
    }

    public class UnprocessableDomainException : System.Exception
    {
        public string ErrorCode { get; }

        public UnprocessableDomainException(string message, string errorCode = "UNPROCESSABLE_ENTITY")
            : base(message)
        {
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "UNPROCESSABLE_ENTITY" : errorCode;
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 422;
        }

        public UnprocessableDomainException(string message, System.Exception innerException, string errorCode = "UNPROCESSABLE_ENTITY")
            : base(message, innerException)
        {
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "UNPROCESSABLE_ENTITY" : errorCode;
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 422;
        }
    }
}
