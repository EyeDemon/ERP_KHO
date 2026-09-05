export const validRejectReason = (value: string): boolean => {
  const reason = value.trim();
  const hasControlCharacter = [...reason].some((character) => {
    const code = character.charCodeAt(0);
    return code < 32 || code === 127;
  });
  return reason.length >= 3 && reason.length <= 500 && !hasControlCharacter && !/<[^>]*>/.test(reason);
};

export const approvalDisplayAction = (action: string): string => {
  if (action === 'ApprovalRejected') return 'Bị từ chối';
  if (action.endsWith('.Cancelled')) return 'Đã hủy';
  return action;
};
