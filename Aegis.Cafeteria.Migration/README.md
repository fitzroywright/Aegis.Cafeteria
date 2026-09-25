# Aegis.Cafeteria.Migration

One-time cutover utility for importing legacy month-to-date cafeteria activity from FFP Manager.

This project is intentionally separate from the normal POS and Cafeteria Services runtime. It reads the legacy SQL database directly and submits a migration payload to the feature-gated Cafeteria Services migration endpoint.

The utility imports only legacy scan types:

- 3 - monthly allocation
- 8 - cafeteria purchase
- 4 - stipend/card reduction

It also reads current legacy CreditLimit and AvailableBalance values for reconciliation reporting.

## Safety

The default mode is a dry run. No Cafeteria data is changed unless `--execute` is supplied.

Cafeteria Services must temporarily have:

```json
"Migration": {
  "Enabled": true
}
```

The endpoint still requires Cafeteria management authorization. Disable the migration feature again immediately after cutover.

## Example

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --server JM-SQL01 \
  --database FFPManager \
  --services-url https://cafeteria-services.ffpja.org \
  --year 2026 \
  --month 10
```

After reviewing the dry-run totals, execute:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --server JM-SQL01 \
  --database FFPManager \
  --services-url https://cafeteria-services.ffpja.org \
  --year 2026 \
  --month 10 \
  --execute
```

The source SQL connection uses Windows Integrated Security.
