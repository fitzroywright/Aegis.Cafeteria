namespace Aegis.Cafeteria.Reporting;

using System.Text.Json;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Aegis.Cafeteria.Reporting.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class CafeteriaReportService(
    IDbContextFactory<LocalCafeteriaDbContext> localFactory,
    ICustomerAccountQuery accountQuery,
    IServiceProvider services) : ICafeteriaReportService
{
    public IReadOnlyList<ReportCatalogItem> Catalog { get; } = CafeteriaReportCatalog.All;

    public async Task<ReportDocument> GenerateAsync(CafeteriaReportKind kind, ReportFilter filter, CancellationToken cancellationToken = default)
    {
        Validate(filter);
        return kind switch
        {
            CafeteriaReportKind.ExecutiveSummary => await ExecutiveSummaryAsync(filter, cancellationToken),
            CafeteriaReportKind.DailySales => await DailySalesAsync(filter, cancellationToken),
            CafeteriaReportKind.SalesTrend => await SalesTrendAsync(filter, cancellationToken),
            CafeteriaReportKind.TenderMix => await TenderMixAsync(filter, cancellationToken),
            CafeteriaReportKind.ProductSales => await ProductSalesAsync(filter, cancellationToken),
            CafeteriaReportKind.PurchaseItemDetail => await PurchaseItemDetailAsync(filter, cancellationToken),
            CafeteriaReportKind.PurchaseByPerson => await PurchaseByPersonAsync(filter, cancellationToken),
            CafeteriaReportKind.EmployeeStatement => await EmployeeStatementAsync(filter, cancellationToken),
            CafeteriaReportKind.CanteenSummary => await CanteenSummaryAsync(filter, cancellationToken),
            CafeteriaReportKind.MonthlyAllocations => await MonthlyAllocationsAsync(filter, cancellationToken),
            CafeteriaReportKind.SubsidyUtilization => await SubsidyUtilizationAsync(filter, cancellationToken),
            CafeteriaReportKind.CustomerResponsibility => await ResponsibilityAsync(filter, cancellationToken, false),
            CafeteriaReportKind.OutstandingDebt => await ResponsibilityAsync(filter, cancellationToken, true),
            CafeteriaReportKind.RefundVoidAudit => await RefundVoidAuditAsync(filter, cancellationToken),
            CafeteriaReportKind.AdditionalCreditAudit => await AdditionalCreditAuditAsync(filter, cancellationToken),
            CafeteriaReportKind.ExternalSystemReconciliation => await FfpReconciliationAsync(filter, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private async Task<ReportDocument> ExecutiveSummaryAsync(ReportFilter f, CancellationToken ct)
    {
        var sales = await LoadSalesAsync(f, ct);
        var responsibility = await LoadAccountPositionsAsync(f, ct);
        var gross = sales.Orders.Sum(x => x.Total);
        var cash = sales.Orders.Sum(x => x.Cash);
        var account = sales.Orders.Sum(x => x.Account);
        var refunds = sales.Orders.Sum(x => x.Refunds);
        var net = gross - refunds;
        var employeeLiability = responsibility.Sum(x => x.View.FinancialStatus.CustomerResponsibility);
        var subsidyUsed = responsibility.Sum(x => x.View.FinancialStatus.SubsidyUsed);
        var daily = DailyBuckets(sales.Orders, f);
        var products = ProductBuckets(sales.Orders).Take(10).ToArray();

        return Doc(CafeteriaReportKind.ExecutiveSummary, "Cafeteria executive summary", f,
        [
            new("Net sales", net), new("Gross sales", gross), new("Transactions", sales.Orders.Count, "integer"),
            new("Cash tender", cash), new("Account tender", account), new("Refunds", refunds),
            new("Subsidy used", subsidyUsed), new("Customer responsibility", employeeLiability)
        ],
        [
            new("Daily net sales", ReportChartType.Line, daily.Select(x=>x.Date.ToString("MMM d")).ToArray(), [new("Net sales", daily.Select(x=>x.Net).ToArray(), "currency")]),
            new("Tender mix", ReportChartType.Pie, ["Cafeteria account", "Cash"], [new("Tender", [account, cash], "currency")]),
            new("Top products", ReportChartType.Bar, products.Select(x=>x.Name).ToArray(), [new("Sales", products.Select(x=>x.Value).ToArray(), "currency")])
        ],
        [BuildDailyTable(daily), BuildProductTable(products)]);
    }

    private async Task<ReportDocument> DailySalesAsync(ReportFilter f, CancellationToken ct)
    {
        var sales=await LoadSalesAsync(f,ct); var daily=DailyBuckets(sales.Orders,f);
        return Doc(CafeteriaReportKind.DailySales,"Daily cafeteria sales",f,
            [new("Net sales",daily.Sum(x=>x.Net)),new("Transactions",daily.Sum(x=>x.Count),"integer"),new("Refunds",daily.Sum(x=>x.Refunds))],
            [new("Daily sales",ReportChartType.Bar,daily.Select(x=>x.Date.ToString("MMM d")).ToArray(),[new("Net sales",daily.Select(x=>x.Net).ToArray(),"currency")])],
            [BuildDailyTable(daily)]);
    }

    private async Task<ReportDocument> SalesTrendAsync(ReportFilter f,CancellationToken ct)
    {
        var sales=await LoadSalesAsync(f,ct);var daily=DailyBuckets(sales.Orders,f);
        return Doc(CafeteriaReportKind.SalesTrend,"Sales trend",f,[],[
            new("Net sales trend",ReportChartType.Line,daily.Select(x=>x.Date.ToString("MMM d")).ToArray(),[new("Net sales",daily.Select(x=>x.Net).ToArray(),"currency")]),
            new("Transaction count",ReportChartType.Line,daily.Select(x=>x.Date.ToString("MMM d")).ToArray(),[new("Transactions",daily.Select(x=>(decimal)x.Count).ToArray(),"integer")])
        ],[BuildDailyTable(daily)]);
    }

    private async Task<ReportDocument> TenderMixAsync(ReportFilter f,CancellationToken ct)
    {
        var sales=await LoadSalesAsync(f,ct);var account=sales.Orders.Sum(x=>x.Account);var cash=sales.Orders.Sum(x=>x.Cash);
        return Doc(CafeteriaReportKind.TenderMix,"Tender mix",f,[new("Cafeteria account",account),new("Cash",cash)],
            [new("Tender composition",ReportChartType.Pie,["Cafeteria account","Cash"],[new("Tender",[account,cash],"currency")])],[]);
    }

    private async Task<ReportDocument> ProductSalesAsync(ReportFilter f,CancellationToken ct)
    {
        var sales=await LoadSalesAsync(f,ct);var products=ProductBuckets(sales.Orders).Where(x=>f.ProductId is null||x.ProductId==f.ProductId).ToArray();
        return Doc(CafeteriaReportKind.ProductSales,"Product sales",f,[new("Product sales",products.Sum(x=>x.Value)),new("Units sold",products.Sum(x=>x.Quantity),"integer")],
            [new("Sales by product",ReportChartType.Bar,products.Take(15).Select(x=>x.Name).ToArray(),[new("Sales",products.Take(15).Select(x=>x.Value).ToArray(),"currency")])],
            [BuildProductTable(products)]);
    }

    private async Task<ReportDocument> PurchaseItemDetailAsync(ReportFilter f, CancellationToken ct)
    {
        var purchases = await LoadPurchaseReportDataAsync(f, ct);
        var detailRows = purchases.Lines
            .OrderBy(x => x.At)
            .ThenBy(x => x.Department)
            .ThenBy(x => x.Customer)
            .ThenBy(x => x.ItemName)
            .Select(x => Row(
                ("period", PeriodLabel(x.At, f.Grouping)),
                ("date", x.At),
                ("customer", x.Customer),
                ("department", x.Department),
                ("item", x.ItemName),
                ("price", x.UnitPrice),
                ("quantity", x.Quantity),
                ("total", x.Total),
                ("cash", x.CashAmount),
                ("account", x.AccountAmount),
                ("tender", TenderLabel(x.CashAmount, x.AccountAmount))))
            .ToArray();

        var totals = BuildPersonPeriodTotals(purchases.Lines, f.Grouping);
        var cash = purchases.Lines.Sum(x => x.CashAmount);
        var account = purchases.Lines.Sum(x => x.AccountAmount);
        var total = purchases.Lines.Sum(x => x.Total);

        return Doc(
            CafeteriaReportKind.PurchaseItemDetail,
            "Itemized purchases by customer",
            f,
            [new("Total spend", total), new("Cash purchases", cash), new("Card / account purchases", account), new("Items purchased", purchases.Lines.Sum(x => x.Quantity), "integer")],
            [],
            [
                new("Itemized purchases",
                    [
                        new("period", $"{f.Grouping} period"),
                        new("date", "Purchase date/time"),
                        new("customer", "Customer"),
                        new("department", "Department"),
                        new("item", "Item"),
                        new("price", "Unit price", "currency"),
                        new("quantity", "Quantity", "integer"),
                        new("total", "Total", "currency"),
                        new("cash", "Cash", "currency"),
                        new("account", "Card / Account", "currency"),
                        new("tender", "Tender")
                    ], detailRows),
                BuildPersonPeriodTotalsTable(totals, f.Grouping),
                BuildDepartmentPeriodTotalsTable(totals, f.Grouping),
                BuildPeriodTotalsTable(totals, f.Grouping)
            ],
            purchases.Warnings);
    }

    private async Task<ReportDocument> PurchaseByPersonAsync(ReportFilter f, CancellationToken ct)
    {
        var purchases = await LoadPurchaseReportDataAsync(f, ct);
        var totals = BuildPersonPeriodTotals(purchases.Lines, f.Grouping);
        var cash = totals.Sum(x => x.CashAmount);
        var account = totals.Sum(x => x.AccountAmount);
        var total = totals.Sum(x => x.Total);

        return Doc(
            CafeteriaReportKind.PurchaseByPerson,
            "Purchases summarized by person",
            f,
            [new("Total spend", total), new("Cash purchases", cash), new("Card / account purchases", account), new("Customers", totals.Select(x => x.CustomerId).Distinct().Count(), "integer")],
            [],
            [
                BuildPersonPeriodTotalsTable(totals, f.Grouping),
                BuildDepartmentPeriodTotalsTable(totals, f.Grouping),
                BuildPeriodTotalsTable(totals, f.Grouping)
            ],
            purchases.Warnings);
    }

    private async Task<PurchaseReportData> LoadPurchaseReportDataAsync(ReportFilter f, CancellationToken ct)
    {
        await using var db = await localFactory.CreateDbContextAsync(ct);
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var orders = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .ToListAsync(ct);

        var lines = new List<PurchaseLine>();
        var warnings = new List<string>();
        foreach (var order in orders
            .Where(x => x.CompletedAt.HasValue && InRange(x.CompletedAt.Value, f) && x.Status == (int)OrderStatus.Completed)
            .OrderBy(x => x.CompletedAt))
        {
            if (!customers.TryGetValue(order.CustomerId, out var customer))
            {
                warnings.Add($"Order {order.Id} references missing customer {order.CustomerId}; a placeholder name was used.");
            }

            var customerName = customer?.Name ?? $"#{order.CustomerId}";
            var department = string.IsNullOrWhiteSpace(customer?.Department) ? "Unassigned" : customer.Department;
            var orderTotal = order.Items.Sum(x => x.UnitPrice * x.Quantity);
            var cashTotal = order.Payments.Where(x => x.Method == (int)PaymentMethod.Cash).Sum(x => x.Amount);
            var accountTotal = order.Payments.Where(x => x.Method == (int)PaymentMethod.CafeteriaAccount).Sum(x => x.Amount);
            var tenderTotal = cashTotal + accountTotal;
            if (Math.Abs(tenderTotal - orderTotal) > 0.01m)
                warnings.Add($"Order {order.Id} tender ({tenderTotal:N2}) does not equal item total ({orderTotal:N2}); item tender allocation is proportional to recorded tender.");

            var itemValues = order.Items.Select(x => x.UnitPrice * x.Quantity).ToArray();
            var cashAllocations = AllocateByValue(itemValues, cashTotal, orderTotal);
            var accountAllocations = AllocateByValue(itemValues, accountTotal, orderTotal);
            for (var i = 0; i < order.Items.Count; i++)
            {
                var item = order.Items[i];
                lines.Add(new PurchaseLine(
                    order.Id,
                    order.CustomerId,
                    customerName,
                    department,
                    order.CompletedAt!.Value,
                    item.ProductId,
                    item.ProductName,
                    item.UnitPrice,
                    item.Quantity,
                    item.UnitPrice * item.Quantity,
                    cashAllocations[i],
                    accountAllocations[i]));
            }
        }

        return new PurchaseReportData(lines, warnings.Distinct().ToArray());
    }

    private static decimal[] AllocateByValue(IReadOnlyList<decimal> itemValues, decimal tenderAmount, decimal orderTotal)
    {
        var result = new decimal[itemValues.Count];
        if (itemValues.Count == 0 || tenderAmount == 0m || orderTotal == 0m) return result;
        decimal allocated = 0m;
        for (var i = 0; i < itemValues.Count; i++)
        {
            var amount = i == itemValues.Count - 1
                ? tenderAmount - allocated
                : Math.Round(tenderAmount * itemValues[i] / orderTotal, 2, MidpointRounding.AwayFromZero);
            result[i] = amount;
            allocated += amount;
        }
        return result;
    }

    private static PersonPeriodTotal[] BuildPersonPeriodTotals(IEnumerable<PurchaseLine> lines, PurchaseGrouping grouping) =>
        lines.GroupBy(x => new { Period = PeriodKey(x.At, grouping), x.CustomerId, x.Customer, x.Department })
            .Select(g => new PersonPeriodTotal(
                PeriodLabel(g.First().At, grouping),
                g.Key.Period,
                g.Key.CustomerId,
                g.Key.Customer,
                g.Key.Department,
                g.Sum(x => x.CashAmount),
                g.Sum(x => x.AccountAmount),
                g.Sum(x => x.Total)))
            .OrderBy(x => x.PeriodKey)
            .ThenBy(x => x.Department)
            .ThenBy(x => x.Customer)
            .ToArray();

    private static ReportTable BuildPersonPeriodTotalsTable(IEnumerable<PersonPeriodTotal> totals, PurchaseGrouping grouping) =>
        new($"Totals by person, department and {grouping.ToString().ToLowerInvariant()}",
            [
                new("period", $"{grouping} period"),
                new("department", "Department"),
                new("customer", "Customer"),
                new("cash", "Cash", "currency"),
                new("account", "Card / Account", "currency"),
                new("total", "Total spend", "currency")
            ],
            totals.Select(x => Row(("period", x.Period), ("department", x.Department), ("customer", x.Customer), ("cash", x.CashAmount), ("account", x.AccountAmount), ("total", x.Total))).ToArray());

    private static ReportTable BuildDepartmentPeriodTotalsTable(IEnumerable<PersonPeriodTotal> totals, PurchaseGrouping grouping)
    {
        var rows = totals
            .GroupBy(x => new { x.PeriodKey, x.Period, x.Department })
            .OrderBy(g => g.Key.PeriodKey)
            .ThenBy(g => g.Key.Department)
            .Select(g => Row(
                ("period", g.Key.Period),
                ("department", g.Key.Department),
                ("cash", g.Sum(x => x.CashAmount)),
                ("account", g.Sum(x => x.AccountAmount)),
                ("total", g.Sum(x => x.Total))))
            .ToArray();
        return new($"Totals by department and {grouping.ToString().ToLowerInvariant()}",
            [
                new("period", $"{grouping} period"),
                new("department", "Department"),
                new("cash", "Cash", "currency"),
                new("account", "Card / Account", "currency"),
                new("total", "Total spend", "currency")
            ], rows);
    }

    private static ReportTable BuildPeriodTotalsTable(IEnumerable<PersonPeriodTotal> totals, PurchaseGrouping grouping)
    {
        var rows = totals
            .GroupBy(x => new { x.PeriodKey, x.Period })
            .OrderBy(g => g.Key.PeriodKey)
            .Select(g => Row(
                ("period", g.Key.Period),
                ("cash", g.Sum(x => x.CashAmount)),
                ("account", g.Sum(x => x.AccountAmount)),
                ("total", g.Sum(x => x.Total))))
            .ToArray();
        return new($"Totals by {grouping.ToString().ToLowerInvariant()}",
            [
                new("period", $"{grouping} period"),
                new("cash", "Cash", "currency"),
                new("account", "Card / Account", "currency"),
                new("total", "Total spend", "currency")
            ], rows);
    }

    private static DateOnly PeriodKey(DateTimeOffset at, PurchaseGrouping grouping)
    {
        var d = DateOnly.FromDateTime(at.LocalDateTime);
        return grouping switch
        {
            PurchaseGrouping.Day => d,
            PurchaseGrouping.Week => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
            PurchaseGrouping.Month => new DateOnly(d.Year, d.Month, 1),
            _ => d
        };
    }

    private static string PeriodLabel(DateTimeOffset at, PurchaseGrouping grouping)
    {
        var start = PeriodKey(at, grouping);
        return grouping switch
        {
            PurchaseGrouping.Day => start.ToString("dd MMM yyyy"),
            PurchaseGrouping.Week => $"{start:dd MMM yyyy} - {start.AddDays(6):dd MMM yyyy}",
            PurchaseGrouping.Month => start.ToString("MMM yyyy"),
            _ => start.ToString("dd MMM yyyy")
        };
    }

    private static string TenderLabel(decimal cash, decimal account) =>
        cash > 0m && account > 0m ? "Split" : cash > 0m ? "Cash" : account > 0m ? "Card / Account" : "Unpaid";

    private async Task<ReportDocument> EmployeeStatementAsync(ReportFilter f,CancellationToken ct)
    {
        if(f.EmployeeId is null)throw new ArgumentException("Employee statement requires an EmployeeId.",nameof(f));
        if(f.From.Year!=f.To.Year||f.From.Month!=f.To.Month)throw new ArgumentException("Employee statement must be limited to one account month.",nameof(f));
        var view=await accountQuery.GetAsync(f.EmployeeId.Value,f.From.Year,f.From.Month,ct) ?? throw new KeyNotFoundException($"Employee {f.EmployeeId.Value} has no cafeteria account for {f.From:yyyy-MM}.");
        var txRows=view.Transactions.Select(x=>Row(("date",x.OccurredAt),("kind",x.Kind),("description",x.Description),("amount",x.Amount),("account",x.CafeteriaAccountAmount),("cash",x.CashAmount))).ToArray();
        var table=new ReportTable("Transactions",[new("date","Date"),new("kind","Type"),new("description","Description"),new("amount","Amount","currency"),new("account","Account","currency"),new("cash","Cash","currency")],txRows);
        var fs=view.FinancialStatus;
        return Doc(CafeteriaReportKind.EmployeeStatement,$"Employee statement — {view.Customer.Name}",f,
            [new("Credit limit",fs.CreditLimit),new("Available",fs.TotalAvailableToSpend),new("Subsidy remaining",fs.SubsidyAmountRemaining),new("Customer responsibility",fs.CustomerResponsibility),new("Employee amount due",fs.EmployeeAmountDue),new("Refunds",fs.TotalRefunds)],[],[table]);
    }

    private async Task<ReportDocument> MonthlyAllocationsAsync(ReportFilter f,CancellationToken ct)
    {
        await using var db=await localFactory.CreateDbContextAsync(ct);var allocations=await db.MonthlyCreditAllocations.AsNoTracking().ToListAsync(ct);var customers=await db.Customers.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var filtered=allocations.Where(x=>PeriodIntersects(x.Year,x.Month,f)&& (f.EmployeeId is null||x.CustomerId==f.EmployeeId)).OrderBy(x=>x.Year).ThenBy(x=>x.Month).ThenBy(x=>customers.GetValueOrDefault(x.CustomerId)?.Name).ToArray();
        var rows=filtered.Select(x=>Row(("period",$"{x.Year:D4}-{x.Month:D2}"),("employee",customers.GetValueOrDefault(x.CustomerId)?.Name??$"#{x.CustomerId}"),("days",x.EligibleWorkingDays),("employeeFunded",x.EmployeeFundedAllocation),("subsidy",x.SubsidyAllocation),("limit",x.TotalCreditLimit))).ToArray();
        return Doc(CafeteriaReportKind.MonthlyAllocations,"Monthly credit allocations",f,[new("Total credit allocated",filtered.Sum(x=>x.TotalCreditLimit)),new("Subsidy allocated",filtered.Sum(x=>x.SubsidyAllocation)),new("Employee-funded credit allocated",filtered.Sum(x=>x.EmployeeFundedAllocation))],
            [new("Allocation components",ReportChartType.Bar,["Employee-funded","Company subsidy"],[new("Allocation",[filtered.Sum(x=>x.EmployeeFundedAllocation),filtered.Sum(x=>x.SubsidyAllocation)],"currency")])],
            [new("Allocations",[new("period","Period"),new("employee","Employee"),new("days","Working days","integer"),new("employeeFunded","Employee-funded","currency"),new("subsidy","Subsidy","currency"),new("limit","Credit limit","currency")],rows)]);
    }

    private async Task<ReportDocument> SubsidyUtilizationAsync(ReportFilter f,CancellationToken ct)
    {
        var positions=await LoadAccountPositionsAsync(f,ct);var rows=positions.Select(x=>Row(("employee",x.View.Customer.Name),("period",$"{x.Year:D4}-{x.Month:D2}"),("allocated",x.View.FinancialStatus.SubsidyLimit),("used",x.View.FinancialStatus.SubsidyUsed),("remaining",x.View.FinancialStatus.SubsidyAmountRemaining))).ToArray();
        var allocated=positions.Sum(x=>x.View.FinancialStatus.SubsidyLimit);var used=positions.Sum(x=>x.View.FinancialStatus.SubsidyUsed);var remaining=positions.Sum(x=>x.View.FinancialStatus.SubsidyAmountRemaining);
        return Doc(CafeteriaReportKind.SubsidyUtilization,"Subsidy utilization",f,[new("Allocated",allocated),new("Used",used),new("Remaining",remaining)],
            [new("Subsidy utilization",ReportChartType.Pie,["Used","Remaining"],[new("Subsidy",[used,remaining],"currency")])],
            [new("Employee subsidy",[new("employee","Employee"),new("period","Period"),new("allocated","Allocated","currency"),new("used","Used","currency"),new("remaining","Remaining","currency")],rows)]);
    }

    private async Task<ReportDocument> ResponsibilityAsync(ReportFilter f,CancellationToken ct,bool debtOnly)
    {
        var positions=await LoadAccountPositionsAsync(f,ct);if(debtOnly)positions=positions.Where(x=>x.View.FinancialStatus.CustomerResponsibility>0).ToList();
        var rows=positions.OrderByDescending(x=>x.View.FinancialStatus.CustomerResponsibility).Select(x=>Row(("employee",x.View.Customer.Name),("period",$"{x.Year:D4}-{x.Month:D2}"),("responsibility",x.View.FinancialStatus.CustomerResponsibility),("amountDue",x.View.FinancialStatus.EmployeeAmountDue),("carried",x.View.FinancialStatus.CarriedForwardDebt),("payments",x.View.FinancialStatus.CustomerPayments))).ToArray();
        var title=debtOnly?"Outstanding customer debt":"Customer responsibility";var kind=debtOnly?CafeteriaReportKind.OutstandingDebt:CafeteriaReportKind.CustomerResponsibility;
        return Doc(kind,title,f,[new("Customer responsibility",positions.Sum(x=>x.View.FinancialStatus.CustomerResponsibility)),new("Employee amount due",positions.Sum(x=>x.View.FinancialStatus.EmployeeAmountDue)),new("Carried debt",positions.Sum(x=>x.View.FinancialStatus.CarriedForwardDebt))],
            [new("Largest employee balances",ReportChartType.Bar,positions.OrderByDescending(x=>x.View.FinancialStatus.CustomerResponsibility).Take(12).Select(x=>x.View.Customer.Name).ToArray(),[new("Responsibility",positions.OrderByDescending(x=>x.View.FinancialStatus.CustomerResponsibility).Take(12).Select(x=>x.View.FinancialStatus.CustomerResponsibility).ToArray(),"currency")])],
            [new(title,[new("employee","Employee"),new("period","Period"),new("responsibility","Responsibility","currency"),new("amountDue","Employee amount due","currency"),new("carried","Carried debt","currency"),new("payments","Payments","currency")],rows)]);
    }

    private async Task<ReportDocument> RefundVoidAuditAsync(ReportFilter f,CancellationToken ct)
    {
        await using var db=await localFactory.CreateDbContextAsync(ct);var orders=await db.Orders.AsNoTracking().Include(x=>x.Items).Include(x=>x.Refunds).ThenInclude(x=>x.Items).ToListAsync(ct);var customers=await db.Customers.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var refundRows=orders.Where(o=>f.EmployeeId is null||o.CustomerId==f.EmployeeId).SelectMany(o=>o.Refunds.Select(r=>(o,r))).Where(x=>InRange(x.r.RefundedAt,f)).Select(x=>Row(("date",x.r.RefundedAt),("employee",customers.GetValueOrDefault(x.o.CustomerId)?.Name??$"#{x.o.CustomerId}"),("type","Refund"),("amount",x.r.Items.Sum(i=>i.UnitPrice*i.Quantity)),("operator",x.r.RefundedBy),("reason",x.r.Reason),("order",x.o.Id))).ToList();
        var voidRows=orders.Where(o=>o.VoidedAt.HasValue&&InRange(o.VoidedAt.Value,f)&&(f.EmployeeId is null||o.CustomerId==f.EmployeeId)).Select(o=>Row(("date",o.VoidedAt!.Value),("employee",customers.GetValueOrDefault(o.CustomerId)?.Name??$"#{o.CustomerId}"),("type","Void"),("amount",o.Items.Sum(i=>i.UnitPrice*i.Quantity)),("operator",o.VoidedBy??""),("reason",o.VoidReason??""),("order",o.Id))).ToList();
        var rows=refundRows.Concat(voidRows).OrderBy(x=>(DateTimeOffset)x["date"]!).ToArray();
        return Doc(CafeteriaReportKind.RefundVoidAudit,"Refund and void audit",f,[new("Refund/void events",rows.Length,"integer"),new("Value affected",rows.Sum(x=>Convert.ToDecimal(x["amount"])))],[],[new("Audit detail",[new("date","Date"),new("employee","Employee"),new("type","Type"),new("amount","Amount","currency"),new("operator","Operator"),new("reason","Reason"),new("order","Order")],rows)]);
    }

    private async Task<ReportDocument> AdditionalCreditAuditAsync(ReportFilter f,CancellationToken ct)
    {
        await using var db=await localFactory.CreateDbContextAsync(ct);var events=await db.AccountEvents.AsNoTracking().Where(x=>x.EventType=="AdditionalCreditApproval").ToListAsync(ct);var customers=await db.Customers.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var selected=events.Where(x=>InRange(x.OccurredAt,f)&&(f.EmployeeId is null||x.CustomerId==f.EmployeeId)).OrderBy(x=>x.OccurredAt).ToArray();
        var rows=selected.Select(x=>{var reason=ExtractJson(x.PayloadJson,"Reason");var approved=ExtractJson(x.PayloadJson,"ApprovedBy");return Row(("date",x.OccurredAt),("employee",customers.GetValueOrDefault(x.CustomerId)?.Name??$"#{x.CustomerId}"),("amount",x.Amount),("approvedBy",approved),("reason",reason),("operation",x.Id));}).ToArray();
        return Doc(CafeteriaReportKind.AdditionalCreditAudit,"Additional credit audit",f,[new("Additional credit approved",selected.Sum(x=>x.Amount)),new("Approvals",selected.Length,"integer")],[],[new("Approvals",[new("date","Date"),new("employee","Employee"),new("amount","Amount","currency"),new("approvedBy","Approved by"),new("reason","Reason"),new("operation","Operation")],rows)]);
    }

    private async Task<ReportDocument> CanteenSummaryAsync(ReportFilter f, CancellationToken ct)
    {
        await using var db = await localFactory.CreateDbContextAsync(ct);
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var orders = await db.Orders.AsNoTracking()
            .Include(x => x.Payments)
            .Include(x => x.Refunds).ThenInclude(x => x.Allocations)
            .ToListAsync(ct);

        var policyProvider = services.GetRequiredService<ICafeteriaRatePolicyProvider>();
        var policy = await policyProvider.GetAsync(ct)
            ?? throw new InvalidOperationException("Cafeteria funding policy is unavailable. Refresh cafeteria reference data before generating the Canteen Summary.");

        var holidays = (await db.Holidays.AsNoTracking().ToListAsync(ct))
            .Select(x => x.Date)
            .ToHashSet();
        var workDays = CountWorkingDays(f.From, f.To, holidays);
        var subsidyRate = policy.SubsidyCost;
        var companyAmount = subsidyRate * workDays;

        var usage = new Dictionary<int, decimal>();
        foreach (var order in orders)
        {
            if (order.CompletedAt.HasValue && InRange(order.CompletedAt.Value, f) && order.Status == (int)OrderStatus.Completed)
            {
                var accountCharge = order.Payments
                    .Where(x => x.Method == (int)PaymentMethod.CafeteriaAccount)
                    .Sum(x => x.Amount);
                if (accountCharge != 0m)
                    usage[order.CustomerId] = usage.GetValueOrDefault(order.CustomerId) + accountCharge;
            }

            foreach (var refund in order.Refunds.Where(x => InRange(x.RefundedAt, f)))
            {
                var accountRefund = refund.Allocations
                    .Where(x => x.Method == (int)PaymentMethod.CafeteriaAccount)
                    .Sum(x => x.Amount);
                if (accountRefund != 0m)
                    usage[order.CustomerId] = usage.GetValueOrDefault(order.CustomerId) - accountRefund;
            }
        }

        var rows = usage
            .Where(x => x.Value != 0m && customers.ContainsKey(x.Key))
            .Select(x =>
            {
                var customer = customers[x.Key];
                var employeeCompanyAmount = customer.IsEligibleForCredit ? companyAmount : 0m;
                var deduction = Math.Max(0m, x.Value - employeeCompanyAmount);
                return Row(
                    ("department", string.IsNullOrWhiteSpace(customer.Department) ? "Unassigned" : customer.Department),
                    ("employee", customer.Name),
                    ("amountUsed", x.Value),
                    ("workDays", customer.IsEligibleForCredit ? workDays : 0),
                    ("companyPayment", customer.IsEligibleForCredit ? subsidyRate : 0m),
                    ("companyAmount", employeeCompanyAmount),
                    ("deduction", deduction));
            })
            .OrderBy(x => Convert.ToString(x["department"], System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(x => Convert.ToString(x["employee"], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        var totalUsed = rows.Sum(x => Convert.ToDecimal(x["amountUsed"], System.Globalization.CultureInfo.InvariantCulture));
        var totalCompany = rows.Sum(x => Convert.ToDecimal(x["companyAmount"], System.Globalization.CultureInfo.InvariantCulture));
        var totalDeduction = rows.Sum(x => Convert.ToDecimal(x["deduction"], System.Globalization.CultureInfo.InvariantCulture));

        return Doc(
            CafeteriaReportKind.CanteenSummary,
            "Canteen Summary",
            f,
            [new("Amount used", totalUsed), new("Company amount", totalCompany), new("Deduction", totalDeduction)],
            [],
            [new("Canteen summary detail",
                [
                    new("department", "Department"),
                    new("employee", "Employee Name"),
                    new("amountUsed", "Amt Used", "currency"),
                    new("workDays", "Work Days", "integer"),
                    new("companyPayment", "Company Pmt", "currency"),
                    new("companyAmount", "Company Amt", "currency"),
                    new("deduction", "Deduction", "currency")
                ], rows)]);
    }

    private static int CountWorkingDays(DateOnly from, DateOnly to, HashSet<DateOnly> holidays)
    {
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(d))
                count++;
        return count;
    }

    private async Task<ReportDocument> FfpReconciliationAsync(ReportFilter f,CancellationToken ct)
    {
        var gateway=services.GetService<IExternalAccountingGateway>();
        if(gateway is null)return Doc(CafeteriaReportKind.ExternalSystemReconciliation,"External system reconciliation",f,[],[],[],["External accounting integration is unavailable; live reconciliation cannot run."]);
        return Doc(CafeteriaReportKind.ExternalSystemReconciliation,"External system reconciliation",f,[],[],[],["External accounting integration is write-only; live balance reconciliation is not available through this boundary."]);
    }

    private async Task<SalesData> LoadSalesAsync(ReportFilter f,CancellationToken ct)
    {
        await using var db=await localFactory.CreateDbContextAsync(ct);
        // SQLite cannot reliably translate DateTimeOffset ordering/range comparisons. Materialize the bounded operational data first and filter/sort in memory.
        var orders=await db.Orders.AsNoTracking().Include(x=>x.Items).Include(x=>x.Payments).Include(x=>x.Refunds).ThenInclude(x=>x.Items).ToListAsync(ct);
        var selected=orders.Where(o=>o.CompletedAt.HasValue&&InRange(o.CompletedAt.Value,f)&&(f.EmployeeId is null||o.CustomerId==f.EmployeeId)&&(o.Status==(int)OrderStatus.Completed||f.IncludeVoided&&o.Status==(int)OrderStatus.Voided)).OrderBy(o=>o.CompletedAt).Select(o=>new SaleOrder(o.Id,o.CustomerId,o.CompletedAt!.Value,o.Items.Sum(i=>i.UnitPrice*i.Quantity),o.Payments.Where(p=>p.Method==(int)PaymentMethod.Cash).Sum(p=>p.Amount),o.Payments.Where(p=>p.Method==(int)PaymentMethod.CafeteriaAccount).Sum(p=>p.Amount),o.Refunds.Where(r=>InRange(r.RefundedAt,f)).Sum(r=>r.Items.Sum(i=>i.UnitPrice*i.Quantity)),o.Items.Select(i=>new SaleItem(i.ProductId,i.ProductName,i.Quantity,i.UnitPrice)).ToArray())).ToArray();
        return new(selected);
    }

    private async Task<List<AccountPosition>> LoadAccountPositionsAsync(ReportFilter f,CancellationToken ct)
    {
        await using var db=await localFactory.CreateDbContextAsync(ct);var customers=await db.Customers.AsNoTracking().Select(x=>x.Id).ToListAsync(ct);if(f.EmployeeId.HasValue)customers=customers.Where(x=>x==f.EmployeeId.Value).ToList();var result=new List<AccountPosition>();
        foreach(var (year,month) in Months(f))foreach(var id in customers){ct.ThrowIfCancellationRequested();var view=await accountQuery.GetAsync(id,year,month,ct);if(view is not null)result.Add(new(year,month,view));}
        return result;
    }

    private static DailyBucket[] DailyBuckets(IReadOnlyList<SaleOrder> orders,ReportFilter f)=>Enumerable.Range(0,f.To.DayNumber-f.From.DayNumber+1).Select(i=>f.From.AddDays(i)).Select(d=>{var day=orders.Where(o=>DateOnly.FromDateTime(o.At.LocalDateTime)==d).ToArray();return new DailyBucket(d,day.Length,day.Sum(x=>x.Total),day.Sum(x=>x.Account),day.Sum(x=>x.Cash),day.Sum(x=>x.Refunds));}).ToArray();
    private static ProductBucket[] ProductBuckets(IReadOnlyList<SaleOrder> orders)=>orders.SelectMany(x=>x.Items).GroupBy(x=>new{x.ProductId,x.Name}).Select(g=>new ProductBucket(g.Key.ProductId,g.Key.Name,g.Sum(x=>x.Quantity),g.Sum(x=>x.UnitPrice*x.Quantity))).OrderByDescending(x=>x.Value).ToArray();
    private static ReportTable BuildDailyTable(IEnumerable<DailyBucket> data)=>new("Daily detail",[new("date","Date"),new("count","Transactions","integer"),new("gross","Gross sales","currency"),new("account","Account","currency"),new("cash","Cash","currency"),new("refunds","Refunds","currency"),new("net","Net sales","currency")],data.Select(x=>Row(("date",x.Date),("count",x.Count),("gross",x.Gross),("account",x.Account),("cash",x.Cash),("refunds",x.Refunds),("net",x.Net))).ToArray());
    private static ReportTable BuildProductTable(IEnumerable<ProductBucket> data)=>new("Product detail",[new("product","Product"),new("quantity","Quantity","integer"),new("sales","Sales","currency")],data.Select(x=>Row(("product",x.Name),("quantity",x.Quantity),("sales",x.Value))).ToArray());
    private ReportDocument Doc(CafeteriaReportKind kind,string title,ReportFilter f,IReadOnlyList<ReportMetric> metrics,IReadOnlyList<ReportChart> charts,IReadOnlyList<ReportTable> tables,IReadOnlyList<string>? warnings=null)
        => new(kind,title,$"{f.From:dd MMM yyyy} — {f.To:dd MMM yyyy}",DateTimeOffset.Now,f,metrics,charts,SuppressZeroOnlyRows(tables),warnings,services.GetService<IOperatorSession>()?.Current?.DisplayName);

    private static IReadOnlyList<ReportTable> SuppressZeroOnlyRows(IReadOnlyList<ReportTable> tables)
        => tables.Select(table => table with { Rows = table.Rows.Where(ShouldShowReportRow).ToArray() }).ToArray();

    private static bool ShouldShowReportRow(IReadOnlyDictionary<string, object?> row)
    {
        var numericValues = row.Values.Where(IsNumeric).Select(ToDecimal).ToArray();
        return numericValues.Length == 0 || numericValues.Any(value => value != 0m);
    }

    private static bool IsNumeric(object? value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static decimal ToDecimal(object? value)
        => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    private static Dictionary<string,object?> Row(params (string Key,object? Value)[] values)=>values.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
    private static bool InRange(DateTimeOffset at,ReportFilter f){var d=DateOnly.FromDateTime(at.LocalDateTime);return d>=f.From&&d<=f.To;}
    private static bool PeriodIntersects(int year,int month,ReportFilter f){var first=new DateOnly(year,month,1);var last=first.AddMonths(1).AddDays(-1);return first<=f.To&&last>=f.From;}
    private static IEnumerable<(int Year,int Month)> Months(ReportFilter f){var cur=new DateOnly(f.From.Year,f.From.Month,1);var end=new DateOnly(f.To.Year,f.To.Month,1);while(cur<=end){yield return(cur.Year,cur.Month);cur=cur.AddMonths(1);}}
    private static void Validate(ReportFilter f){if(f.To<f.From)throw new ArgumentException("Report To date cannot precede From date.",nameof(f));if(f.To.DayNumber-f.From.DayNumber>366)throw new ArgumentException("Interactive reports are limited to 367 days per request.",nameof(f));}
    private static string ExtractJson(string json,string property){try{using var doc=JsonDocument.Parse(json);foreach(var p in doc.RootElement.EnumerateObject())if(string.Equals(p.Name,property,StringComparison.OrdinalIgnoreCase))return p.Value.ValueKind==JsonValueKind.String?p.Value.GetString()??"":p.Value.ToString();}catch(JsonException){}return "";}

    private sealed record SalesData(IReadOnlyList<SaleOrder> Orders);
    private sealed record SaleOrder(Guid Id,int CustomerId,DateTimeOffset At,decimal Total,decimal Cash,decimal Account,decimal Refunds,IReadOnlyList<SaleItem> Items);
    private sealed record SaleItem(int ProductId,string Name,int Quantity,decimal UnitPrice);
    private sealed record DailyBucket(DateOnly Date,int Count,decimal Gross,decimal Account,decimal Cash,decimal Refunds){public decimal Net=>Gross-Refunds;}
    private sealed record ProductBucket(int ProductId,string Name,int Quantity,decimal Value);
    private sealed record PurchaseReportData(IReadOnlyList<PurchaseLine> Lines, IReadOnlyList<string> Warnings);
    private sealed record PurchaseLine(Guid OrderId,int CustomerId,string Customer,string Department,DateTimeOffset At,int ProductId,string ItemName,decimal UnitPrice,int Quantity,decimal Total,decimal CashAmount,decimal AccountAmount);
    private sealed record PersonPeriodTotal(string Period,DateOnly PeriodKey,int CustomerId,string Customer,string Department,decimal CashAmount,decimal AccountAmount,decimal Total);
    private sealed record AccountPosition(int Year,int Month,CustomerAccountView View);
}
