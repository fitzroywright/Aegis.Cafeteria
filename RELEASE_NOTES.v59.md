# v59

- Fixed Windows universal customer search partial-name behavior.
- Changed WinForms customer search AutoCompleteMode from SuggestAppend to Suggest so suggestions never rewrite the cashier-entered prefix.
- NFC/Card, Code and TRN resolution remains exact and unchanged.
- Name typing continues to show suggestions from the local cache; the cashier's typed text remains authoritative.
- No database schema or business-rule changes.
