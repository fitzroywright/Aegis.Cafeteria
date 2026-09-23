namespace Aegis.Cafeteria.Reporting;

using Aegis.Cafeteria.Reporting.Models;

public static class CafeteriaReportCatalog
{
    public static IReadOnlyList<ReportCatalogItem> All { get; } =
    [
        new(CafeteriaReportKind.ExecutiveSummary, "Executive summary", "Sales, tender, refunds, employee exposure and top products at a glance.", false, false),
        new(CafeteriaReportKind.DailySales, "Daily sales", "Daily transaction totals with tender and refund detail.", false, false),
        new(CafeteriaReportKind.SalesTrend, "Sales trend", "Sales and transaction-count trend across the selected period.", false, false),
        new(CafeteriaReportKind.TenderMix, "Tender mix", "Cash versus cafeteria-account tender composition.", false, false),
        new(CafeteriaReportKind.ProductSales, "Product sales", "Quantity and value sold by product.", false, true),
        new(CafeteriaReportKind.PurchaseItemDetail, "Itemized purchases by customer", "Itemized purchases with customer, department, item, price, quantity, total, and separate cash/card-account amounts. Includes Day/Week/Month grouped totals.", false, false),
        new(CafeteriaReportKind.PurchaseByPerson, "Purchases summarized by person", "Customer and department spending summary with separate cash/card-account totals, grouped by Day/Week/Month.", false, false),
        new(CafeteriaReportKind.EmployeeStatement, "Employee statement", "Detailed cafeteria activity and account position for one employee.", true, false),
        new(CafeteriaReportKind.CanteenSummary, "Canteen summary", "Department-grouped employee usage, work days, subsidy rate, company amount and employee amount due for the selected period.", false, false),
        new(CafeteriaReportKind.MonthlyAllocations, "Monthly allocations", "Stored monthly credit limits, employee-funded and company subsidy allocations.", true, false),
        new(CafeteriaReportKind.SubsidyUtilization, "Subsidy utilization", "Subsidy allocated, used and remaining by employee.", true, false),
        new(CafeteriaReportKind.CustomerResponsibility, "Customer responsibility", "Employee cafeteria liability after customer payments.", true, false),
        new(CafeteriaReportKind.RefundVoidAudit, "Refund and void audit", "Refunds and voided orders with operator/reason detail.", true, false),
        new(CafeteriaReportKind.AdditionalCreditAudit, "Additional credit audit", "Manager-approved additional credit events.", true, false),
        new(CafeteriaReportKind.OutstandingDebt, "Outstanding debt", "Outstanding customer liabilities across employees.", true, false),
        new(CafeteriaReportKind.ExternalSystemReconciliation, "External system reconciliation", "Cafeteria calculated balances compared with the configured external accounting system.", true, false)
    ];
}
