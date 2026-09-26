# Aegis.Cafeteria.Migration

One-time cutover utility for importing legacy cafeteria activity through the configured Cafeteria integration plugin.

The migration executable contains no source-system table, column, transaction-type, or product-policy knowledge. It loads an `ILegacyCafeteriaMigrationSource` implementation from the integration plugin, requests generic migration snapshots, displays dry-run totals, and submits those generic records to Cafeteria Services.

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
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyCafeteria;Encrypt=True;TrustServerCertificate=True" \
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
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyCafeteria;Encrypt=True;TrustServerCertificate=True" \
  --services-url https://cafeteria-services.ffpja.org \
  --year 2026 \
  --month 10 \
  --execute
```

## Product catalog migration

Product bootstrap also goes through the integration plugin. The migration executable does not know the source product table or reserved policy-row rules.

Dry run:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --product-bootstrap \
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyCafeteria;Encrypt=True;TrustServerCertificate=True" \
  --services-url https://cafeteria-services.ffpja.org
```

Execute:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- \
  --product-bootstrap \
  --connection-string "Integrated Security=SSPI;Pooling=false;Data Source=LEGACY-SQL;Initial Catalog=LegacyCafeteria;Encrypt=True;TrustServerCertificate=True" \
  --services-url https://cafeteria-services.ffpja.org \
  --execute
```

The integration plugin is responsible for translating its external source into the generic migration contracts.
