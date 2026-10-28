using System;
using System.Linq;

namespace ERP.Domain.Exceptions
{
    public class ConcurrencyException : Exception
    {
        public string ErrorCode { get; }

        public ConcurrencyException(string message, string errorCode = "CONCURRENCY_CONFLICT")
            : base(ResolveMessage(message, errorCode))
        {
            ErrorCode = ResolveErrorCode(message, errorCode);
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 409;
        }

        public ConcurrencyException(string message, Exception innerException, string errorCode = "CONCURRENCY_CONFLICT")
            : base(ResolveMessage(message, errorCode), innerException)
        {
            ErrorCode = ResolveErrorCode(message, errorCode);
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 409;
        }

        private static bool LooksLikeErrorCode(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains('_') &&
            value.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_');

        private static string ResolveErrorCode(string first, string second)
        {
            if (LooksLikeErrorCode(first) && !LooksLikeErrorCode(second))
                return first;
            return string.IsNullOrWhiteSpace(second) ? "CONCURRENCY_CONFLICT" : second;
        }

        private static string ResolveMessage(string first, string second)
        {
            if (LooksLikeErrorCode(first) && !LooksLikeErrorCode(second))
                return $"{first}: {second}";
            return first;
        }
    }
}
