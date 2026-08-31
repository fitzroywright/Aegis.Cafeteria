# Aegis.Cafeteria v50

## UI workflow improvements

- Employee name search in the Customers dialog now searches automatically while the cashier types; no Search button click is required.
- Search requests are debounced and older in-flight searches are cancelled so slow results cannot overwrite newer text.
- The order-action area now reserves enough width/height for **New Customer**, **Manager Void**, and **Complete Sale** to remain visible at the supported minimum window size.
- Identified customer data is automatically cleared after POS inactivity. The default is 30 seconds and is configured with `Ui:CustomerInactivityTimeoutSeconds`. Set the value to `0` to disable automatic clearing.
- Keyboard and mouse activity anywhere in the POS resets the inactivity clock. Timeout uses the same full reset as **New Customer**, including clearing the cart and customer-facing second screen.
- No database schema change; v50 continues to use local schema version 48.
