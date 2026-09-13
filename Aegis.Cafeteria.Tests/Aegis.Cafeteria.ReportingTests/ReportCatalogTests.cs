using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Models;

namespace Aegis.Cafeteria.ReportingTests;

public sealed class ReportCatalogTests
{
    [Fact]
    public void Catalog_ContainsEveryReportKindExactlyOnce()
    {
        var kinds = Enum.GetValues<CafeteriaReportKind>();
        var catalogKinds = CafeteriaReportCatalog.All.Select(x => x.Kind).ToArray();
        Assert.Equal(kinds.Length, catalogKinds.Length);
        Assert.Equal(kinds.OrderBy(x => x), catalogKinds.Distinct().OrderBy(x => x));
    }

    [Fact]
    public void Catalog_HasUsableNameAndDescriptionForEveryReport()
    {
        Assert.All(CafeteriaReportCatalog.All, item => { Assert.False(string.IsNullOrWhiteSpace(item.Name)); Assert.False(string.IsNullOrWhiteSpace(item.Description)); });
    }

    [Fact]
    public void EmployeeStatement_SupportsEmployeeFilter()
    {
        var item = Assert.Single(CafeteriaReportCatalog.All, x => x.Kind == CafeteriaReportKind.EmployeeStatement);
        Assert.True(item.SupportsEmployeeFilter); Assert.False(item.SupportsProductFilter);
    }

    [Fact]
    public void ProductSales_SupportsProductFilter()
    {
        var item = Assert.Single(CafeteriaReportCatalog.All, x => x.Kind == CafeteriaReportKind.ProductSales);
        Assert.True(item.SupportsProductFilter);
    }

    [Theory]
    [InlineData(CafeteriaReportKind.PurchaseItemDetail)]
    [InlineData(CafeteriaReportKind.PurchaseByPerson)]
    public void PurchaseReports_AreInCatalog(CafeteriaReportKind kind)
    {
        var item = Assert.Single(CafeteriaReportCatalog.All, x => x.Kind == kind);
        Assert.False(string.IsNullOrWhiteSpace(item.Name));
        Assert.False(string.IsNullOrWhiteSpace(item.Description));
        Assert.Contains("purchase", item.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PurchaseReportGrouping_DefaultsToDay()
    {
        var filter = new ReportFilter(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        Assert.Equal(PurchaseGrouping.Day, filter.Grouping);
    }
}
