using System.Text.Json;
using ERP.Application.DTOs;
using FluentAssertions;

namespace ERP.Application.Tests;

public sealed class PutawayDtoSerializationTests
{
    [Fact]
    public void Viewer_dtos_omit_empty_concurrency_tokens_while_mutation_dtos_keep_them()
    {
        var viewer = JsonSerializer.Serialize(new PutawayTaskDto
        {
            Items = [new PutawayTaskItemDto()],
        });
        var viewerDestination = JsonSerializer.Serialize(new WarehouseLocationDto());

        viewer.Should().NotContain("RowVersion");
        viewerDestination.Should().NotContain("RowVersion");

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var mutation = JsonSerializer.Serialize(new PutawayTaskDto
        {
            RowVersion = token,
            Items = [new PutawayTaskItemDto { RowVersion = token }],
        });
        using var document = JsonDocument.Parse(mutation);
        document.RootElement.GetProperty("RowVersion").GetString().Should().Be(token);
        document.RootElement.GetProperty("Items")[0].GetProperty("RowVersion").GetString().Should().Be(token);
        Convert.FromBase64String(token).Should().HaveCount(16);
    }
}
