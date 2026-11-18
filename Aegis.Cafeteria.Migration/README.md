# Aegis.Cafeteria.Migration

One-time cutover utility for importing legacy cafeteria activity through the configured Cafeteria integration plugin.

The migration executable loads an `ILegacyCafeteriaMigrationSource` implementation from the integration plugin. The plugin reads the source tables and supplies generic migration snapshots for dry-run review and submission to Cafeteria Services.

## Safety

The default mode is a dry run. No Cafeteria data is changed unless `--execute` is supplied.

Cafeteria Services must temporarily have:

```json
"Migration": {
  "Enabled": true
}
```

The endpoint still requires Cafeteria management authorization. Disable the migration feature again immediately after cutover.

## Source connection

The preferred form is to pass the integration source connection string:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyDatabase;Encrypt=True;TrustServerCertificate=True" \
  --services-url https://cafeteria-services.ffpja.org \
  --year 2026 \
  --month 10
```

For backward compatibility, `--server` and `--database` may still be supplied instead of `--connection-string`. Those values are used only to construct a generic source connection string; source-system schema knowledge remains inside the plugin.

The default plugin path is:

```text
plugins/Aegis.Cafeteria.Plugin.Integration.dll
```

Override it with `--plugin <path>` when necessary.

## Execute month migration

After reviewing the dry-run totals:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyDatabase;Encrypt=True;TrustServerCertificate=True" \
  --services-url https://cafeteria-services.ffpja.org \
  --year 2026 \
  --month 10 \
  --execute
```

## Product catalog migration

Product bootstrap excludes codes that start with `FFP` after trimming, regardless of case. The integration plugin and migration executable filter these rows. Cafeteria Services rejects a submitted batch containing any reserved code with HTTP 400 and reports the offending zero-based request indexes.

Dry run:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --product-bootstrap \
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyDatabase;Encrypt=True;TrustServerCertificate=True" \
  --services-url https://cafeteria-services.ffpja.org
```

Execute:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --product-bootstrap \
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyDatabase;Encrypt=True;TrustServerCertificate=True" \
  --services-url https://cafeteria-services.ffpja.org \
  --execute
```

The integration plugin is responsible for translating its external source into the generic migration contracts.

## Review and remediate prior imports

Use this procedure only for an explicitly approved cleanup of confirmed POS catalog entries. Preserve a backup/export of the affected catalog rows and all historical records. A patched POS records and acknowledges old reserved import events in its inbox without adding a saleable item. That guard applies during replay and cannot identify catalog entries already materialized by older terminals.

1. Review a read-only export of Services `CafeteriaServiceEvents` and each terminal's `ManagementEventInbox` for `Kind = 7` (`ProductCatalogImport`). Inspect `SourceCode` in `PayloadJson` using the trimmed, case-insensitive `FFP` prefix rule. Record the operation ID, source product ID, name, price and event timestamps for each match.
2. Compare processed inbox events with the terminal's `Products` rows and the catalog's audit or backup evidence. The POS assigns a local product ID and stores the name and price without the source code or ID. An import could also have been skipped because a legitimate product already had that name. A name or price match alone is insufficient proof. Leave ambiguous rows in place for manual investigation.
3. After confirming ownership of a row and obtaining approval, export its current local catalog values, then remove only that entry through POS Product Maintenance on the affected terminal. Review the current sale catalog afterward. Order lines store their own product name and price and have no foreign key to `Products`. Preserve the Services event stream, the POS inbox and its checkpoint, and all orders, refunds and financial records. Keep the source LunchItems policy rows, including `FFP00`.
