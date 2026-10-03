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
        [Fact]
        public void MigratedHttpActionsHaveCataloguedPermissionsWithoutRoleAlternatives()
        {
            var controllers = new[] { typeof(ImportReceiptsController), typeof(PutawayTasksController),
                typeof(ProductsController), typeof(ProductCategoriesController), typeof(ProductBarcodesController),
                typeof(ProductBarcodeLookupController), typeof(WarehousesController), typeof(UnitsController),
                typeof(BusinessPartnersController), typeof(UserWarehouseAccessController), typeof(AccountSecurityController),
                typeof(PermissionsController), typeof(WarehouseStructureController), typeof(LocationsController) };
            var catalog = typeof(AppPermissions).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
            foreach (var controller in controllers)
            foreach (var action in controller.GetMethods().Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any()))
            {
                var grants = controller.GetCustomAttributes<PermissionAuthorizeAttribute>()
                    .Concat(action.GetCustomAttributes<PermissionAuthorizeAttribute>()).Select(p => p.Permission).ToArray();
                grants.Should().NotBeEmpty($"{controller.Name}.{action.Name} is a migrated HTTP action");
                grants.Should().OnlyContain(code => catalog.Contains(code));
                controller.GetCustomAttributes<AuthorizeAttribute>().Concat(action.GetCustomAttributes<AuthorizeAttribute>())
                    .Should().OnlyContain(a => string.IsNullOrEmpty(a.Roles) && string.IsNullOrEmpty(a.Policy));
                Console.WriteLine($"Endpoint permission: {controller.Name}.{action.Name} => {string.Join(",", grants)}");
            }
        }

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
        [InlineData(typeof(ProductCategoriesController))]
        [InlineData(typeof(WarehousesController))]
        public void CatalogControllers_CreateUpdateDelete_RequiresAdminOrManager(Type controllerType)
        {
            var createMethod = controllerType.GetMethod("Create");
            var updateMethod = controllerType.GetMethod("Update");
            var deleteMethod = controllerType.GetMethod("Delete");

        var expected = controllerType == typeof(WarehousesController)
            ? new[] { AppPermissions.WarehouseManage, AppPermissions.WarehouseManage, AppPermissions.WarehouseManage }
            : controllerType == typeof(UnitsController)
                ? new[] { AppPermissions.UomManage, AppPermissions.UomManage, AppPermissions.UomManage }
                : controllerType == typeof(ProductCategoriesController)
                    ? new[] { AppPermissions.CategoryManage, AppPermissions.CategoryManage, AppPermissions.CategoryManage }
                    : new[] { AppPermissions.ProductCreate, AppPermissions.ProductUpdate, AppPermissions.ProductDeactivate };
        createMethod!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(expected[0]);
        updateMethod!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(expected[1]);
        deleteMethod!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(expected[2]);
        }

        [Theory]
        [InlineData(typeof(ProductsController))]
        [InlineData(typeof(UnitsController))]
        [InlineData(typeof(ProductCategoriesController))]
        [InlineData(typeof(WarehousesController))]
        public void CatalogControllers_Get_RequiresAllRoles(Type controllerType)
        {
            var classAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
            classAttr.Should().NotBeNull();
            classAttr!.Roles.Should().BeNull();
            
            var getMethod = controllerType.GetMethod("GetAll");
            var permission = controllerType == typeof(WarehousesController) ? AppPermissions.WarehouseRead
                : controllerType == typeof(UnitsController) ? AppPermissions.UomRead
                : controllerType == typeof(ProductCategoriesController) ? AppPermissions.CategoryRead : AppPermissions.ProductRead;
            getMethod!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(permission);
        }

        [Fact]
        public void ImportReceiptsController_Class_RequiresAdminManagerOrViewer()
        {
            var classAttr = typeof(ImportReceiptsController).GetCustomAttribute<AuthorizeAttribute>();
            classAttr.Should().NotBeNull();
            classAttr!.Roles.Should().BeNull();
        }

        [Theory]
        [InlineData("Create")]
        [InlineData("Cancel")]
        public void ImportReceiptsController_Mutations_RequireAdminOrManager(string methodName)
        {
            var method = typeof(ImportReceiptsController).GetMethod(methodName);
            var methodAttr = method!.GetCustomAttribute<PermissionAuthorizeAttribute>();
            methodAttr!.Permission.Should().Be(methodName == "Create" ? AppPermissions.ReceiptCreate : AppPermissions.ReceiptCancel);
        }

        [Fact]
        public void ImportReceiptsController_Approve_RequiresCheckerPolicy()
        {
            typeof(ImportReceiptsController).GetMethod("Approve")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.ReceiptComplete);
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


        [Fact]
        public void WarehouseStructureEndpoints_UseCanonicalLocationAndZonePermissions()
        {
            typeof(WarehouseStructureController).GetMethod("Structure")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.LocationRead);
            typeof(WarehouseStructureController).GetMethod("Zones")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.LocationRead);
            typeof(WarehouseStructureController).GetMethod("CreateZone")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.WarehouseZoneManage);
            typeof(WarehouseStructureController).GetMethod("CreateAisle")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.WarehouseZoneManage);
            typeof(WarehouseStructureController).GetMethod("CreateRack")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.WarehouseZoneManage);
            typeof(WarehouseStructureController).GetMethod("CreateLevel")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.WarehouseZoneManage);
            typeof(LocationsController).GetMethod("List")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.LocationRead);
            typeof(LocationsController).GetMethod("Create")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.LocationManage);
            typeof(LocationsController).GetMethod("Update")!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.LocationManage);
        }

        [Fact]
        public void ProductBarcodeEndpoints_ReadForAllRoles_MutateForAdminOrManager()
        {
            typeof(ProductBarcodeLookupController).GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.ProductRead);
            typeof(ProductBarcodesController).GetMethod("GetAll")!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.ProductRead);
            typeof(ProductBarcodesController).GetMethod("Create")!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.BarcodeManage);
            typeof(ProductBarcodesController).GetMethod("Delete")!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.BarcodeManage);
        }
    }
}
