# Aegis.Cafeteria v86

## Report preview/export responsiveness

- Moves report exporter rendering off the Windows WinForms UI thread.
- Moves report exporter rendering off the Linux Avalonia UI thread.
- Preview/export now shows a rendering status while work is in progress.
- PDF, Excel, CSV, HTML and preview payload generation use the same non-blocking helper.
- Final file writing/opening remains asynchronous.
- No report calculations or accounting rules changed.

## Verification

Static audit passes, including a v86 regression rule requiring off-UI-thread exporter rendering on both platforms.

The .NET SDK is not installed in the execution environment, so a Release build and unit tests were not run here.
