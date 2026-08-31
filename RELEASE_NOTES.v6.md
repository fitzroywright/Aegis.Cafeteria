# Aegis.Cafeteria v6

## Correctness and parity

- Enforces closed account periods inside `PosCheckoutService`; checkout can no longer bypass the period lock through a non-UI caller.
- Routes management stipend creation through the existing `StipendService` business rules. Services reconstructs the current monthly allocation, FFP account availability, monthly purchases/refunds/payments, and prior stipend reductions before accepting a stipend event.
- Rejects a management stipend request when the supplied card reduction does not equal the reduction allowed by current subsidy and cafeteria availability.
- Adds Linux `Administration > Price Maintenance...` using the same local `IProductCatalogMaintenance` implementation and manager authorization boundary as Windows.
- Corrects FFP integration documentation so the POS-local database is the saleable product catalog; `LunchItems` is not restored as a product source.
- No legacy F1/F2/F3/F4/F10 cashier shortcut work was added.
