namespace ERP.Api.Authorization
{
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string WarehouseStaff = "WarehouseStaff";
        public const string Viewer = "Viewer";

        public const string AdminOrManager = Admin + "," + Manager;
        public const string AdminManagerOrStaff = Admin + "," + Manager + "," + WarehouseStaff;
        public const string AdminManagerOrViewer = Admin + "," + Manager + "," + Viewer;
        public const string AllRoles = Admin + "," + Manager + "," + WarehouseStaff + "," + Viewer;
    }
}
