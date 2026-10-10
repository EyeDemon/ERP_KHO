# INV-09 — Truy vấn danh sách điều chuyển và phân trang an toàn

## Phạm vi đã triển khai trong PR #31

- `GET /api/stock-transfers` vẫn lấy dữ liệu từ SQL Server trong phạm vi kho người gọi được cấp quyền; mọi filter được thực hiện trước khi phân trang.
- Giới hạn `pageSize` từ 1 tới 100; `pageIndex` tối thiểu 1. Tính offset bằng `long` và trả trang rỗng khi offset vượt tổng bản ghi. Không phát lệnh SQL `Skip` với offset âm do tràn số nguyên.
- Sắp xếp `CreatedAt DESC, Id DESC` để không bị lặp hoặc bỏ sót dòng giữa các trang khi hai phiếu có cùng thời điểm tạo (không cam kết snapshot bất biến nếu dữ liệu bị sửa đồng thời).
- Filter `toDate` là ngày lịch, bao gồm trọn ngày bằng ngưỡng loại trừ của ngày kế tiếp; nếu là `DateTime.MaxValue` thì bỏ ngưỡng trên thay vì làm tràn `AddDays`.
- Kết quả vẫn trả `TotalRecords`, `PageIndex`, `PageSize` đúng contract; trang ngoài phạm vi trả `Items=[]`, không phải HTTP 500.

## Bằng chứng kiểm thử

`SqlServerStockTransferQuerySafetyTests`: kiểm thử trang `int.MaxValue` với `pageSize=100`; truy vấn `DateTime.MaxValue`; hai bản ghi cùng `CreatedAt` phân trang ổn định và filter ngày bao gồm bản ghi trong ngày.

## Ranh giới nghiệm thu

- Không đổi state machine, posting, ledger, khóa giao dịch hoặc quyền kho của Stock Transfer.
- Không nâng INV-09/INV-10 lên `live`; đây là hardening truy vấn của hệ thống thật, không phải chứng nhận đầy đủ luồng Reversal/Traceability.
- Chỉ công nhận khi CI đúng HEAD PASS; browser QA và staging API/SQL thật vẫn cần bằng chứng độc lập.
- Không sửa Notion; UX/UI tiếng Việt, tên API/DTO/code giữ tiếng Anh.
