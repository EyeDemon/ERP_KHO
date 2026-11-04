namespace ERP.Domain.Entities;

public sealed class Permission
{
    public int Id { get; set; }
    public string Code { get; private set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    private Permission() { }
    public Permission(string code, string description = "")
    {
        Code = Normalize(code);
        Description = description.Trim();
    }

    public static string Normalize(string code)
    {
        var value = code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length is < 3 or > 100 || !value.Contains('.') ||
            value.StartsWith('.') || value.EndsWith('.') || value.Split('.').Any(x => x.Length == 0) ||
            value.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_')))
            throw new ArgumentException("Mã quyền phải viết thường theo dạng resource.action.", nameof(code));
        return value;
    }
}
