namespace PosSaas.Api.Dtos;

public record CreateOrderItemRequest(
    Guid ProductId,
    Guid? ProductVariantId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    string? Notes);

public record CreateOrderRequest(
    Guid BranchId,
    Guid? DeviceId,
    string Type,
    Guid? CustomerId,
    Guid? TableId,
    decimal DiscountTotal,
    List<CreateOrderItemRequest> Items);

public record CartItemDto(
    Guid ProductId,
    Guid? ProductVariantId,
    string ProductNameSnapshot,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    string? Notes);

public record OrderDto(
    Guid Id,
    string OrderNumber,
    string Type,
    string Status,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    DateTime OrderedAtUtc,
    List<CartItemDto> Items);

/// <summary>ClientPaymentId mirrors CreateOrderRequest's clientOrderId query param (PosController.
/// CreateOrder) - offline checkout (mobile/src/screens/Pos/PosScreen.tsx) generates it up front and
/// retries this same request once connectivity returns (mobile/src/sync/syncEngine.ts), so without
/// an idempotency key a lost response (payment recorded, ack never arrived) would double-charge on
/// retry. Optional/nullable so every existing online-only caller keeps working unchanged.</summary>
public record PaymentRequest(Guid OrderId, string Method, decimal Amount, decimal? TenderedAmount, Guid? ClientPaymentId = null);
