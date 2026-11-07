export interface WmsGlossaryEntry {
  term: string;
  label: string;
  description: string;
}

export const WMS_GLOSSARY = {
  FEFO: { term: 'FEFO', label: 'Xuất theo hạn dùng gần nhất trước', description: 'Ưu tiên phân bổ và xuất kho lô hàng có ngày hết hạn sớm nhất trước để tránh cận hạn hoặc quá hạn.' },
  FIFO: { term: 'FIFO', label: 'Nhập trước xuất trước', description: 'Ưu tiên xuất kho lô hàng nhập vào kho sớm nhất trước theo thứ tự thời gian tiếp nhận.' },
  BOM: { term: 'BOM', label: 'Định mức nguyên vật liệu lắp ráp bộ', description: 'Danh mục linh kiện thành phần và định mức số lượng chuẩn để đóng gói hoặc xả bộ sản phẩm.' },
  WAC: { term: 'WAC', label: 'Giá vốn bình quân gia quyền di động', description: 'Phương pháp tính lại đơn giá vốn tồn kho bình quân sau mỗi giao dịch nhập kho được ghi sổ.' },
  AWB: { term: 'AWB', label: 'Mã vận đơn hãng vận chuyển', description: 'Mã định danh kiện hàng trên tem nhiệt dùng để bàn giao và tra cứu hành trình với đơn vị vận chuyển.' },
  'GS1-128': { term: 'GS1-128', label: 'Chuẩn mã vạch công nghiệp lô và hạn dùng', description: 'Chuẩn mã vạch công nghiệp chứa đồng thời mã hàng hóa, số lô sản xuất và ngày hết hạn trong một lần quét.' },
  RMA: { term: 'RMA', label: 'Phiếu ủy quyền trả hàng khách hoàn', description: 'Chứng từ tiếp nhận, kiểm định chất lượng và phân loại xử lý hàng do khách hàng hoàn trả về kho.' },
  MST: { term: 'MST', label: 'Mã số thuế doanh nghiệp / hộ kinh doanh', description: 'Mã định danh người nộp thuế 10 hoặc 13 chữ số phục vụ phát hành hóa đơn điện tử hợp lệ.' },
  SoD: { term: 'SoD', label: 'Phân tách nhiệm vụ kiểm soát chéo', description: 'Nguyên tắc bất kiêm nhiệm yêu cầu người duyệt chứng từ phải độc lập với người lập phiếu để chống tự duyệt.' },
  ĐVVC: { term: 'ĐVVC', label: 'Đơn vị vận chuyển giao nhận', description: 'Đối tác hãng vận chuyển hoặc đội xe nhận bàn giao kiện hàng tại cửa xuất để giao đến người nhận.' },
  ĐVT: { term: 'ĐVT', label: 'Đơn vị tính quy đổi hàng hóa', description: 'Đơn vị đo lường giao dịch (thùng, hộp, cái) được quy đổi thống nhất về đơn vị cơ sở trên sổ cái tồn kho.' },
  SKU: { term: 'SKU', label: 'Mã định danh mặt hàng tồn kho', description: 'Mã quản lý duy nhất cho từng phân loại sản phẩm dùng để theo dõi vị trí, lô hàng và số dư tồn kho.' },
  Bin: { term: 'Bin', label: 'Ô kệ / vị trí lưu trữ chi tiết trong kho', description: 'Tọa độ ô kệ vật lý nhỏ nhất trong kho dùng để cất hàng, phân bổ tồn kho và hướng dẫn lấy hàng.' },
} as const satisfies Record<string, WmsGlossaryEntry>;

export type WmsGlossaryKey = keyof typeof WMS_GLOSSARY;
