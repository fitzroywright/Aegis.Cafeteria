namespace Aegis.Cafeteria.UnitTests.Identification;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;

public sealed class CustomerLookupResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesCardThenCodeThenTrnThenName()
    {
        var service = new StubIdentificationService();
        service.Candidates = [new CustomerSearchCandidate(7, "Brown")];

        await service.ResolveAsync("Brown");

        Assert.Equal(
            [CustomerIdentifierType.NfcTag, CustomerIdentifierType.Code, CustomerIdentifierType.Trn],
            service.IdentifierCalls);
        Assert.Equal(1, service.SearchCalls);
    }

    [Fact]
    public async Task ResolveAsync_StopsAfterExactCardMatch()
    {
        var account = Account(7, "Card User");
        var service = new StubIdentificationService { NfcResult = account };

        var result = await service.ResolveAsync("123456");

        Assert.Same(account, result.Account);
        Assert.Equal([CustomerIdentifierType.NfcTag], service.IdentifierCalls);
        Assert.Equal(0, service.SearchCalls);
    }


    [Fact]
    public async Task ResolveAsync_NameCandidateNeverAutoLoadsEvenWhenSingleMatch()
    {
        var service = new StubIdentificationService();
        service.Candidates = [new CustomerSearchCandidate(7, "Jane Brown")];

        var result = await service.ResolveAsync("Jane");

        Assert.Null(result.Account);
        Assert.Single(result.Candidates);
        Assert.Equal(0, service.LoadCalls);
    }

    [Theory]
    [InlineData("Brown", true)]
    [InlineData("Br", true)]
    [InlineData("123456", false)]
    [InlineData("A", false)]
    public void LooksLikeNameSearch_OnlySuggestsForPlausibleNames(string value, bool expected)
        => Assert.Equal(expected, CustomerLookupResolver.LooksLikeNameSearch(value));

    private static CustomerAccountView Account(int id, string name) => new(
        new CustomerIdentityView(id, name, null, true),
        new AccountPeriodView(2026, 8, 20, 0, 0, false),
        new CustomerFinancialStatusView(
            CreditLimit: 0, SubsidyLimit: 0, SubsidyUsed: 0, SubsidyAmountRemaining: 0,
            NonSubsidyAmountRemaining: 0, TotalAvailableToSpend: 0, CarriedForwardDebt: 0,
            GrossCustomerLiability: 0, TotalOrderSales: 0, CafeteriaAccountSpending: 0,
            CashSpending: 0, AdditionalCreditApproved: 0, AdditionalCreditUsed: 0,
            AdditionalCreditRemaining: 0, StipendCashPaid: 0, StipendCardReduction: 0,
            PriorPeriodSettlements: 0, PriorPeriodRefundCredits: 0, CustomerPayments: 0,
            CustomerResponsibility: 0, PayrollDeductionRequired: 0, TotalRefunds: 0),
        Array.Empty<CustomerMonthlyTransactionView>(),
        new FfpPublicationStatus(false, 0, 0, null, null));

    private sealed class StubIdentificationService : ICustomerIdentificationService
    {
        public List<CustomerIdentifierType> IdentifierCalls { get; } = [];
        public int SearchCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public CustomerAccountView? NfcResult { get; init; }
        public IReadOnlyList<CustomerSearchCandidate> Candidates { get; set; } = [];

        public Task<CustomerAccountView?> IdentifyAsync(CustomerIdentifierType type, string value, CancellationToken cancellationToken = default)
        {
            IdentifierCalls.Add(type);
            return Task.FromResult(type == CustomerIdentifierType.NfcTag ? NfcResult : null);
        }

        public Task<IReadOnlyList<CustomerSearchCandidate>> SearchAsync(string searchText, int maximumResults = 20, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(Candidates);
        }

        public Task<CustomerAccountView?> LoadAsync(int customerId, CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return Task.FromResult<CustomerAccountView?>(null);
        }
    }
}
