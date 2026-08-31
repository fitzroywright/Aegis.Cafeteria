# Aegis.Cafeteria v89

- Applies the Fitzroy/IoT private-field naming convention across `src`: camelCase with no underscore prefix.
- Adds `.editorconfig` and `docs/CODING-STANDARDS.md` to formalize the Fitzroy/IoT C# house style.
- Fixes Linux `Path` ambiguity by explicitly aliasing `System.IO.Path` in the Avalonia main window.
- Fixes Avalonia window-decoration enum/property ambiguity by fully qualifying `Avalonia.Controls.SystemDecorations.None`.
- Moves Cafeteria Services immutable event contracts to `Aegis.Cafeteria.Domain.Services`, a shared compile-time dependency for Application, Infrastructure and Services.
- Updates unit-test and runtime references to the new service-contract namespace.
- Updates static regression checks for the new private-field naming convention.
