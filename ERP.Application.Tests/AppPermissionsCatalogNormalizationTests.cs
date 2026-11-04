using System.Text.RegularExpressions;
using ERP.Domain.Security;
using FluentAssertions;

namespace ERP.Application.Tests;

public sealed class AppPermissionsCatalogNormalizationTests
{
    private static readonly Regex DuplicatePhraseRegex = new(
        @"(phê duyệt\s+phê duyệt|kiểm tra\s+kiểm tra)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void Definitions_And_CustomerDefinitions_NormalizeApprovalLabels_And_ContainZeroDuplicatedPhrases()
    {
        AppPermissions.Definitions.Should().HaveCount(151);
        AppPermissions.CustomerDefinitions.Should().HaveCount(141);

        foreach (var catalog in new[] { AppPermissions.Definitions, AppPermissions.CustomerDefinitions })
        {
            catalog.Single(x => x.Code == AppPermissions.ApprovalApprove)
                .Description.Should().Be("Phê duyệt phiếu nhập kho");

            catalog.Single(x => x.Code == AppPermissions.ApprovalReject)
                .Description.Should().Be("Từ chối phê duyệt phiếu nhập kho");

            catalog.Where(x => DuplicatePhraseRegex.IsMatch(x.Description))
                .Should().BeEmpty();
        }
    }
}
