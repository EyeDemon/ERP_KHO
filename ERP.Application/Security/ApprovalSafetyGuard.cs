using ERP.Application.Exceptions;

namespace ERP.Application.Security;

public static class ApprovalSafetyGuard
{
    public static void EnsureDifferentChecker(int creatorUserId, int checkerUserId)
    {
        if (creatorUserId == checkerUserId)
            throw new ForbiddenException("Người tạo chứng từ không được tự duyệt chứng từ đó.");
    }
}
