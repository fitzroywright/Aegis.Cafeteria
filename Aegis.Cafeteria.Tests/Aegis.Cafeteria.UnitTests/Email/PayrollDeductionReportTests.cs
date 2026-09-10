namespace Aegis.Cafeteria.UnitTests.Email;

using Aegis.Cafeteria.Services.Email;

using System.Text;

public sealed class PayrollDeductionReportTests
{
    [Fact]
    public void DetailCsvContainsEmployeeAmountDueAndAuditComponents()
    {
        PayrollDeductionReportPackage report = new
        (
              2026
            , 9
            ,
            [
                new PayrollDeductionDetailRow
                (
                      123
                    , "E123"
                    , "Test Employee"
                    , "Finance"
                    , 10000m
                    , 6000m
                    , 8000m
                    , 500m
                    , 0m
                    , 6000m
                    , 1500m
                    , 250m
                    , 1250m
                )
            ]
            , [new PayrollDeductionSummaryRow("ALL DEPARTMENTS", 1, 1250m)]
            , 1250m
        );

        string csv = Encoding.UTF8.GetString(PayrollDeductionReportSource.BuildDetailCsv(report));

        Assert.Contains("Employee Amount Due", csv);
        Assert.Contains("Company Responsibility", csv);
        Assert.Contains("Customer Payments", csv);
        Assert.Contains("1250.00", csv);
    }

    [Fact]
    public void SummaryCsvContainsOverallAmount()
    {
        PayrollDeductionReportPackage report = new
        (
              2026
            , 9
            , []
            , [new PayrollDeductionSummaryRow("ALL DEPARTMENTS", 2, 1875m)]
            , 1875m
        );

        string csv = Encoding.UTF8.GetString(PayrollDeductionReportSource.BuildSummaryCsv(report));

        Assert.Contains("Employees Requiring Deduction", csv);
        Assert.Contains("ALL DEPARTMENTS,2,1875.00", csv);
    }
}
