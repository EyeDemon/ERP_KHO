using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Api.Tests.Helpers
{
    public class MockAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string DefaultScheme = "TestScheme";

        public MockAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Context.Request.Headers.TryGetValue("X-Test-Role", out var roleValues))
            {
                // No role provided, act as not authenticated
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = roleValues.FirstOrDefault();
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "99"),
                new Claim("Id", "99"), // For custom Id extraction
                new Claim(ClaimTypes.Name, "TestUser"),
                new Claim(ClaimTypes.Role, role!)
            };

            var identity = new ClaimsIdentity(claims, DefaultScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, DefaultScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
