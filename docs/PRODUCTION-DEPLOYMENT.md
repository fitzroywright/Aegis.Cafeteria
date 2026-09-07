# Production Deployment and Configuration

## Production topology

- POS terminals run the Windows or Linux POS client with local SQLite storage.
- The service layer runs as the central integration and management boundary.
- Only the service layer connects directly to the central FFP database.
- Management tools call the service layer for funding, credit, stipend, month-opening and audit operations.

## Environment separation

Development enables Swagger, coordinated reset and developer launch profiles. Production keeps Swagger disabled and uses production configuration with secret references instead of plain secret values.

## Secrets

Production configuration should store secret identifiers, not secret values. Required logical secrets include the FFP database connection string, service API key and any messaging bearer token when messaging is enabled.

## Deployment verification

Run the build gate before production deployment:

```powershell
dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
python scripts\static-audit.py
```

After deployment, verify service startup, health endpoint response, authentication for management endpoints, POS connection to the service, local sales, refund workflow, report export and FFP publication.

## Operational safeguards

- Keep cashier work local-first.
- Keep management operations authenticated.
- Keep production Swagger disabled.
- Keep audit trail enabled for protected and mutating operations.
- Monitor publication workers and service logs.
