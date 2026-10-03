using ERP.Application.DTOs;
using ERP.Application.Validators;
using FluentAssertions;
using Xunit;

namespace ERP.Application.Tests
{
    public class ProductValidatorTests
    {
        private readonly CreateProductDtoValidator _createValidator = new();
        private readonly UpdateProductDtoValidator _updateValidator = new();

        [Fact]
        public void CreateProductDtoValidator_ValidDto_ShouldPassValidation()
        {
            var dto = new CreateProductDto
            {
                Code = "SP001",
                Name = "Sản phẩm 1",
                UnitId = 1,
                Description = "Mô tả hợp lệ"
            };

            var result = _createValidator.Validate(dto);

            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("", "Tên hợp lệ", 1)]
        [InlineData("SP001", "", 1)]
        [InlineData("SP001", "Tên hợp lệ", 0)]
        [InlineData("SP001", "Tên hợp lệ", -1)]
        public void CreateProductDtoValidator_InvalidDto_ShouldFailValidation(string code, string name, int unitId)
        {
            var dto = new CreateProductDto
            {
                Code = code,
                Name = name,
                UnitId = unitId
            };

            var result = _createValidator.Validate(dto);

            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void CreateProductDtoValidator_CodeExceeding50Chars_ShouldFailValidation()
        {
            var dto = new CreateProductDto
            {
                Code = new string('A', 51),
                Name = "Tên hợp lệ",
                UnitId = 1
            };

            var result = _createValidator.Validate(dto);

            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void UpdateProductDtoValidator_ValidDto_ShouldPassValidation()
        {
            var dto = new UpdateProductDto
            {
                Name = "Tên cập nhật",
                UnitId = 2,
                IsActive = true,
                Description = "Mô tả mới"
            };

            var result = _updateValidator.Validate(dto);

            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("", 1)]
        [InlineData("Tên", 0)]
        public void UpdateProductDtoValidator_InvalidDto_ShouldFailValidation(string name, int unitId)
        {
            var dto = new UpdateProductDto
            {
                Name = name,
                UnitId = unitId
            };

            var result = _updateValidator.Validate(dto);

            result.IsValid.Should().BeFalse();
        }
    }
}
