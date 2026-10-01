using ECommerce.Modules.Identity.Infrastructure;
using ECommerce.Modules.Promotion;
using Xunit;

namespace ECommerce.UnitTests;

public class BusinessLogicUnitTests
{
    [Fact]
    public void Coupon_PercentageDiscount_Should_Respect_MaxDiscountLimit()
    {
        // Giảm 20%, tối đa 50,000 VND
        var coupon = new Coupon
        {
            Code = "SAVE20",
            DiscountType = "PERCENTAGE",
            DiscountValue = 20,
            MinOrderValue = 100000,
            MaxDiscountAmount = 50000,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsActive = true
        };

        // Đơn 500k -> 20% là 100k -> nhưng bị trần 50k
        var discount = coupon.CalculateDiscount(500000);
        Assert.Equal(50000, discount);

        // Đơn 200k -> 20% là 40k -> nhỏ hơn trần 50k
        var discount2 = coupon.CalculateDiscount(200000);
        Assert.Equal(40000, discount2);

        // Đơn 50k -> Nhỏ hơn min 100k -> 0
        var discount3 = coupon.CalculateDiscount(50000);
        Assert.Equal(0, discount3);
    }

    [Fact]
    public void PasswordHasher_Should_Hash_And_Verify_Correctly()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var rawPassword = "StrongPassword@123!";

        var hash = hasher.HashPassword(rawPassword);
        Assert.NotEmpty(hash);

        var isValid = hasher.VerifyPassword(rawPassword, hash);
        Assert.True(isValid);

        var isInvalid = hasher.VerifyPassword("WrongPassword", hash);
        Assert.False(isInvalid);
    }
}
