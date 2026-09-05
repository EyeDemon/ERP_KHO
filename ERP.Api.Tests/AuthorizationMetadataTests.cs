using System.Reflection;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ERP.Api.Tests
{
    public class AuthorizationMetadataTests
    {
        private AuthorizeAttribute? GetAuthorizeAttribute(Type controllerType, string? methodName = null, Type[]? methodParams = null)
        {
            if (methodName != null)
            {
                var methodInfo = methodParams != null 
                    ? controllerType.GetMethod(methodName, methodParams)
                    : controllerType.GetMethod(methodName);
                
                var methodAttr = methodInfo?.GetCustomAttribute<AuthorizeAttribute>();
                if (methodAttr != null) return methodAttr;
            }
            
            return controllerType.GetCustomAttribute<AuthorizeAttribute>();
        }

        [Fact]
        public void AuthController_Login_HasNoRoleRequirement()
        {
            var methodInfo = typeof(AuthController).GetMethod("Login");
            var authorizeAttribute = methodInfo?.GetCustomAttribute<AuthorizeAttribute>();
            var allowAnonymous = methodInfo?.GetCustomAttribute<AllowAnonymousAttribute>();

            // It should either have [AllowAnonymous] or no [Authorize]
            if (authorizeAttribute != null)
            {
                allowAnonymous.Should().NotBeNull();
            }
        }

        [Theory]
        [InlineData(typeof(ProductsController))]
        [InlineData(typeof(UnitsController))]
        [InlineData(typeof(WarehousesController))]
        public void CatalogControllers_CreateUpdateDelete_RequiresAdminOrManager(Type controllerType)
        {
            var createMethod = controllerType.GetMethod("Create");
            var updateMethod = controllerType.GetMethod("Update");
            var deleteMethod = controllerType.GetMethod("Delete");

        var expectedRoles = controllerType == typeof(WarehousesController) ? AppRoles.Admin : AppRoles.AdminOrManager;
        createMethod!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(expectedRoles);
        updateMethod!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(expectedRoles);
        deleteMethod!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(expectedRoles);
        }

        [Theory]
        [InlineData(typeof(ProductsController))]
        [InlineData(typeof(UnitsController))]
        [InlineData(typeof(WarehousesController))]
        public void CatalogControllers_Get_RequiresAllRoles(Type controllerType)
        {
            var classAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
            classAttr!.Roles.Should().Be(AppRoles.AllRoles);
            
            var getMethod = controllerType.GetMethod("GetAll");
            getMethod!.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull("Because class level is sufficient");
        }

        [Fact]
        public void ImportReceiptsController_Class_RequiresAdminManagerOrViewer()
        {
            var classAttr = typeof(ImportReceiptsController).GetCustomAttribute<AuthorizeAttribute>();
            classAttr!.Roles.Should().Be(AppRoles.AdminManagerOrViewer);
        }

        [Theory]
        [InlineData("Create")]
        [InlineData("Cancel")]
        public void ImportReceiptsController_Mutations_RequireAdminOrManager(string methodName)
        {
            var method = typeof(ImportReceiptsController).GetMethod(methodName);
            var methodAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
            methodAttr!.Roles.Should().Be(AppRoles.AdminOrManager);
        }

        [Fact]
        public void ImportReceiptsController_Approve_RequiresCheckerPolicy()
        {
            typeof(ImportReceiptsController).GetMethod("Approve")!
                .GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(ApprovalPolicies.Checker);
        }

        [Fact]
        public void ExportReceiptsController_Get_RequiresAllRoles()
        {
            var classAttr = typeof(ExportReceiptsController).GetCustomAttribute<AuthorizeAttribute>();
            classAttr!.Roles.Should().Be(AppRoles.AllRoles);
        }

        [Fact]
        public void ExportReceiptsController_CreateCancel_RequiresAdminManagerOrStaff()
        {
            var createMethod = typeof(ExportReceiptsController).GetMethod("Create");
            var cancelMethod = typeof(ExportReceiptsController).GetMethod("Cancel");

            createMethod!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(AppRoles.AdminManagerOrStaff);
            cancelMethod!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(AppRoles.AdminManagerOrStaff);
        }

        [Theory]
        [InlineData(typeof(ExportReceiptsController), "Approve")]
        [InlineData(typeof(ExportReceiptsController), "ApproveAndReserve")]
        [InlineData(typeof(ExportReceiptsController), "ApproveAndDispatch")]
        [InlineData(typeof(StocktakesController), "Approve")]
        [InlineData(typeof(StockTransfersController), "Approve")]
        public void ApprovalEndpoints_RequireCheckerPolicy(Type controllerType, string methodName)
        {
            controllerType.GetMethod(methodName)!
                .GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(ApprovalPolicies.Checker);
        }

        [Fact]
        public void ReportsController_Get_RequiresAdminManagerOrViewer()
        {
            var classAttr = typeof(ReportsController).GetCustomAttribute<AuthorizeAttribute>();
            classAttr!.Roles.Should().Be(AppRoles.AdminManagerOrViewer);
        }

        [Fact]
        public void InventoryStocksController_Get_RequiresAllRoles()
        {
            var classAttr = typeof(InventoryStocksController).GetCustomAttribute<AuthorizeAttribute>();
            classAttr!.Roles.Should().Be(AppRoles.AllRoles);
        }

        [Fact]
        public void InventoryTransactionsController_Get_RequiresAllRoles()
        {
            var classAttr = typeof(InventoryTransactionsController).GetCustomAttribute<AuthorizeAttribute>();
            classAttr!.Roles.Should().Be(AppRoles.AllRoles);
        }

        [Fact]
        public void StockReservationsController_Expire_RequiresAdminManagerOrStaff()
        {
            var method = typeof(StockReservationsController).GetMethod("Expire");

            method!.GetCustomAttribute<AuthorizeAttribute>()!.Roles
                .Should().Be(AppRoles.AdminManagerOrStaff);
            AppRoles.AdminManagerOrStaff.Should().NotContain(AppRoles.Viewer);
        }

        [Fact]
        public void ViewerRole_CannotMutate()
        {
            // Verify Viewer is NOT in AdminOrManager or AdminManagerOrStaff
            AppRoles.AdminOrManager.Should().NotContain(AppRoles.Viewer);
            AppRoles.AdminManagerOrStaff.Should().NotContain(AppRoles.Viewer);
        }
    }
}
