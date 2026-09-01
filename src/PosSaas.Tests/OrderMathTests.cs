using PosSaas.Domain.Common;
using Xunit;

namespace PosSaas.Tests;

/// <summary>
/// Covers the checkout math extracted from PosController.CreateOrder into
/// PosSaas.Domain/Common/OrderMath.cs (see that file's doc comment - a pure refactor, same
/// formulas, now independently testable).
/// </summary>
public class OrderMathTests
{
    [Fact]
    public void CalculateLineTotal_NormalSale()
    {
        // 2 x 15.00 + 1.50 tax, no discount.
        var total = OrderMath.CalculateLineTotal(quantity: 2, unitPrice: 15m, discountAmount: 0m, taxAmount: 1.5m);

        Assert.Equal(31.5m, total);
    }

    [Fact]
    public void CalculateLineTotal_WithDiscount()
    {
        // 3 x 10.00 - 5.00 discount + 2.00 tax.
        var total = OrderMath.CalculateLineTotal(quantity: 3, unitPrice: 10m, discountAmount: 5m, taxAmount: 2m);

        Assert.Equal(27m, total);
    }

    [Fact]
    public void CalculateLineTotal_ZeroTax()
    {
        var total = OrderMath.CalculateLineTotal(quantity: 1, unitPrice: 20m, discountAmount: 0m, taxAmount: 0m);

        Assert.Equal(20m, total);
    }

    [Fact]
    public void CalculateGrandTotal_NoDiscountNoTax_EqualsSubTotal()
    {
        var grandTotal = OrderMath.CalculateGrandTotal(subTotal: 100m, discountTotal: 0m, taxTotal: 0m);

        Assert.Equal(100m, grandTotal);
    }

    [Fact]
    public void ThreeLineItems_SumToCorrectSubTotalAndGrandTotal()
    {
        var line1 = OrderMath.CalculateLineTotal(2, 15m, 0m, 1.5m);   // (2*15) - 0 + 1.5 = 31.5
        var line2 = OrderMath.CalculateLineTotal(1, 50m, 5m, 4.5m);   // (1*50) - 5 + 4.5 = 49.5
        var line3 = OrderMath.CalculateLineTotal(3, 8m, 0m, 2.4m);    // (3*8)  - 0 + 2.4 = 26.4

        Assert.Equal(31.5m, line1);
        Assert.Equal(49.5m, line2);
        Assert.Equal(26.4m, line3);

        var subTotal = (2 * 15m) + (1 * 50m) + (3 * 8m); // 104.0
        var taxTotal = 1.5m + 4.5m + 2.4m;               // 8.4
        var discountTotal = 5m;

        var grandTotal = OrderMath.CalculateGrandTotal(subTotal, discountTotal, taxTotal);

        Assert.Equal(104m, subTotal);
        Assert.Equal(107.4m, grandTotal);
    }
}
