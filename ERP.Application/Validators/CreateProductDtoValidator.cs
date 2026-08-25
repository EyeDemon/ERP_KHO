using ERP.Application.DTOs;
using FluentValidation;

namespace ERP.Application.Validators
{
    public class CreateProductDtoValidator : AbstractValidator<CreateProductDto>
    {
        public CreateProductDtoValidator()
        {
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Mã sản phẩm là bắt buộc")
                .MaximumLength(50).WithMessage("Mã sản phẩm không được vượt quá 50 ký tự");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên sản phẩm là bắt buộc")
                .MaximumLength(200).WithMessage("Tên sản phẩm không được vượt quá 200 ký tự");

            RuleFor(x => x.UnitId)
                .GreaterThan(0).WithMessage("Đơn vị tính không hợp lệ");

            RuleFor(x => x.Description)
                .MaximumLength(500).WithMessage("Mô tả không được vượt quá 500 ký tự")
                .When(x => !string.IsNullOrEmpty(x.Description));
        }
    }
}
