# INV-09 — Truy vết theo kho được cấp quyền

## Chế độ tìm kiếm

- Giao diện `/inventory-traceability` cho phép chỉ nhập **ID kho** để xem nhóm tồn kho hiện tại và các sự kiện sổ cái mới nhất của kho đó.
- Không nhập bất kỳ kho, sản phẩm, lô, sê-ri hoặc cặp chứng từ nào vẫn bị **từ chối**; không cho tìm kiếm mọi kho mà không có điều kiện.
- `GET /api/inventory/traceability?warehouseId=<id>&limit=200` sử dụng quyền `inventory_traceability.read`; backend phải xác minh quyền truy cập **kho được chỉ định**, không dựa vào tính năng lọc trên frontend.
- Có thể kết hợp kho với sản phẩm/lô/sê-ri hoặc cặp `referenceType` + `referenceId`. Hai trường chứng từ luôn phải đi cùng nhau.

## Phân biệt tồn hiện tại với lịch sử

- **Tra theo kho đơn lẻ:** lấy các nhóm tồn *trực tiếp* từ `InventoryStocks`, kể cả nhóm chưa có sự kiện mới hoặc sự kiện không còn trong cửa sổ kết quả; sự kiện sổ cái là dòng thời gian riêng.
- **Tra theo chứng từ mà không chọn sản phẩm/lô/sê-ri:** chỉ lấy nhóm tồn khớp chính xác định danh từ các sự kiện tham chiếu (sản phẩm/kho/lô/sê-ri), áp dụng điều kiện trước khi giới hạn kết quả SQL. Khi không có sự kiện tham chiếu thì không đoán ra tồn kho liên quan.
- **Tra theo sản phẩm/lô/sê-ri:** giữ bộ lọc hiện hành và quyền kho.
- Sự kiện gần nhất bị giới hạn bởi tham số `limit` (mặc định 200, tối đa 500); chuỗi đảo liên quan vẫn được nối khi cần để không đứt phả hệ.
- Nhóm tồn hiện tại có phân trang `bucketOffset=0,500,1000,...,50000`, mặc định 0; nhận offset âm, lệch bước 500 hoặc vượt 50000 phải báo lỗi trước khi truy vấn kho. Mỗi trang trả tối đa **500** dòng, đọc thêm dòng 501 để bật `bucketsTruncated` (còn trang sau); sắp xếp xác định theo sản phẩm, kho, vị trí, trạng thái, lô, sê-ri và ID tồn kho trước `Skip`/`Take`. Giao diện có nút **Trang trước / Trang sau**, vô hiệu khi đang tải, đặt lại trang đầu khi thay đổi điều kiện lọc và bỏ qua kết quả trả về muộn của truy vấn cũ. Quá giới hạn trang cần lọc chi tiết thay vì khẳng định đủ tồn kho. Kết hợp kho và tham chiếu không tồn tại trả rỗng thay vì mở rộng tới toàn bộ kho; giao diện cảnh báo rõ, không biến danh sách 500 thành tuyên bố toàn bộ kho đã được xem.
- Không cộng số lượng hoặc sửa sổ cái trong API này: toàn bộ thao tác **read-only**.

## Kiểm thử và nghiệm thu

- SQL Server: truy theo kho khi không có giao dịch sổ cái vẫn trả nhóm tồn hiện hữu; gọi kho không được phép trả NotFound; trường hợp 501 nhóm tồn bật cờ bị cắt.
- SQL Server: lọc theo chứng từ vẫn tìm đúng nhóm tồn sau vị trí thứ 500, không trả nhóm lô/serial không thuộc tham chiếu.
- API: truyền đúng `warehouseId` đến service, không bỏ hoặc mở rộng bộ lọc.
- Frontend: truy bằng kho đơn lẻ gửi request đúng, vẫn chặn yêu cầu trống.
- Chỉ công nhận PASS khi CI đúng HEAD thành công; trình duyệt và staging API/SQL thật cần nghiệm thu riêng. Vercel Preview demo READY không xác nhận backend staging.
- Notion là nguồn đặc tả **chỉ đọc**, không chỉnh sửa khi triển khai phần này.

## Chọn kho theo dữ liệu thật — mốc 09/10/2026

- Màn `/inventory-traceability` hiển thị một danh sách **mã kho — tên kho** từ `GET /api/inventory/traceability-warehouses`; không yêu cầu người dùng biết ID kho.
- API mới yêu cầu `inventory_traceability.read` và chỉ lấy các kho nằm trong `GetAccessibleWarehouseIdsAsync`. **Không** dùng `reversal-warehouses` vì API đó có quyền `inventory_ledger.read` khác; không làm rò kho ở màn truy vết.
- Chọn một kho để xem các nhóm tồn trong kho; chọn **Tất cả kho được phân quyền** chỉ hợp lệ khi nhập thêm sản phẩm, lô, sê-ri hoặc cặp tham chiếu. Không gửi truy vấn hoàn toàn trống.
- Khi đang tải danh sách kho, selector bị khóa và có trạng thái tải; lỗi tải danh sách phải hiển thị lỗi, khóa selector và cung cấp nút **Tải lại danh sách kho**, không tự tạo kho hoặc hiện kho giả.
- Danh sách trả về rỗng thì thông báo chưa được phân quyền, không tự động nới phạm vi truy vấn. Backend luôn xác minh quyền truy vết/kho lại khi truy vấn nhóm tồn hoặc dòng thời gian.
- Frontend từ chối payload danh sách kho sai hợp đồng: ID phải là số nguyên dương và không trùng; mã/tên kho là chuỗi có nội dung thực. Lỗi dữ liệu hoặc HTTP không được hiển thị danh sách giả. Kiểm thử SQL tạo mã kho giả nằm trong giới hạn `Warehouses.Code` 20 ký tự đúng schema thật.
- Giao diện Việt ngữ, dùng `UiToolbarField`, select native hỗ trợ bàn phím, focus rõ ràng và bố cục responsive theo `design-system/erp-wms/MASTER.md`.
- Kiểm thử: SQL Server chỉ trả kho trong quyền; thu hồi quyền khi đang mở trang phải làm danh sách rỗng và truy vết kho cũ trả NotFound; API chuyển đúng tới dịch vụ truy vết + kiểm tra annotation `InventoryTraceabilityRead`; frontend có loading, danh sách kho thật, lỗi, retry, danh sách rỗng; module HTTP từ chối payload sai kiểu.
- CI đúng HEAD, browser QA và staging backend/SQL là các cổng nghiệm thu riêng. Không merge PR Draft khi chưa có bằng chứng. Notion chỉ đọc.

