# Hướng dẫn cấu hình Local Development

Để chạy ứng dụng ở máy local, bạn cần cấu hình chuỗi kết nối và JWT Secret. Không ghi trực tiếp các thông tin này vào file `appsettings.json` để tránh lọt secret.

### Cách 1: Sử dụng User Secrets (Khuyến nghị)
Di chuyển vào thư mục `ERP.Api` và chạy các lệnh sau:

```bash
cd ERP.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\mssqllocaldb;Database=ERP_KHO;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
dotnet user-secrets set "JwtSettings:Secret" "<generate-a-private-key-at-least-32-bytes>"
```

### Cách 2: Environment Variables
Bạn cũng có thể thiết lập thông qua biến môi trường trước khi chạy `dotnet run`:
```bash
$env:ConnectionStrings__DefaultConnection="Server=(localdb)\mssqllocaldb;Database=ERP_KHO;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$env:JwtSettings__Secret="<generate-a-private-key-at-least-32-bytes>"
```

## Authentication sessions

The API uses short-lived JWT access tokens and rotating refresh tokens in an `HttpOnly` cookie. See `docs/SESSION_MANAGEMENT_RUNBOOK.md` for the security model, migration commands, cookie/CORS constraints and rollback procedure.
