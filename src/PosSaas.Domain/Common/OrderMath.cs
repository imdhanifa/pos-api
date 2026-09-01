namespace PosSaas.Domain.Common;

/// <summary>
/// Pure checkout math used by PosController.CreateOrder (POS Cart and Billing, Section 3,
/// 9, 16). Extracted out of the controller so it's testable without spinning up the web
/// host - see PosSaas.Tests/OrderMathTests.cs. Behavior is unchanged from the formulas that
/// used to be inlined in the controller; this is a pure refactor.
/// </summary>
public static class OrderMath
{
    /// <summary>
    /// One order line's total: quantity * unit price, minus any line-level discount,
    /// plus any line-level tax.
    /// </summary>
    public static decimal CalculateLineTotal(decimal quantity, decimal unitPrice, decimal discountAmount, decimal taxAmount)
        => (quantity * unitPrice) - discountAmount + taxAmount;

    /// <summary>
    /// Order grand total: subtotal minus the order-level discount, plus total tax summed
    /// across all lines.
    /// </summary>
    public static decimal CalculateGrandTotal(decimal subTotal, decimal discountTotal, decimal taxTotal)
        => subTotal - discountTotal + taxTotal;
}
