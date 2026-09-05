using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentCommandAttribute : TypeFilterAttribute
{
    public IdempotentCommandAttribute(string commandScope) : base(typeof(IdempotentCommandFilter))
    {
        Arguments = [commandScope];
    }
}
