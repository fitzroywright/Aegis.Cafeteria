# v63

- Removed WinForms autocomplete from the universal customer search field. The NFC/Code/TRN/Name resolver now receives exactly the text entered by the cashier or keyboard-wedge reader.
- Name candidates are shown only in the explicit customer chooser; the universal textbox no longer substitutes or accepts a suggested employee name.
- Preserved exact NFC -> Code -> TRN precedence and contains-based Name search.
- Added a persistence regression proving different partial names resolve to different customer candidate IDs.
- No database schema or business-rule changes.
