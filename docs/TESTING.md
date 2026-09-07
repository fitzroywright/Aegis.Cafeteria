# Testing Guide

## Automated verification

Run the release build and automated test suite before deployment:

```powershell
dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
python scripts\static-audit.py
```

## Functional acceptance coverage

- Employee search by NFC, code, TRN and name.
- Cash-customer sale.
- Employee cafeteria-account sale.
- Split tender sale.
- Void current sale.
- Full refund and partial refund.
- Customer payment against Employee Amount Due.
- Additional credit approval.
- Stipend or card reduction.
- Manual month opening.
- Daily dashboard and report exports.
- FFP publication and balance projection verification.
- Audit query for protected and mutating operations.

## Technical acceptance coverage

- Service startup.
- Health endpoint.
- Development Swagger.
- Authentication for management endpoints.
- Local POS store access.
- Service store access.
- FFP database connectivity through the service layer.
- Background worker logs and publication behavior.
