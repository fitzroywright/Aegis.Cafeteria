# Aegis.Cafeteria v82

Build-fix release based on v81.

- Fixed `PdfReportExporter` compiler errors CS0443/CS1003/CS1525 by replacing an unsupported nested empty collection expression in a traditional collection initializer with explicit `List<PdfLine>` construction.
- Also replaced the page-reset empty collection expression with explicit construction for compiler compatibility.
- Removed the unread `CafeteriaServicesOptions options` primary-constructor parameter from `CafeteriaServicesClient`, resolving CS9113.
- Removed the now-unused configuration namespace import.
- No report calculations, financial rules, exports, or POS behavior changed.
