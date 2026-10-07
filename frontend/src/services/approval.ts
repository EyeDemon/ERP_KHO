export const validRejectReason = (value: string): boolean => {
  const reason = value.trim();
  const hasControlCharacter = [...reason].some((character) => {
    const code = character.charCodeAt(0);
    return code < 32 || code === 127;
  });
  return reason.length >= 3 && reason.length <= 500 && !hasControlCharacter && !/<[^>]*>/.test(reason);
};

export const approvalDisplayAction = (action: string): string => {
  const exportActions: Record<string, string> = {
    'ExportReceipt.Created': 'Đã tạo phiếu xuất',
    'ExportReceipt.CustomerChanged': 'Đã cập nhật khách hàng',
    'ExportReceipt.ApprovedAndReserved': 'Đã duyệt và giữ hàng',
    'ExportReceipt.ApprovedAndDispatched': 'Đã duyệt và xuất ngay',
    'ExportReceipt.Dispatched': 'Đã xuất kho',
    'ExportReceipt.CancelledAndReleased': 'Đã hủy và giải phóng hàng giữ',
  };
  if (exportActions[action]) return exportActions[action];
  if (['Bị từ chối', 'Đã hủy', 'Đã duyệt'].includes(action)) return action;
  if (action === 'ApprovalRejected') return 'Bị từ chối';
  if (action.endsWith('.Cancelled')) return 'Đã hủy';
  if (action.endsWith('.Approved')) return 'Đã duyệt';
  if (action.endsWith('.Rejected')) return 'Thao tác không được chấp nhận';
  return 'Đã cập nhật chứng từ';
};
