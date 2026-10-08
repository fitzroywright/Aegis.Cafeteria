namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

using System.Globalization;
using System.Text;
using System.Text.Json;

public sealed record PayrollDeductionDetailRow
(
      int EmployeeId
    , string EmployeeCode
    , string EmployeeName
    , string Department
    , decimal MonthlyCreditLimit
    , decimal SubsidyAllocation
    , decimal CafeteriaPurchases
    , decimal CafeteriaRefunds
    , decimal StipendCardReduction
    , decimal CompanyResponsibility
    , decimal EmployeeResponsibility
    , decimal CustomerPayments
    , decimal EmployeeAmountDue
);

public sealed record PayrollDeductionSummaryRow
(
      string Department
    , int EmployeesWithAmountDue
    , decimal EmployeeAmountDue
);

public sealed record PayrollDeductionReportPackage
(
      int Year
    , int Month
    , IReadOnlyList<PayrollDeductionDetailRow> Details
    , IReadOnlyList<PayrollDeductionSummaryRow> Summary
    , decimal TotalEmployeeAmountDue
)
{
    public int EmployeesWithAmountDue => Details.Count;
}

public sealed class PayrollDeductionReportSource
{
    private readonly IPayrollLedgerSource ledgerSource;
    private readonly IWorkforceReferenceSource workforce;
    private readonly IDbContextFactory<ServiceDbContext> factory;

    public PayrollDeductionReportSource
    (
          IPayrollLedgerSource ledgerSource
        , IWorkforceReferenceSource workforce
        , IDbContextFactory<ServiceDbContext> factory
    )
    {
        this.ledgerSource = ledgerSource;
        this.workforce = workforce;
        this.factory = factory;
    }

    public async Task<PayrollDeductionReportPackage> BuildAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        IReadOnlyDictionary<int, MonthlyAllocationServiceEvent> allocations = await LoadAllocationsAsync
        (
              year
            , month
            , cancellationToken
        );

        IReadOnlyDictionary<int, PayrollLedgerTotals> ledger = await this.ledgerSource.GetMonthlyTotalsAsync
        (
              year
            , month
            , cancellationToken
        );

        IReadOnlyList<ExternalCustomerSnapshot> employees = await this.workforce.GetCustomersAsync(cancellationToken);
        Dictionary<int, ExternalCustomerSnapshot> employeeMap = employees.ToDictionary(employee => employee.CustomerId);
        List<PayrollDeductionDetailRow> details = [];

        foreach (KeyValuePair<int, MonthlyAllocationServiceEvent> entry in allocations.OrderBy(item => item.Key))
        {
            MonthlyAllocationServiceEvent allocation = entry.Value;

            if (!allocation.IsEligibleForCredit)
            {
                continue;
            }

            PayrollLedgerTotals totals = ledger.GetValueOrDefault(entry.Key) ?? PayrollLedgerTotals.Empty;
            decimal monthlyCreditLimit = allocation.EligibleWorkingDays * allocation.CreditLimitPrice;
            decimal subsidyAllocation = Math.Min
            (
                  monthlyCreditLimit
                , allocation.EligibleWorkingDays * allocation.SubsidyCost
            );
            decimal totalCharges = Math.Max(0m, totals.Purchases - totals.Refunds);
            decimal remainingCompanyContribution = Math.Max(0m, subsidyAllocation - totals.StipendCardReduction);
            decimal companyResponsibility = Math.Min(totalCharges, remainingCompanyContribution);
            decimal employeeResponsibility = Math.Max(0m, totalCharges - companyResponsibility);
            decimal employeeAmountDue = Math.Max(0m, employeeResponsibility - totals.CustomerPayments);

            if (employeeAmountDue <= 0m)
            {
                continue;
            }

            ExternalCustomerSnapshot? employee = employeeMap.GetValueOrDefault(entry.Key);

            details.Add
            (
                new PayrollDeductionDetailRow
                (
                      entry.Key
                    , employee?.Code ?? entry.Key.ToString(CultureInfo.InvariantCulture)
                    , employee?.Name ?? allocation.EmployeeName
                    , employee?.Department ?? string.Empty
                    , monthlyCreditLimit
                    , subsidyAllocation
                    , totals.Purchases
                    , totals.Refunds
                    , totals.StipendCardReduction
                    , companyResponsibility
                    , employeeResponsibility
                    , totals.CustomerPayments
                    , employeeAmountDue
                )
            );
        }

