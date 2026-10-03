export const validRejectReason = (value: string): boolean => {
  const reason = value.trim();
  const hasControlCharacter = [...reason].some((character) => {
    const code = character.charCodeAt(0);
    return code < 32 || code === 127;
  });
  return reason.length >= 3 && reason.length <= 500 && !hasControlCharacter && !/<[^>]*>/.test(reason);
};

export const approvalDisplayAction = (action: string): string => {
  if (['Bị từ chối', 'Đã hủy', 'Đã duyệt'].includes(action)) return action;
  if (action === 'ApprovalRejected') return 'Bị từ chối';
  if (action.endsWith('.Cancelled')) return 'Đã hủy';
  if (action.endsWith('.Approved')) return 'Đã duyệt';
  if (action.endsWith('.Rejected')) return 'Thao tác không được chấp nhận';
  return 'Đã cập nhật chứng từ';
};
