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
            var controllers = new[] { typeof(ImportReceiptsController), typeof(PutawayTasksController), typeof(PurchaseOrdersController), typeof(AsnsController),
                typeof(ProductsController), typeof(ProductCategoriesController), typeof(ProductBarcodesController),
                typeof(ProductBarcodeLookupController), typeof(WarehousesController), typeof(UnitsController),
                typeof(BusinessPartnersController), typeof(ExportReceiptsController), typeof(StockAllocationsController), typeof(PickingTasksController), typeof(PackingSessionsController), typeof(HandlingUnitsController), typeof(ShipmentsController), typeof(UserWarehouseAccessController), typeof(AccountSecurityController),
                typeof(PermissionsController) };
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
        public void ExportReceiptsController_UsesExactCapabilityPermissions()
        {
            var expected = new Dictionary<string, string[]>
            {
                ["GetAll"] = [AppPermissions.ExportReceiptRead],
                ["GetById"] = [AppPermissions.ExportReceiptRead],
                ["Create"] = [AppPermissions.ExportReceiptCreate],
                ["Cancel"] = [AppPermissions.ExportReceiptCancel],
                ["ApproveAndReserve"] = [AppPermissions.ExportReceiptApprove],
                ["ApproveAndDispatch"] = [AppPermissions.ExportReceiptApprove, AppPermissions.ExportReceiptDispatch],
                ["Dispatch"] = [AppPermissions.ExportReceiptDispatch],
                ["Approve"] = [AppPermissions.ExportReceiptApprove],
                ["SetCustomer"] = [AppPermissions.ExportReceiptUpdate, AppPermissions.PartnerRead],
            };

            typeof(ExportReceiptsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().BeNull();
            foreach (var (methodName, permissions) in expected)
            {
                var grants = typeof(ExportReceiptsController).GetMethod(methodName)!
                    .GetCustomAttributes<PermissionAuthorizeAttribute>()
                    .Select(x => x.Permission)
                    .ToArray();
                grants.Should().BeEquivalentTo(permissions, $"ExportReceiptsController.{methodName}");
                typeof(ExportReceiptsController).GetMethod(methodName)!
                    .GetCustomAttributes<AuthorizeAttribute>()
                    .Should().OnlyContain(x => string.IsNullOrEmpty(x.Roles) && string.IsNullOrEmpty(x.Policy));
            }
        }

        [Theory]
        [InlineData(typeof(StocktakesController), "Approve")]
        [InlineData(typeof(StockTransfersController), "Approve")]
        public void LegacyApprovalEndpoints_StillRequireCheckerPolicy(Type controllerType, string methodName)
        {
            controllerType.GetMethod(methodName)!
                .GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(ApprovalPolicies.Checker);
        }

        [Theory]
        [InlineData(typeof(PurchaseOrdersController), "List", AppPermissions.PurchaseOrderRead)]
        [InlineData(typeof(PurchaseOrdersController), "Detail", AppPermissions.PurchaseOrderRead)]
        [InlineData(typeof(PurchaseOrdersController), "Create", AppPermissions.PurchaseOrderCreate)]
        [InlineData(typeof(PurchaseOrdersController), "Update", AppPermissions.PurchaseOrderUpdate)]
        [InlineData(typeof(PurchaseOrdersController), "Open", AppPermissions.PurchaseOrderRelease)]
        [InlineData(typeof(PurchaseOrdersController), "Close", AppPermissions.PurchaseOrderClose)]
        [InlineData(typeof(PurchaseOrdersController), "Cancel", AppPermissions.PurchaseOrderCancel)]
        [InlineData(typeof(AsnsController), "List", AppPermissions.AsnRead)]
        [InlineData(typeof(AsnsController), "Detail", AppPermissions.AsnRead)]
        [InlineData(typeof(AsnsController), "Create", AppPermissions.AsnCreate)]
        [InlineData(typeof(AsnsController), "Update", AppPermissions.AsnUpdate)]
        [InlineData(typeof(AsnsController), "Confirm", AppPermissions.AsnConfirm)]
        [InlineData(typeof(AsnsController), "MarkInTransit", AppPermissions.AsnUpdate)]
        [InlineData(typeof(AsnsController), "Arrive", AppPermissions.AsnReceive)]
        [InlineData(typeof(AsnsController), "StartReceiving", AppPermissions.AsnReceive)]
        [InlineData(typeof(AsnsController), "Complete", AppPermissions.AsnReceive)]
        [InlineData(typeof(AsnsController), "Cancel", AppPermissions.AsnCancel)]
        public void InboundPlanningEndpoints_RequireExactCapability(Type controllerType, string methodName, string permission)
        {
            controllerType.GetMethod(methodName)!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(permission);
        }

        [Theory]
        [InlineData("List", AppPermissions.PutawayRead)]
        [InlineData("Detail", AppPermissions.PutawayRead)]
        [InlineData("Destinations", AppPermissions.PutawayRead)]
        [InlineData("Assign", AppPermissions.PutawayAssign)]
        [InlineData("Start", AppPermissions.PutawayExecute)]
        [InlineData("Move", AppPermissions.PutawayExecute)]
        [InlineData("Exception", AppPermissions.PutawayExecute)]
        [InlineData("Resume", AppPermissions.PutawayExecute)]
        [InlineData("Cancel", AppPermissions.PutawayCancel)]
        public void PutawayEndpoints_RequireExactCapability(string methodName, string permission)
        {
            typeof(PutawayTasksController).GetMethod(methodName)!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(permission);
        }

        [Theory]
        [InlineData("List", AppPermissions.PickingRead)]
        [InlineData("Get", AppPermissions.PickingRead)]
        [InlineData("Assign", AppPermissions.PickingAssign)]
        [InlineData("Start", AppPermissions.PickingExecute)]
        [InlineData("Pick", AppPermissions.PickingExecute)]
        [InlineData("ReportShortPick", AppPermissions.PickingShortPick)]
        [InlineData("ResolveShortPick", AppPermissions.PickingShortPick)]
        [InlineData("OverrideShortPick", AppPermissions.PickingOverride)]
        [InlineData("Complete", AppPermissions.PickingExecute)]
        public void PickingEndpoints_RequireExactCapability(string methodName, string permission)
        {
            typeof(PickingTasksController).GetMethod(methodName)!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(permission);
        }

        [Theory]
        [InlineData("List", AppPermissions.PackingRead)]
        [InlineData("Get", AppPermissions.PackingRead)]
        [InlineData("Create", AppPermissions.PackingExecute)]
        [InlineData("CreateHandlingUnit", AppPermissions.HandlingUnitCreate)]
        [InlineData("Pack", AppPermissions.PackingExecute)]
        [InlineData("CloseHandlingUnit", AppPermissions.PackingExecute)]
        [InlineData("CancelHandlingUnit", AppPermissions.HandlingUnitModify)]
        [InlineData("NestHandlingUnit", AppPermissions.HandlingUnitModify)]
        [InlineData("UnnestHandlingUnit", AppPermissions.HandlingUnitModify)]
        [InlineData("Complete", AppPermissions.PackingExecute)]
        [InlineData("Close", AppPermissions.PackingExecute)]
        [InlineData("Cancel", AppPermissions.PackingExecute)]
        public void PackingEndpoints_RequireExactCapability(string methodName, string permission)
        {
            typeof(PackingSessionsController).GetMethod(methodName)!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(permission);
        }

        [Fact]
        public void HandlingUnitReadController_RequiresExactCapability()
        {
            typeof(HandlingUnitsController).GetCustomAttribute<PermissionAuthorizeAttribute>()!
                .Permission.Should().Be(AppPermissions.HandlingUnitRead);
        }

        [Theory]
        [InlineData("List", AppPermissions.ShipmentRead)]
        [InlineData("Get", AppPermissions.ShipmentRead)]
        [InlineData("Stage", AppPermissions.ShipmentStage)]
        [InlineData("Dispatch", AppPermissions.ShipmentDispatch)]
        public void ShipmentEndpoints_RequireExactCapability(string methodName, string permission)
        {
            typeof(ShipmentsController).GetMethod(methodName)!
                .GetCustomAttributes<PermissionAuthorizeAttribute>()
                .Select(x => x.Permission)
                .Should().Contain(permission);
        }

        [Theory]
        [InlineData("StartLoading")]
        [InlineData("LoadHandlingUnit")]
        [InlineData("CompleteLoading")]
        public void ShipmentLoadingEndpoints_RequireShipmentLoadAndLoadingExecute(string methodName)
        {
            typeof(ShipmentsController).GetMethod(methodName)!
                .GetCustomAttributes<PermissionAuthorizeAttribute>()
                .Select(x => x.Permission)
                .Should().BeEquivalentTo(AppPermissions.ShipmentLoad, AppPermissions.LoadingExecute);
        }

        [Theory]
        [InlineData(typeof(SalesOrdersController), "List", AppPermissions.SalesOrderRead)]
        [InlineData(typeof(SalesOrdersController), "Get", AppPermissions.SalesOrderRead)]
        [InlineData(typeof(SalesOrdersController), "Create", AppPermissions.SalesOrderCreate)]
        [InlineData(typeof(SalesOrdersController), "Hold", AppPermissions.SalesOrderHold)]
        [InlineData(typeof(SalesOrdersController), "Release", AppPermissions.SalesOrderRelease)]
        [InlineData(typeof(SalesOrdersController), "Cancel", AppPermissions.SalesOrderCancel)]
        [InlineData(typeof(BackordersController), "List", AppPermissions.BackorderRead)]
        [InlineData(typeof(BackordersController), "Get", AppPermissions.BackorderRead)]
        [InlineData(typeof(BackordersController), "Reallocate", AppPermissions.BackorderManage)]
        [InlineData(typeof(BackordersController), "Cancel", AppPermissions.BackorderManage)]
        public void DemandAndBackorderEndpoints_RequireExactCapability(
            Type controllerType,
            string methodName,
            string permission)
        {
            controllerType.GetMethod(methodName)!
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(permission);
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
        public void ProductBarcodeEndpoints_ReadForAllRoles_MutateForAdminOrManager()
        {
            typeof(ProductBarcodeLookupController).GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.ProductRead);
            typeof(ProductBarcodesController).GetMethod("GetAll")!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.ProductRead);
            typeof(ProductBarcodesController).GetMethod("Create")!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.BarcodeManage);
            typeof(ProductBarcodesController).GetMethod("Delete")!.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission.Should().Be(AppPermissions.BarcodeManage);
        }
    }
}
