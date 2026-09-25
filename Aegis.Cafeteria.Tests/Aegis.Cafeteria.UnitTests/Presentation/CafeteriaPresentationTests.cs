namespace Aegis.Cafeteria.UnitTests.Presentation;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Presentation;
using System.Globalization;

public sealed class CafeteriaPresentationTests
{
    [Fact]
    public void Money_UsesConfiguredPresentationCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo originalUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo culture = new("en-US");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            Assert.Equal("$1,234.50", CafeteriaDisplay.Money(1234.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void Eligibility_IsSharedAcrossUiClients()
    {
        CustomerAccountView account = Account(isCash: false, eligible: true);

        Assert.Equal("ELIGIBLE FOR CAFETERIA CREDIT", CafeteriaDisplayText.Eligibility(account));
    }

    [Fact]
    public void Period_UsesSharedWording()
    {
        CustomerAccountView account = Account(isCash: false, eligible: true);

        Assert.Contains("September 2026", CafeteriaDisplayText.Period(account));
        Assert.Contains("20 eligible work days", CafeteriaDisplayText.Period(account));
    }

    [Fact]
    public void Time_UsesTwelveHourClock()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            Assert.True(CafeteriaDisplay.TryFormatTime("14:05", out string formatted));
            Assert.Equal("2:05 PM", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static CustomerAccountView Account(bool isCash, bool eligible)
        => new
        (
              new CustomerIdentityView(42, "Employee", null, eligible, IsCashCustomer: isCash)
            , new AccountPeriodView(2026, 9, 20, 0m, 0m, false)
            , new CustomerFinancialStatusView
            (
                  CreditLimit: 1000m
                , SubsidyLimit: 500m
                , SubsidyUsed: 0m
                , SubsidyAmountRemaining: 500m
                , NonSubsidyAmountRemaining: 500m
                , TotalAvailableToSpend: 1000m
                , CarriedForwardDebt: 0m
                , GrossCustomerLiability: 0m
                , TotalOrderSales: 0m
                , CafeteriaAccountSpending: 0m
                , CashSpending: 0m
                , AdditionalCreditApproved: 0m
                , AdditionalCreditUsed: 0m
                , AdditionalCreditRemaining: 0m
                , StipendCashPaid: 0m
                , StipendCardReduction: 0m
                , PriorPeriodSettlements: 0m
                , PriorPeriodRefundCredits: 0m
                , CustomerPayments: 0m
                , CustomerResponsibility: 0m
                , EmployeeAmountDue: 0m
                , TotalRefunds: 0m
            )
            , []
            , new ExternalPublicationStatus(false, 0, 0, null, null)
            , Identification: new CustomerIdentificationContext(IDMethod.Name, "TEST")
        );
}
