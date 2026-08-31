# Aegis.Cafeteria v13

Build-fix release based on v12.

- Windows WinForms startup no longer uses `ApplicationConfiguration.Initialize()`.
- Windows startup now uses `System.Windows.Forms.Application.EnableVisualStyles()` and `SetCompatibleTextRenderingDefault(false)` before `Application.Run(...)`.
- Avalonia Linux startup now fully qualifies `Avalonia.Controls.ShutdownMode.OnLastWindowClose`.
- Serilog remains absent; Microsoft.Extensions.Logging-based providers from v12 remain unchanged.
- Secrets Manager architecture from v11/v12 remains unchanged.
