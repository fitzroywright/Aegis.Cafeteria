# Aegis.Cafeteria v90

## Compile cleanup

- Updated the Linux customer display for Avalonia 12: `WindowDecorations = Avalonia.Controls.WindowDecorations.None`.
- Disambiguated the Linux pie-chart `Path` type with `ShapePath = Avalonia.Controls.Shapes.Path`.
- Added `Aegis.Cafeteria.Domain.Services` to the Services host so `CafeteriaServiceEventKind`, `AdditionalCreditServiceEvent`, and `StipendServiceEvent` resolve.
- Added static regression checks for all of these compile failures.
- No financial or database behavior changed.
