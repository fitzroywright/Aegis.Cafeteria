# Aegis.Cafeteria v53

## WinForms startup layout stability

- Fixed `InvalidOperationException: SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize` on Windows POS startup.
- The main `SplitContainer` no longer applies a fixed `SplitterDistance` or production panel minimums while WinForms is still using its temporary pre-layout width.
- Split sizing is now applied after real layout and on resize, with values clamped to the available client width.
- The production target remains a 455 px customer panel with 420 px / 650 px minimum panel widths whenever the screen has room.
- On transient small widths, the constraints degrade safely instead of throwing.
- No persistence, checkout, FFP, funding, reporting, or database-schema behavior changed.

SQLite schema remains version 48.
