using System;

namespace ERP.Domain.Exceptions
{
    public class ConcurrencyException : Exception
    {
        public string ErrorCode { get; }

        public ConcurrencyException(string message, string errorCode = "CONCURRENCY_CONFLICT") : base(message)
        {
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "CONCURRENCY_CONFLICT" : errorCode;
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 409;
        }

        public ConcurrencyException(string message, Exception innerException, string errorCode = "CONCURRENCY_CONFLICT") : base(message, innerException)
        {
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "CONCURRENCY_CONFLICT" : errorCode;
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 409;
        }
    }
}