        PayrollDeductionDetailRow[] orderedDetails = details
            .OrderBy(detail => detail.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(detail => detail.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(detail => detail.EmployeeId)
            .ToArray();

        List<PayrollDeductionSummaryRow> summary = orderedDetails
            .GroupBy
            (
                  detail => string.IsNullOrWhiteSpace(detail.Department) ? "Unassigned" : detail.Department.Trim()
                , StringComparer.OrdinalIgnoreCase
            )
            .Select
            (
                group => new PayrollDeductionSummaryRow
                (
                      group.Key
                    , group.Count()
                    , group.Sum(detail => detail.EmployeeAmountDue)
                )
            )
            .OrderBy(row => row.Department, StringComparer.OrdinalIgnoreCase)
            .ToList();

        decimal totalEmployeeAmountDue = orderedDetails.Sum(detail => detail.EmployeeAmountDue);

        summary.Add
        (
            new PayrollDeductionSummaryRow
            (
                  "ALL DEPARTMENTS"
                , orderedDetails.Length
                , totalEmployeeAmountDue
            )
        );

        return new PayrollDeductionReportPackage
        (
              year
            , month
            , orderedDetails
            , summary
            , totalEmployeeAmountDue
        );
    }

    public static byte[] BuildDetailCsv(PayrollDeductionReportPackage report)
    {
        ArgumentNullException.ThrowIfNull(report);
        StringBuilder text = new();
        text.AppendLine("Employee ID,Employee Code,Employee Name,Department,Monthly Credit Limit,Subsidy Allocation,Cafeteria Purchases,Cafeteria Refunds,Stipend Deduction,Company Responsibility,Employee Responsibility,Customer Payments,Employee Amount Due");

        foreach (PayrollDeductionDetailRow row in report.Details)
        {
            text.AppendLine
            (
                string.Join
                (
                      ","
                    , Csv(row.EmployeeId.ToString(CultureInfo.InvariantCulture))
                    , Csv(row.EmployeeCode)
                    , Csv(row.EmployeeName)
                    , Csv(row.Department)
                    , Money(row.MonthlyCreditLimit)
                    , Money(row.SubsidyAllocation)
                    , Money(row.CafeteriaPurchases)
                    , Money(row.CafeteriaRefunds)
                    , Money(row.StipendCardReduction)
                    , Money(row.CompanyResponsibility)
                    , Money(row.EmployeeResponsibility)
                    , Money(row.CustomerPayments)
                    , Money(row.EmployeeAmountDue)
                )
            );
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static byte[] BuildSummaryCsv(PayrollDeductionReportPackage report)
    {
        ArgumentNullException.ThrowIfNull(report);
        StringBuilder text = new();
        text.AppendLine("Department,Employees Requiring Deduction,Employee Amount Due");

        foreach (PayrollDeductionSummaryRow row in report.Summary)
        {
            text.AppendLine
            (
                string.Join
                (
                      ","
                    , Csv(row.Department)
                    , row.EmployeesWithAmountDue.ToString(CultureInfo.InvariantCulture)
                    , Money(row.EmployeeAmountDue)
                )
            );
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private async Task<IReadOnlyDictionary<int, MonthlyAllocationServiceEvent>> LoadAllocationsAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        List<ServiceEventRow> rows = await database.Events
            .AsNoTracking()
            .Where
            (
                serviceEvent =>
                       serviceEvent.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocation
                    || serviceEvent.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocationRecalculation
            )
            .OrderBy(serviceEvent => serviceEvent.Sequence)
            .ToListAsync(cancellationToken);

        Dictionary<int, MonthlyAllocationServiceEvent> allocations = [];

        foreach (ServiceEventRow row in rows)
        {
            MonthlyAllocationServiceEvent? allocation = null;

            if (row.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocation)
            {
                allocation = JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(row.PayloadJson);
            }
            else if (row.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocationRecalculation)
            {
                MonthlyAllocationRecalculationServiceEvent? recalculation = JsonSerializer.Deserialize<MonthlyAllocationRecalculationServiceEvent>(row.PayloadJson);
                allocation = recalculation?.ReplacementAllocation;
            }

            if (allocation is not null && allocation.Year == year && allocation.Month == month)
            {
                allocations[allocation.CustomerId] = allocation;
            }
        }

        return allocations;
    }

    private static string Csv(string value)
    {
        string text = value ?? string.Empty;

        if (text.Contains('"'))
        {
            text = text.Replace("\"", "\"\"");
        }

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{text}\""
            : text;
    }

    private static string Money(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

}
