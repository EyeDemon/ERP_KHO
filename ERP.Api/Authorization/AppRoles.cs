namespace ERP.Api.Authorization
{
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Supervisor = "Supervisor";
        public const string Accountant = "Accountant";
        public const string WarehouseStaff = "WarehouseStaff";
        public const string Viewer = "Viewer";

        public const string SupportAdmin = "SupportAdmin";
        public const string SupportReadOnly = "SupportReadOnly";
        public const string SuperAdmin = "SuperAdmin";
        public const string PlatformOwner = "PlatformOwner";
        public const string PlatformAdmin = "PlatformAdmin";
        public const string BillingAdmin = "BillingAdmin";
        public const string SecurityAuditor = "SecurityAuditor";
        public const string PlatformReadOnly = "PlatformReadOnly";
        public const string PlatformSuperAdmin = "PlatformSuperAdmin";
        public const string PlatformOpsAdmin = "PlatformOpsAdmin";
        public const string PlatformBillingAdmin = "PlatformBillingAdmin";
        public const string PlatformSupportAgent = "PlatformSupportAgent";

        public const string AdminOrManager = Admin + "," + Manager;
        public const string AdminManagerOrStaff = Admin + "," + Manager + "," + Supervisor + "," + WarehouseStaff;
        public const string AdminManagerOrViewer = Admin + "," + Manager + "," + Supervisor + "," + Accountant + "," + Viewer;
        public const string AllRoles = Admin + "," + Manager + "," + Supervisor + "," + Accountant + "," + WarehouseStaff + "," + Viewer;

        public static bool IsSuperAdminPlatformRole(string? roleName) =>
            string.Equals(roleName, SuperAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformOwner, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformSuperAdmin, System.StringComparison.OrdinalIgnoreCase);

        public static bool IsPlatformRole(string? roleName) =>
            string.Equals(roleName, SupportAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, SupportReadOnly, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, SuperAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformOwner, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, BillingAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, SecurityAuditor, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformReadOnly, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformSuperAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformOpsAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformBillingAdmin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, PlatformSupportAgent, System.StringComparison.OrdinalIgnoreCase);
    }
}
