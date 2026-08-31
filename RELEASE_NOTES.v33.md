# Aegis.Cafeteria v33

## Windows startup splash

- Adds an immediate Food For The Poor Jamaica / Aegis Cafeteria startup splash window.
- The splash runs on its own STA WinForms message loop, so async startup operations can update it safely.
- Displays live startup stages for configuration, secure settings, local database initialization, FFP employee refresh, holiday refresh, company subsidy refresh, FFP LunchItems product refresh, and hosted background services.
- Shows a clear cached-data warning state when the FFP refresh is unavailable rather than appearing frozen.
- Closes only after the main POS form has been created, reducing the blank-window interval during startup.
- No customer identifiers, credentials, connection strings, or other sensitive values are displayed on the splash.

All v32 product-catalog, cash-customer, privacy, eligibility, offline, and FFP integration behavior is retained.
