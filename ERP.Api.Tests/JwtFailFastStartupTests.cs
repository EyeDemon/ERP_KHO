using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ERP.Api.Tests
{
    public class JwtFailFastStartupTests
    {
        [Fact]
        public void Startup_InProductionWithPlaceholderSecret_ThrowsInvalidOperationException()
        {
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
            });

            Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        }
    }
}
