namespace PosSaas.Domain.Common;

public enum OrderType { DineIn, Takeaway, Delivery }

public enum OrderStatus { Open, Confirmed, Completed, Cancelled, Refunded }

public enum PaymentMethod { Cash, Upi, Card, Wallet }

public enum PaymentStatus { Pending, Success, Failed }

public enum TableStatus { Available, Occupied, Reserved, Cleaning }

public enum StockMovementType { PurchaseIn, SaleOut, AdjustmentIn, AdjustmentOut, TransferIn, TransferOut }

/// <summary>Matches the wire values mobile/src/sync/syncEngine.ts's SyncChange.operation actually sends - "Insert", not "Create".</summary>
public enum SyncOperation { Insert, Update, Delete }

public enum SyncEntityStatus { Pending, Applied, Conflict, Failed }

public enum SubscriptionStatus { Trialing, Active, PastDue, Cancelled }

public enum BackupStatus { Pending, InProgress, Completed, Failed }

/// <summary>Section 7's multi-step verify/safety-backup/swap/rollback restore state machine.</summary>
public enum RestoreStatus { NotStarted, Verifying, SafetyBackup, Restoring, Completed, Failed, RolledBack }

public enum DeviceKind { MobilePos, KitchenDisplay, PrinterGateway }
