# Aegis.Cafeteria v10

- Added separate `Aegis.Cafeteria.UI.Linux` project.
- Linux UI is Avalonia 12.1.0 targeting .NET 10.
- Existing Windows WinForms UI is retained unchanged.
- Linux UI uses the same local-first SQLite persistence, customer-identification, account-query, product-catalog, checkout, synchronization, SignalR and logging services.
- Supports NFC/barcode/TRN/search, customer photo/financial view, transactions, products, cart and split tender.
- Uses event-driven synchronization; no periodic polling.
- Uses platform-neutral log path syntax.
- Designed for JetBrains Rider on Linux; Debian 13 is supported by current Avalonia 12 documentation.
