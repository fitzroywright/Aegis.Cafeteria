namespace Aegis.Cafeteria.Application.Abstractions;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;

public interface ICustomerIdentificationService
{
    Task<CustomerAccountView?> IdentifyAsync(CustomerIdentifierType type, string value, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerSearchCandidate>> SearchAsync(string searchText, int maximumResults = 20, CancellationToken cancellationToken = default);
    Task<CustomerAccountView?> LoadAsync(int customerId, CancellationToken cancellationToken = default);
}

public sealed record CustomerSearchCandidate(
    int CustomerId,
    string Name,
    bool IsEligibleForCredit = false,
    byte[]? Photo = null,
    string Department = "",
    string Manager = "",
    string EmailAddress = "",
    string Extension = "");
