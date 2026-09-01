using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Security;

namespace PosSaas.Infrastructure.Persistence;

/// <summary>
/// Seeds one demo tenant ("Mugavai Bakery") with a full year of realistic activity so every
/// screen/report in the mobile app and every endpoint in Scalar has real data to show, not an
/// empty state. Written against <see cref="IRepository{T}"/>'s AddAsync (not the
/// InMemoryRepository-specific bulk `Seed` helper) so it runs identically whether
/// <see cref="PosSaasStore"/> is backed by EF Core/PostgreSQL or the in-memory store - see
/// PosSaasStore's doc comment for both paths. Checks for existing data first so re-running this
/// against a persisted database (Program.cs calls it after `EnsureCreated` on every startup)
/// does not insert duplicate rows on every restart.
///
/// Deliberately NOT seeded: Permission/RolePermission (the fixed permission catalog isn't read
/// by any endpoint yet - [Authorize(Roles=...)] checks the JWT's role claim directly, not this
/// table) and SyncQueueItem (that table only makes sense as a replay log a real device produced,
/// not something to fabricate).
///
/// A year of order history means a few thousand inserts, and EfRepository.AddAsync does one
/// SaveChangesAsync per call (see its own doc comment) - expect this to take some tens of
/// seconds on first run against PostgreSQL. It only ever runs once per database.
/// </summary>
public static class SeedData
{
    public static async Task Seed(PosSaasStore store)
    {
        if ((await store.Tenants.GetAllAsync(null)).Any())
        {
            return; // already seeded (e.g. a persisted PostgreSQL database from a prior run)
        }

        var rng = new Random(20260901); // fixed seed - the demo looks the same on every fresh database

        var tenant = new Tenant
        {
            Name = "Mugavai Bakery",
            LegalName = "Mugavai Bakery Pvt Ltd",
            BusinessType = "Bakery & Cafe",
            DefaultCurrency = "INR"
        };
        await store.Tenants.AddAsync(tenant);

        var branch = new Branch { TenantId = tenant.Id, Name = "MG Road Outlet", Address = "12 MG Road, Bengaluru" };
        await store.Branches.AddAsync(branch);

        var devices = new[]
        {
            new Device { TenantId = tenant.Id, BranchId = branch.Id, Name = "Front Counter Tablet", Kind = DeviceKind.MobilePos, LastSyncedAtUtc = DateTime.UtcNow.AddMinutes(-14) },
            new Device { TenantId = tenant.Id, BranchId = branch.Id, Name = "Kitchen Display", Kind = DeviceKind.KitchenDisplay, LastSyncedAtUtc = DateTime.UtcNow.AddHours(-2) },
        };
        foreach (var device in devices) await store.Devices.AddAsync(device);

        // --- Roles & staff --------------------------------------------------------------
        var ownerRole = new Role { TenantId = tenant.Id, Name = "Owner", IsSystemRole = true };
        var managerRole = new Role { TenantId = tenant.Id, Name = "Manager", IsSystemRole = true };
        var cashierRole = new Role { TenantId = tenant.Id, Name = "Cashier", IsSystemRole = true };
        await store.Roles.AddAsync(ownerRole);
        await store.Roles.AddAsync(managerRole);
        await store.Roles.AddAsync(cashierRole);

        // Same password for all three so the demo login card in README/LoginScreen only needs
        // to mention one - owner@demo.pos is the one Program.cs's root response advertises.
        const string demoPassword = "Demo@123";
        var owner = new User { TenantId = tenant.Id, BranchId = branch.Id, FullName = "Anitha Rao", Email = "owner@demo.pos", PasswordHash = PasswordHasher.Hash(demoPassword), RoleId = ownerRole.Id };
        var manager = new User { TenantId = tenant.Id, BranchId = branch.Id, FullName = "Suresh Kumar", Email = "manager@demo.pos", PasswordHash = PasswordHasher.Hash(demoPassword), RoleId = managerRole.Id };
        var cashier = new User { TenantId = tenant.Id, BranchId = branch.Id, FullName = "Divya Prakash", Email = "cashier@demo.pos", PasswordHash = PasswordHasher.Hash(demoPassword), RoleId = cashierRole.Id };
        await store.Users.AddAsync(owner);
        await store.Users.AddAsync(manager);
        await store.Users.AddAsync(cashier);
        var staffIds = new[] { cashier.Id, cashier.Id, cashier.Id, manager.Id, owner.Id }; // weighted - cashier rings up most sales

        // --- Catalog ----------------------------------------------------------------------
        var unitPiece = new Unit { TenantId = tenant.Id, Name = "Piece", ShortCode = "pc" };
        var unitKg = new Unit { TenantId = tenant.Id, Name = "Kilogram", ShortCode = "kg" };
        var unitBox = new Unit { TenantId = tenant.Id, Name = "Box", ShortCode = "box" };
        await store.Units.AddAsync(unitPiece);
        await store.Units.AddAsync(unitKg);
        await store.Units.AddAsync(unitBox);

        var catBreads = new Category { TenantId = tenant.Id, Name = "Breads" };
        var catCakes = new Category { TenantId = tenant.Id, Name = "Cakes" };
        var catPastries = new Category { TenantId = tenant.Id, Name = "Pastries & Cookies" };
        var catSavouries = new Category { TenantId = tenant.Id, Name = "Savouries" };
        var catBeverages = new Category { TenantId = tenant.Id, Name = "Beverages" };
        foreach (var category in new[] { catBreads, catCakes, catPastries, catSavouries, catBeverages })
        {
            await store.Categories.AddAsync(category);
        }

        (string Name, Category Category, Unit Unit, decimal Price, decimal TaxPercent)[] catalog =
        {
            ("Milk Bread Loaf", catBreads, unitPiece, 55m, 5m),
            ("Multigrain Loaf", catBreads, unitPiece, 75m, 5m),
            ("Garlic Bread", catBreads, unitPiece, 90m, 5m),
            ("Pav (pack of 6)", catBreads, unitPiece, 40m, 5m),
            ("Chocolate Truffle Cake", catCakes, unitKg, 650m, 12m),
            ("Red Velvet Cake", catCakes, unitKg, 750m, 12m),
            ("Black Forest Cake", catCakes, unitKg, 600m, 12m),
            ("Pineapple Cake", catCakes, unitKg, 550m, 12m),
            ("Fresh Cream Cupcake", catCakes, unitPiece, 45m, 12m),
            ("Butter Croissant", catPastries, unitPiece, 70m, 5m),
            ("Chocolate Brownie", catPastries, unitPiece, 60m, 5m),
            ("Danish Pastry", catPastries, unitPiece, 80m, 5m),
            ("Chocolate Chip Cookies (box)", catPastries, unitBox, 150m, 5m),
            ("Butter Cookies (box)", catPastries, unitBox, 130m, 5m),
            ("Vanilla Muffin", catPastries, unitPiece, 55m, 5m),
            ("Veg Puff", catSavouries, unitPiece, 35m, 5m),
            ("Egg Puff", catSavouries, unitPiece, 40m, 5m),
            ("Vegetable Samosa", catSavouries, unitPiece, 20m, 5m),
            ("Cheese Sandwich", catSavouries, unitPiece, 85m, 5m),
            ("Masala Chai", catBeverages, unitPiece, 25m, 5m),
            ("Filter Coffee", catBeverages, unitPiece, 40m, 5m),
            ("Cold Coffee", catBeverages, unitPiece, 90m, 5m),
            ("Fresh Lime Soda", catBeverages, unitPiece, 60m, 5m),
        };

        var products = new List<Product>();
        foreach (var (name, category, unit, price, taxPercent) in catalog)
        {
            var product = new Product
            {
                TenantId = tenant.Id,
                CategoryId = category.Id,
                UnitId = unit.Id,
                Name = name,
                Sku = $"MB-{name.Replace(" ", "").Substring(0, Math.Min(6, name.Replace(" ", "").Length)).ToUpperInvariant()}",
                BasePrice = price,
                TaxRatePercent = taxPercent
            };
            await store.Products.AddAsync(product);
            products.Add(product);
        }

        // A couple of variants and barcodes so the Products screen's scan flow has something to find.
        var truffleCake = products.First(p => p.Name == "Chocolate Truffle Cake");
        await store.ProductVariants.AddAsync(new ProductVariant { TenantId = tenant.Id, ProductId = truffleCake.Id, Name = "Half Kg", PriceDelta = -325m });
        await store.ProductVariants.AddAsync(new ProductVariant { TenantId = tenant.Id, ProductId = truffleCake.Id, Name = "2 Kg", PriceDelta = 650m });
        for (var i = 0; i < 8; i++)
        {
            await store.Barcodes.AddAsync(new Barcode { TenantId = tenant.Id, ProductId = products[i].Id, Code = $"890{100000 + i * 37}" });
        }

        await store.Modifiers.AddAsync(new Modifier { TenantId = tenant.Id, Name = "Extra Cheese", PriceDelta = 20m });
        await store.Modifiers.AddAsync(new Modifier { TenantId = tenant.Id, Name = "Less Sugar", PriceDelta = 0m });
        await store.Modifiers.AddAsync(new Modifier { TenantId = tenant.Id, Name = "Eggless", PriceDelta = 15m });

        await store.Printers.AddAsync(new Printer
        {
            TenantId = tenant.Id,
            BranchId = branch.Id,
            Name = "Front Counter Printer",
            BleServiceUuid = "000018f0-0000-1000-8000-00805f9b34fb",
            BleCharacteristicUuid = "00002af1-0000-1000-8000-00805f9b34fb"
        });

        // --- Customers & tables -------------------------------------------------------------
        (string Name, string Phone)[] customerSeed =
        {
            ("Ravi Shankar", "9845011223"), ("Priya Menon", "9845022334"), ("Arjun Nair", "9845033445"),
            ("Sneha Iyer", "9845044556"), ("Kiran Reddy", "9845055667"), ("Lakshmi Narayan", "9845066778"),
            ("Vikram Singh", "9845077889"), ("Ananya Das", "9845088990"), ("Rohit Sharma", "9845099001"),
            ("Meera Pillai", "9845100112"), ("Suresh Babu", "9845111223"), ("Deepa Krishnan", "9845122334"),
            ("Karthik Raja", "9845133445"), ("Nisha Verma", "9845144556"), ("Manoj Gowda", "9845155667"),
        };
        var customers = new List<Customer>();
        foreach (var (name, phone) in customerSeed)
        {
            var customer = new Customer
            {
                TenantId = tenant.Id,
                Name = name,
                Phone = phone,
                Email = $"{name.Split(' ')[0].ToLowerInvariant()}@example.com",
                LoyaltyPoints = rng.Next(0, 40) * 5,
            };
            await store.Customers.AddAsync(customer);
            customers.Add(customer);
        }

        var tables = new List<RestaurantTable>();
        var tableStatuses = new[] { TableStatus.Available, TableStatus.Available, TableStatus.Available, TableStatus.Occupied, TableStatus.Reserved, TableStatus.Available, TableStatus.Available, TableStatus.Cleaning };
        for (var i = 1; i <= 8; i++)
        {
            var table = new RestaurantTable { TenantId = tenant.Id, BranchId = branch.Id, Name = $"T{i}", Capacity = i % 3 == 0 ? 6 : 4, Status = tableStatuses[i - 1] };
            await store.Tables.AddAsync(table);
            tables.Add(table);
        }

        // --- Running stock, seeded high enough to survive a year of sales between restocks ---
        var stockOnHand = products.ToDictionary(p => p.Id, _ => (decimal)rng.Next(150, 300));
        var reorderLevel = products.ToDictionary(p => p.Id, _ => (decimal)rng.Next(15, 30));

        var suppliers = new[] { "Bengaluru Flour Mills", "Fresh Dairy Co.", "Golden Grains Supplies", "Sunrise Packaging" };
        var today = DateTime.UtcNow.Date;

        // Monthly restock, oldest first - each one lifts a handful of products back up.
        for (var monthsAgo = 11; monthsAgo >= 0; monthsAgo--)
        {
            var purchaseDate = today.AddMonths(-monthsAgo);
            var lineItems = products.OrderBy(_ => rng.Next()).Take(rng.Next(4, 8))
                .Select(product => (Product: product, Quantity: rng.Next(40, 90), UnitCost: Math.Round(product.BasePrice * 0.45m, 2)))
                .ToList();

            // Purchase must exist before any PurchaseItem can reference its Id as a foreign key
            // (see PurchasesController.CreatePurchase, which adds the parent row first too).
            var purchase = new Purchase
            {
                TenantId = tenant.Id,
                BranchId = branch.Id,
                SupplierName = suppliers[rng.Next(suppliers.Length)],
                TotalCost = lineItems.Sum(l => l.Quantity * l.UnitCost),
            };
            await store.Purchases.AddAsync(purchase);

            foreach (var (product, quantity, unitCost) in lineItems)
            {
                await store.PurchaseItems.AddAsync(new PurchaseItem { TenantId = tenant.Id, PurchaseId = purchase.Id, ProductId = product.Id, Quantity = quantity, UnitCost = unitCost });
                await store.StockLedger.AddAsync(new StockLedger { TenantId = tenant.Id, BranchId = branch.Id, ProductId = product.Id, MovementType = StockMovementType.PurchaseIn, QuantityDelta = quantity });
                stockOnHand[product.Id] += quantity;
            }
        }

        // A few manual corrections (breakage, stock counts) scattered through the year.
        for (var i = 0; i < 6; i++)
        {
            var product = products[rng.Next(products.Count)];
            var delta = -rng.Next(2, 10);
            stockOnHand[product.Id] = Math.Max(0, stockOnHand[product.Id] + delta);
            await store.StockAdjustments.AddAsync(new StockAdjustment { TenantId = tenant.Id, BranchId = branch.Id, ProductId = product.Id, QuantityDelta = delta, Reason = "Breakage during handling" });
            await store.StockLedger.AddAsync(new StockLedger { TenantId = tenant.Id, BranchId = branch.Id, ProductId = product.Id, MovementType = StockMovementType.AdjustmentOut, QuantityDelta = delta, Note = "Breakage during handling" });
        }

        // --- A year of orders ---------------------------------------------------------------
        var orderTypes = new[] { OrderType.DineIn, OrderType.DineIn, OrderType.Takeaway, OrderType.Takeaway, OrderType.Delivery };
        var paymentMethods = new[] { PaymentMethod.Cash, PaymentMethod.Cash, PaymentMethod.Cash, PaymentMethod.Upi, PaymentMethod.Upi, PaymentMethod.Card };
        var completedOrders = new List<Order>();

        for (var daysAgo = 364; daysAgo >= 0; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);
            if (rng.NextDouble() < 0.08) continue; // ~8% closed/no-sales days across the year

            var isWeekend = date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
            var ordersToday = isWeekend ? rng.Next(2, 5) : rng.Next(0, 3);
            var dailySequence = 1;

            for (var i = 0; i < ordersToday; i++)
            {
                var orderedAt = date.AddHours(rng.Next(7, 21)).AddMinutes(rng.Next(0, 60));
                var type = orderTypes[rng.Next(orderTypes.Length)];
                var itemCount = rng.Next(1, 4);
                var lineProducts = products.OrderBy(_ => rng.Next()).Take(itemCount).ToList();

                var order = new Order
                {
                    TenantId = tenant.Id,
                    BranchId = branch.Id,
                    DeviceId = devices[rng.Next(devices.Length)].Id,
                    OrderNumber = $"ORD-{orderedAt:yyyyMMdd}-{dailySequence++:D4}",
                    Type = type,
                    Status = OrderStatus.Completed,
                    CustomerId = rng.NextDouble() < 0.55 ? customers[rng.Next(customers.Count)].Id : null,
                    TableId = type == OrderType.DineIn && rng.NextDouble() < 0.6 ? tables[rng.Next(tables.Count)].Id : null,
                    CreatedByUserId = staffIds[rng.Next(staffIds.Length)],
                    OrderedAtUtc = orderedAt,
                };

                decimal subTotal = 0, taxTotal = 0;
                foreach (var product in lineProducts)
                {
                    var quantity = rng.Next(1, 4);
                    var lineSubTotal = product.BasePrice * quantity;
                    var lineTax = Math.Round(lineSubTotal * (product.TaxRatePercent / 100m), 2);
                    subTotal += lineSubTotal;
                    taxTotal += lineTax;

                    order.Items.Add(new OrderItem
                    {
                        TenantId = tenant.Id,
                        OrderId = order.Id,
                        ProductId = product.Id,
                        ProductNameSnapshot = product.Name,
                        Quantity = quantity,
                        UnitPrice = product.BasePrice,
                        DiscountAmount = 0,
                        TaxAmount = lineTax,
                        LineTotal = OrderMath.CalculateLineTotal(quantity, product.BasePrice, 0, lineTax),
                    });

                    stockOnHand[product.Id] = Math.Max(0, stockOnHand[product.Id] - quantity);
                }

                // ~20% of orders carry a discount (a mix of round-percent and flat-amount, mirroring
                // PosScreen's own two discount modes) so Reports/Dashboard's new discount tracking
                // has something real to show rather than a flat zero across the whole year.
                var discountTotal = rng.NextDouble() < 0.2
                    ? (rng.NextDouble() < 0.5 ? Math.Round(subTotal * (new[] { 5, 10, 15 }[rng.Next(3)] / 100m), 2) : new[] { 20m, 50m, 100m }[rng.Next(3)])
                    : 0m;
                discountTotal = Math.Min(discountTotal, subTotal);

                order.SubTotal = subTotal;
                order.DiscountTotal = discountTotal;
                order.TaxTotal = taxTotal;
                order.GrandTotal = OrderMath.CalculateGrandTotal(subTotal, discountTotal, taxTotal);
                await store.Orders.AddAsync(order);

                foreach (var item in order.Items)
                {
                    await store.OrderItems.AddAsync(item);
                    await store.StockLedger.AddAsync(new StockLedger { TenantId = tenant.Id, BranchId = branch.Id, ProductId = item.ProductId, MovementType = StockMovementType.SaleOut, QuantityDelta = -item.Quantity });
                }

                var method = paymentMethods[rng.Next(paymentMethods.Length)];
                var payment = new Payment
                {
                    TenantId = tenant.Id,
                    OrderId = order.Id,
                    Method = method,
                    Status = PaymentStatus.Success,
                    Amount = order.GrandTotal,
                    TenderedAmount = method == PaymentMethod.Cash ? order.GrandTotal : null,
                    ChangeGiven = 0,
                };
                await store.Payments.AddAsync(payment);

                if (method != PaymentMethod.Cash)
                {
                    await store.PaymentTransactions.AddAsync(new PaymentTransaction
                    {
                        TenantId = tenant.Id,
                        OrderId = order.Id,
                        Gateway = method == PaymentMethod.Upi ? "UPI" : "Razorpay",
                        GatewayReference = $"TXN{rng.Next(100000000, 999999999)}",
                        Amount = order.GrandTotal,
                        Status = PaymentStatus.Success,
                    });
                }

                completedOrders.Add(order);
            }
        }

