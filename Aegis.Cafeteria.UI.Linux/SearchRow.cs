using Aegis.Cafeteria.Application.Accounts;

namespace Aegis.Cafeteria.UI.Linux;

internal sealed record SearchRow(CustomerSearchCandidate Customer)
{
    public override string ToString() => Customer.Name;
}
