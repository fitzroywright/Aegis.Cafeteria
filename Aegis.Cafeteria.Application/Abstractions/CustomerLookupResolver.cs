namespace Aegis.Cafeteria.Application.Abstractions;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;

public sealed record CustomerLookupResult(
    CustomerAccountView? Account,
    IReadOnlyList<CustomerSearchCandidate> Candidates,
    CustomerIdentifierType? MatchedIdentifierType = null)
{
    public bool HasAccount => Account is not null;
    public bool HasCandidates => Candidates.Count > 0;
}

public static class CustomerLookupResolver
{
    /// <summary>
    /// Resolve a cashier-entered value using the POS precedence rule:
    /// NFC, employee Code, TRN, then name search. Exact identifiers are
    /// never returned to the UI; only name-search candidates are projected.
    /// </summary>
    public static async Task<CustomerLookupResult> ResolveAsync(
        this ICustomerIdentificationService identification,
        string value,
        int maximumNameResults = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identification);
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return new CustomerLookupResult(null, await identification.SearchAsync(string.Empty, maximumNameResults, cancellationToken));

        foreach (var type in new[] { CustomerIdentifierType.NfcTag, CustomerIdentifierType.Code, CustomerIdentifierType.Trn })
        {
            var account = await identification.IdentifyAsync(type, trimmed, cancellationToken);
            if (account is not null)
                return new CustomerLookupResult(account, Array.Empty<CustomerSearchCandidate>(), type);
        }

        // Name matching is deliberately non-authoritative. Even a single partial-name
        // candidate must be confirmed by the cashier; only exact NFC/Code/TRN
        // identifiers may auto-load a customer.
        var candidates = await identification.SearchAsync(trimmed, maximumNameResults, cancellationToken);
        return new CustomerLookupResult(null, candidates);
    }

    public static bool LooksLikeNameSearch(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length >= 2 && trimmed.Any(char.IsLetter);
    }
}