        // ~2% of the year's orders get refunded, mirroring PosController.RefundOrder's
        // reversal-order pattern rather than a destructive edit.
        var refundCandidates = completedOrders.OrderBy(_ => rng.Next()).Take(Math.Max(1, completedOrders.Count / 50)).ToList();
        foreach (var original in refundCandidates)
        {
            var reversal = new Order
            {
                TenantId = original.TenantId,
                BranchId = original.BranchId,
                DeviceId = original.DeviceId,
                OrderNumber = $"{original.OrderNumber}-R",
                Type = original.Type,
                Status = OrderStatus.Refunded,
                CustomerId = original.CustomerId,
                CreatedByUserId = original.CreatedByUserId,
                OrderedAtUtc = original.OrderedAtUtc.AddMinutes(rng.Next(5, 120)),
                ReversalOfOrderId = original.Id,
                SubTotal = -original.SubTotal,
                DiscountTotal = -original.DiscountTotal,
                TaxTotal = -original.TaxTotal,
                GrandTotal = -original.GrandTotal,
            };
            await store.Orders.AddAsync(reversal);

            original.Status = OrderStatus.Refunded;
            await store.Orders.UpdateAsync(original);
        }

        // Bias two products toward genuinely low stock so Dashboard/Inventory have something to flag.
        foreach (var product in products.Take(2))
        {
            stockOnHand[product.Id] = Math.Min(stockOnHand[product.Id], reorderLevel[product.Id] - 3);
        }

