namespace Aegis.Cafeteria.Application.Security;

using Aegis.Cafeteria.Application.Abstractions;

public sealed class AuthorizedProductCatalogMaintenance
(
      IProductCatalogMaintenance inner
    , IManagerAuthorizationRegistry authorization
    , string managerUserId
) : IProductCatalogMaintenance
{
    public Task<IReadOnlyList<ProductCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default)
        => inner.GetAllAsync(cancellationToken);

    public Task<ProductCatalogItem> AddAsync
    (
          string name
        , decimal price
        , CancellationToken cancellationToken = default
    )
    {
        EnsureAuthorized();
        return inner.AddAsync(name, price, cancellationToken);
    }

    public Task<ProductCatalogItem> UpdateAsync
    (
          int productId
        , string name
        , decimal price
        , CancellationToken cancellationToken = default
    )
    {
        EnsureAuthorized();
        return inner.UpdateAsync(productId, name, price, cancellationToken);
    }

    public Task DeleteAsync
    (
          int productId
        , CancellationToken cancellationToken = default
    )
    {
        EnsureAuthorized();
        return inner.DeleteAsync(productId, cancellationToken);
    }

    private void EnsureAuthorized()
    {
        if (string.IsNullOrWhiteSpace(managerUserId) || !authorization.IsAuthorized(managerUserId))
        {
            throw new UnauthorizedAccessException("Manager authorization is required for cafeteria product and price maintenance.");
        }
    }
}
