# Aegis.Cafeteria.Presentation

Shared presentation semantics for Cafeteria clients.

This project owns cross-platform display conventions that must remain consistent between Windows WinForms and Linux Avalonia clients, including:

- currency and negative-number formatting;
- date/time and 12-hour clock formatting;
- common cafeteria terminology such as Employee Card, Employee Credit and Cash;
- customer eligibility and period display text;
- common tender-summary wording.

It must not contain WinForms or Avalonia controls, colors, fonts, layout, event handling, navigation, input collection, persistence, integration logic, or cafeteria business rules.

Business rules belong in Aegis.Cafeteria.Application or Aegis.Cafeteria.Domain. Framework-specific rendering stays in Aegis.Cafeteria.UI or Aegis.Cafeteria.UI.Linux.