        foreach (var product in products)
        {
            await store.Inventory.AddAsync(new Inventory
            {
                TenantId = tenant.Id,
                BranchId = branch.Id,
                ProductId = product.Id,
                QuantityOnHand = Math.Max(0, stockOnHand[product.Id]),
                ReorderLevel = reorderLevel[product.Id],
                AverageCost = Math.Round(product.BasePrice * 0.45m, 2),
            });
        }

        // --- Backups & audit trail -----------------------------------------------------------
        for (var monthsAgo = 11; monthsAgo >= 0; monthsAgo--)
        {
            await store.Backups.AddAsync(new Backup
            {
                TenantId = tenant.Id,
                Checksum = $"seed-{monthsAgo:D2}-{rng.Next(100000, 999999)}",
                SizeBytes = rng.Next(500_000, 4_000_000),
                Version = 12 - monthsAgo,
                Status = BackupStatus.Completed,
            });
        }

        await store.AuditLogs.AddAsync(new AuditLog { TenantId = tenant.Id, UserId = owner.Id, Action = "Tenant.Created", EntityName = nameof(Tenant), EntityId = tenant.Id });
        await store.AuditLogs.AddAsync(new AuditLog { TenantId = tenant.Id, UserId = owner.Id, Action = "Subscription.TrialStarted", EntityName = nameof(Subscription) });
        await store.AuditLogs.AddAsync(new AuditLog { TenantId = tenant.Id, UserId = manager.Id, Action = "Product.BulkImported", EntityName = nameof(Product), DetailsJson = $"{{\"count\":{products.Count}}}" });
        await store.AuditLogs.AddAsync(new AuditLog { TenantId = tenant.Id, UserId = cashier.Id, Action = "Backup.Recorded", EntityName = nameof(Backup) });

        // --- Subscription & plans -------------------------------------------------------------
        var basicPlan = new SubscriptionPlan { Name = "Basic", MonthlyPriceInr = 0m, TrialDays = 30, FeaturesJson = "[\"pos\",\"inventory\"]" };
        var proPlan = new SubscriptionPlan { Name = "Pro", MonthlyPriceInr = 499m, TrialDays = 30, FeaturesJson = "[\"pos\",\"inventory\",\"cloud-sync\",\"auto-backup\",\"multi-device\",\"advanced-analytics\"]" };
        await store.SubscriptionPlans.AddAsync(basicPlan);
        await store.SubscriptionPlans.AddAsync(proPlan);

        // Deliberately close to expiry (3 days out) so the demo shows off SubscriptionBanner and
        // the SignalR push (Notifications/SubscriptionExpiryNotifier.cs) without waiting a year.
        await store.Subscriptions.AddAsync(new Subscription
        {
            TenantId = tenant.Id,
            PlanId = proPlan.Id,
            Status = SubscriptionStatus.Active,
            CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(3),
        });
    }
}