## Đồng bộ môi trường Blueprint (demo chỉ đọc)

- Blueprint Vercel dùng **dữ liệu minh họa tách biệt**, chỉ trả GET `/api/inventory/traceability-warehouses` để bộ chọn kho có thể hiển thị. Không biến dữ liệu mẫu thành dữ liệu SQL thật.
- Adapter truy vết của demo hỗ trợ lọc theo kho, tham chiếu, `limit`, `bucketOffset` và hai cờ `eventsTruncated`/`bucketsTruncated`; khi tìm theo kho không được loại các nhóm tồn chỉ vì thiếu giao dịch sổ cái mẫu.
- POST/PUT/PATCH/DELETE trên Blueprint vẫn trả 405; mọi quyền, tồn kho và sổ cái thật phải được xác minh trên backend có xác thực và SQL staging riêng.

## Khóa chặt định danh chứng từ khi kết hợp bộ lọc — 09/10/2026

- Cặp `referenceType/referenceId` **luôn** được áp dụng như điều kiện giao với kho, sản phẩm, lô và sê-ri; không được bỏ lọc chứng từ chỉ vì có thêm điều kiện khác. Chứng từ không tồn tại trả nhóm tồn rỗng, kể cả khi sản phẩm đang có tồn.
- Các định danh nhóm tồn liên quan chứng từ lấy từ **toàn bộ sự kiện trực tiếp của chứng từ trong phạm vi kho được cấp quyền**, không chỉ từ 20/50/200/500 sự kiện gần nhất của dòng thời gian. Cờ `eventsTruncated` chỉ phản ánh giới hạn lịch sử hiển thị, không được cắt nhóm tồn liên quan chứng từ.
- Nhóm tồn được đối chiếu bằng sản phẩm/kho/lô/sê-ri, giữ khả năng truy vết hàng đã di chuyển vị trí; không suy ra nhóm tồn từ dấu đảo của chứng từ khác. Giới hạn 500 nhóm/trang và quyền kho vẫn do SQL áp dụng trước phân trang.
- Frontend từ chối ID sản phẩm/tham chiếu không phải số nguyên dương an toàn trước khi gửi GET; thông báo lỗi tiếng Việt và đưa focus tới trường cần sửa.
- Regression SQL: 26 sự kiện cùng chứng từ, sự kiện lô cũ nằm ngoài cửa sổ 20 vẫn tìm được nhóm tồn; kết hợp kho+sản phẩm+chứng từ hợp lệ và chứng từ không tồn tại; lọc lô+chứng từ. API xác minh chuyển đủ tham số; Vitest kiểm tra giao diện và ID sai.
- Chưa nghiệm thu browser QA hoặc staging SQL/API thật; CI trên đúng HEAD là cổng riêng. Notion chỉ đọc.

## Phân trang lịch sử sổ cái — 09/10/2026

- API đọc `GET /api/inventory/traceability` nhận thêm `eventOffset` (mặc định 0) độc lập với `bucketOffset`. Mỗi trang truy hồi `limit` giao dịch trực tiếp theo `TransactionDate DESC, TransactionId DESC`, đọc thêm 1 để xác định còn sự kiện cũ hơn.
- Chỉ chấp nhận `eventOffset` từ 0 tới 50.000 và chia hết cho `limit` hiệu lực (mặc định 200, tối đa 500); lỗi offset phải dừng **trước** truy vấn quyền kho và SQL.
- Bổ sung điều hướng `Sự kiện trước / Sự kiện sau`, đặt lại trang khi tìm điều kiện mới hoặc chuyển deep link; phản hồi muộn không được ghi đè kết quả mới. Dữ liệu lịch sử và nhóm tồn phân trang độc lập.
- Khi một trang chứa dấu đảo/giao dịch gốc, tiếp tục ghép các sự kiện liên kết từ ledger được phân quyền, ngay cả khi chúng nằm ngoài cửa sổ trang; vì vậy số sự kiện trả có thể vượt `limit` mà không biểu thị một trang vô hạn.
- Demo Vercel vẫn chỉ đọc. Lọc nhóm tồn theo chứng từ sử dụng toàn bộ sự kiện trực tiếp khớp chứng từ trước phân trang, không phụ thuộc `eventOffset` và không mở rộng điều kiện kho/sản phẩm/lô/sê-ri.
- Regression: SQL Server trang cũ hơn vẫn ghép đúng giao dịch hoàn trả có `ReversalOfTransactionId`; backend từ chối offset không hợp lệ trước tra cứu quyền; API truyền đủ 2 offset; Vitest kiểm tra chuyển trang/quay lại, demo các trang không trùng và offset không hợp lệ.
- Chưa có phả hệ hoàn chỉnh Receipt → QC → Move → Pick → Shipment/Return/Recall hoặc nghiệm thu staging SQL/API và browser E2E, do đó INV-10 tiếp tục `foundation`; Notion chỉ đọc.
