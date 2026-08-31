# Aegis.Cafeteria v34

- Preserves the FFP-branded Windows startup splash screen introduced in v33, including live startup-stage messages for configuration, local database initialization, employee/holiday/subsidy/product refresh, and terminal services.
- Removes internal product/source IDs from the cashier-facing Windows product catalog. The catalog now explicitly renders only Item and Price columns.
- Linux product list remains name-and-price only; internal product IDs remain available solely behind the selection/cart model and are not displayed.
- No changes to product identity persistence, cart behavior, checkout, FFP `LunchItems` synchronization, or offline product caching.
