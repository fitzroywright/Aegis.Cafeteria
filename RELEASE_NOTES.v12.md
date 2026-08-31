# Aegis.Cafeteria v12

## Build-fix and logging cleanup release

- Removed every Serilog package reference and all Serilog source usage.
- Logging now uses Microsoft.Extensions.Logging only.
- Added native asynchronous rolling-file ILoggerProvider.
- Added native bounded asynchronous GELF/UDP ILoggerProvider for Graylog (default 10.10.0.3:12201).
- Graylog/file failures are isolated from the POS transaction path.
- Fixed Windows UI namespace collision by fully qualifying System.Windows.Forms.Application and ApplicationConfiguration.
- Replaced the Avalonia App subclass bootstrap with Avalonia's code-only ClassicDesktopStyleApplicationLifetime pattern.
- This removes the reported ApplicationLifetime, Styles and Application namespace/type errors.
- Reworked the logging test so it no longer attempts to dispose ILogger itself.
- Bitwarden Secrets Manager architecture from v11 is unchanged.
- No timer polling was reintroduced.

## Verification

The static audit passes and no active source or project file contains a Serilog reference.
The generation environment does not contain the .NET SDK, so restore/build/test must still be executed on the Windows laptop using the included scripts.
