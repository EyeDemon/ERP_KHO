using Microsoft.AspNetCore.Http;

namespace ERP.Api.Infrastructure;

public static class FileUploadSecurityValidator
{
    public const long MaxDocumentBytes = 10L * 1024 * 1024;
    public const long MaxSpreadsheetOrDocBytes = MaxDocumentBytes;
    public const long MaxVideoBytes = 50L * 1024 * 1024;

    public static Microsoft.AspNetCore.Mvc.IActionResult ToActionResult(
        this (bool IsValid, int StatusCode, string ErrorCode, string Message) validation,
        Microsoft.AspNetCore.Mvc.ControllerBase controller) =>
        controller.StatusCode(validation.StatusCode, new
        {
            code = validation.ErrorCode,
            errorCode = validation.ErrorCode,
            message = validation.Message
        });

    public static (bool IsValid, int StatusCode, string ErrorCode, string Message) ValidateReconcileEvidenceFile(IFormFile? file)
    {
        if (file is null || file.Length <= 0)
        {
            return (false, 422, "RECONCILE_ATTACHMENT_REQUIRED",
                "Bắt buộc đính kèm chứng từ ủy nhiệm chi/sao kê nhị phân thật (.pdf, .png, .jpg) qua multipart/form-data.");
        }

        if (file.Length > MaxDocumentBytes)
        {
            return (false, 413, "FILE_TOO_LARGE",
                "Tệp đính kèm vượt quá dung lượng tối đa cho phép (10 MB).");
        }

        Span<byte> header = stackalloc byte[8];
        using var stream = file.OpenReadStream();
        var read = stream.Read(header);
        if (read < 4)
        {
            return (false, 400, "INVALID_FILE_SIGNATURE",
                "Tệp đính kèm không hợp lệ hoặc bị hỏng chữ ký nhị phân (magic bytes).");
        }

        var isPdf = read >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D;
        var isPng = read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
        var isJpeg = read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

        if (!isPdf && !isPng && !isJpeg)
        {
            return (false, 400, "INVALID_FILE_SIGNATURE",
                "Định dạng chứng từ không hợp lệ. Chỉ chấp nhận tệp PDF (%PDF-), PNG hoặc JPEG nhị phân thật.");
        }

        return (true, 200, string.Empty, string.Empty);
    }

    public static (bool IsValid, int StatusCode, string ErrorCode, string Message) ValidateSpreadsheetOrCsvFile(IFormFile? file)
    {
        if (file is null || file.Length <= 0)
        {
            return (true, 200, string.Empty, string.Empty);
        }

        if (file.Length > MaxDocumentBytes)
        {
            return (false, 413, "FILE_TOO_LARGE",
                "Tệp bảng tính vượt quá giới hạn 10 MB.");
        }

        var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        Span<byte> header = stackalloc byte[8];
        using var stream = file.OpenReadStream();
        var read = stream.Read(header);
        if (read < 2)
        {
            return (false, 400, "INVALID_FILE_SIGNATURE", "Tệp bảng tính rỗng hoặc hỏng cấu trúc.");
        }

        var isZipXlsx = read >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
        if (ext == ".xlsx" && !isZipXlsx)
        {
            return (false, 400, "INVALID_FILE_SIGNATURE",
                "Tệp .xlsx không đúng chữ ký nhị phân OpenXML (PK\\x03\\x04).");
        }

        if (ext is ".csv" or ".txt")
        {
            for (var i = 0; i < read; i++)
            {
                if (header[i] == 0x00)
                {
                    return (false, 400, "INVALID_FILE_SIGNATURE",
                        "Tệp CSV chứa byte nhị phân không hợp lệ.");
                }
            }
        }
        else if (ext != ".xlsx" && !isZipXlsx)
        {
            return (false, 400, "UNSUPPORTED_FILE_TYPE",
                "Chỉ chấp nhận tệp bảng tính chuẩn .xlsx hoặc .csv.");
        }

        return (true, 200, string.Empty, string.Empty);
    }

    public static (bool IsValid, int StatusCode, string ErrorCode, string Message) ValidatePackingVideoFile(IFormFile? file)
    {
        if (file is null || file.Length <= 0)
        {
            return (true, 200, string.Empty, string.Empty);
        }

        if (file.Length > MaxVideoBytes)
        {
            return (false, 413, "VIDEO_FILE_TOO_LARGE",
                "Tệp video đóng gói vượt quá dung lượng tối đa 50 MB.");
        }

        Span<byte> header = stackalloc byte[12];
        using var stream = file.OpenReadStream();
        var read = stream.Read(header);
        var isWebM = read >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3;
        var isMp4 = read >= 8 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70;
        if (!isWebM && !isMp4)
        {
            return (false, 400, "INVALID_VIDEO_SIGNATURE",
                "Tệp video đóng gói không đúng chữ ký nhị phân chuẩn MP4 (ftyp) hoặc WebM.");
        }

        return (true, 200, string.Empty, string.Empty);
    }

    public static string SanitizeSpreadsheetCellFormula(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')
        {
            return "'" + value;
        }

        return value;
    }
}
