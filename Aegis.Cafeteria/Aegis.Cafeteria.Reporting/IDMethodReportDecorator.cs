namespace Aegis.Cafeteria.Reporting;

using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Aegis.Cafeteria.Reporting.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Adds the persisted Universal Search identification method to transaction-detail reports
/// without changing the existing financial/report calculation engine.
/// </summary>
public sealed class IDMethodReportDecorator(
    CafeteriaReportService inner,
    IDbContextFactory<LocalCafeteriaDbContext> localFactory) : ICafeteriaReportService
{
    public IReadOnlyList<ReportCatalogItem> Catalog => inner.Catalog;

    public async Task<ReportDocument> GenerateAsync(
        CafeteriaReportKind kind,
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ReportDocument document = await inner.GenerateAsync(kind, filter, cancellationToken);
        if (!IsTransactionDetailReport(kind)) return document;

        await using LocalCafeteriaDbContext db = await localFactory.CreateDbContextAsync(cancellationToken);
        Dictionary<int, CustomerRow> customers = await db.Customers.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        List<OrderRow> orders = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Refunds)
            .Where(x => x.CompletedAt.HasValue || x.VoidedAt.HasValue)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, OrderRow> byId = orders.ToDictionary(x => x.Id);
        ReportTable[] tables = document.Tables
            .Select(table => EnrichTable(kind, filter, table, orders, customers, byId))
            .ToArray();
        return document with { Tables = tables };
    }

    private static bool IsTransactionDetailReport(CafeteriaReportKind kind) => kind is
        CafeteriaReportKind.PurchaseItemDetail or
        CafeteriaReportKind.EmployeeStatement or
        CafeteriaReportKind.RefundVoidAudit;

    private static ReportTable EnrichTable(
        CafeteriaReportKind kind,
        ReportFilter filter,
        ReportTable table,
        IReadOnlyList<OrderRow> orders,
        IReadOnlyDictionary<int, CustomerRow> customers,
        IReadOnlyDictionary<Guid, OrderRow> byId)
    {
        if (!ShouldEnrichTable(kind, table)) return table;
        if (table.Columns.Any(x => string.Equals(x.Key, "idMethod", StringComparison.OrdinalIgnoreCase))) return table;

        ReportColumn[] columns = [.. table.Columns, new ReportColumn("idMethod", "ID Method")];
        IReadOnlyDictionary<string, object?>[] rows = table.Rows
            .Select(row => AddIDMethod(row, filter, orders, customers, byId))
            .ToArray();
        return table with { Columns = columns, Rows = rows };
    }

    private static bool ShouldEnrichTable(CafeteriaReportKind kind, ReportTable table)
    {
        if (kind == CafeteriaReportKind.PurchaseItemDetail)
            return table.Title.Contains("Itemized", StringComparison.OrdinalIgnoreCase);
        if (kind == CafeteriaReportKind.RefundVoidAudit)
            return table.Rows.Any(row => row.ContainsKey("order"));
        return kind == CafeteriaReportKind.EmployeeStatement;
    }

    private static IReadOnlyDictionary<string, object?> AddIDMethod(
        IReadOnlyDictionary<string, object?> row,
        ReportFilter filter,
        IReadOnlyList<OrderRow> orders,
        IReadOnlyDictionary<int, CustomerRow> customers,
        IReadOnlyDictionary<Guid, OrderRow> byId)
    {
        OrderRow? order = null;
        if (row.TryGetValue("order", out object? orderValue) && TryGuid(orderValue, out Guid orderId))
        {
            byId.TryGetValue(orderId, out order);
        }

        if (order is null && row.TryGetValue("date", out object? dateValue) && TryDate(dateValue, out DateTimeOffset occurredAt))
        {
            IEnumerable<OrderRow> candidates = orders.Where(x =>
                x.CompletedAt == occurredAt || x.VoidedAt == occurredAt || x.Refunds.Any(r => r.RefundedAt == occurredAt));

            if (filter.EmployeeId.HasValue)
                candidates = candidates.Where(x => x.CustomerId == filter.EmployeeId.Value);

            string? customerName = ReadString(row, "customer") ?? ReadString(row, "employee");
            if (!string.IsNullOrWhiteSpace(customerName))
            {
                candidates = candidates.Where(x =>
                    customers.TryGetValue(x.CustomerId, out CustomerRow? customer) &&
                    string.Equals(customer.Name, customerName, StringComparison.OrdinalIgnoreCase));
            }

            string? itemName = ReadString(row, "item");
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                candidates = candidates.Where(x => x.Items.Any(item =>
                    string.Equals(item.ProductName, itemName, StringComparison.OrdinalIgnoreCase)));
            }

            order = candidates.OrderByDescending(x => x.CompletedAt ?? x.VoidedAt).FirstOrDefault();
        }

        Dictionary<string, object?> result = new(row, StringComparer.OrdinalIgnoreCase)
        {
            ["idMethod"] = FormatIDMethod(order?.IDMethod)
        };
        return result;
    }

    private static string FormatIDMethod(int? value)
    {
        if (!value.HasValue || !Enum.IsDefined(typeof(IDMethod), value.Value)) return string.Empty;
        return ((IDMethod)value.Value).ToString();
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out object? value) ? Convert.ToString(value)?.Trim() : null;

    private static bool TryGuid(object? value, out Guid result)
    {
        if (value is Guid guid)
        {
            result = guid;
            return true;
        }
        return Guid.TryParse(Convert.ToString(value), out result);
    }

    private static bool TryDate(object? value, out DateTimeOffset result)
    {
        if (value is DateTimeOffset dto)
        {
            result = dto;
            return true;
        }
        if (value is DateTime dt)
        {
            result = new DateTimeOffset(dt);
            return true;
        }
        return DateTimeOffset.TryParse(Convert.ToString(value), out result);
    }
}
