namespace Aegis.Cafeteria.Domain.Reporting;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed record CustomerAccountStatement(
    Customer Customer,
    AccountPeriod Period,
    CustomerAccountSnapshot Snapshot,
    IReadOnlyCollection<CustomerAccountStatementEntry> Entries,
    bool IsClosed,
    AccountPeriodClose? Close);
