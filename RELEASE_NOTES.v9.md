# Aegis.Cafeteria v9

v9 adds the first simple POS user-interface project while preserving the v8 architecture.

## Added

- `Aegis.Cafeteria.UI` WinForms project targeting `net10.0-windows`.
- NFC, barcode, TRN and manual-search customer identification.
- Customer photo, current-month financial summary and transaction display.
- Local product catalog query and product search.
- Simple order cart and split cafeteria-account/cash checkout.
- `PosCheckoutService` application coordinator so UI code does not persist EF rows directly.
- Live customer refresh through `ILocalAccountChangePublisher`.
- Event-driven sync pulse on customer identification; no periodic polling.
- Starter `appsettings.json` with Graylog `10.10.0.3:12201`.
- Checkout unit tests.

## Intent

This UI is deliberately plain WinForms with no DevExpress dependency. It is a functional starting point that can be restyled or converted to DevExpress controls without changing application-layer contracts.
