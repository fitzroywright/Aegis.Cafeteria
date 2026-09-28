# Employee metadata and notification email

Employee facts and operator authentication are separate choices. Cafeteria Services owns metadata enrichment; both automatic and manual POS refresh consume the same enriched employee batches. The metadata worker requests ten employees at a time. Last successful metadata is persisted in Services, so a restart or failed directory lookup does not erase it.

## Field update contract

`ExternalCustomerSnapshot.Metadata` contains independent optional updates for EmailAddress, Department, Manager, and Extension. Each supplied update has `Value` and `Source`.

- Missing field: no authoritative answer; retain the cached value.
- Supplied nonblank value: replace the cached value.
- Supplied empty value: the authoritative source confirmed the field is blank; clear it.
- Legacy snapshots with no Metadata object: accept nonblank fields and preserve cached values for blanks.

An unmatched employee, missing login, failed query, or omitted field is not an authoritative clear. Provider results must contain only requested employee IDs. The Common.Security adapter only returns updates for matched profiles. Its legacy manager field cannot distinguish failed manager resolution from an empty manager, so it conservatively preserves blank manager answers. New metadata providers can explicitly clear that field.

## Provider configuration

The Services `EmployeeMetadata` section is independent of `AuthenticationPlugin`:

```json
{
  "EmployeeMetadata": {
    "Mode": "CommonSecurity",
    "SourceId": "common-security-directory",
    "Fields": ["EmailAddress", "Department", "Manager", "Extension"]
  }
}
```

Modes:

| Mode | Behavior |
| --- | --- |
| CommonSecurity | Uses the current authentication plugin's optional IDirectoryProfileProvider. Compatible with the existing AD installation. |
| None | No directory worker. Employee fields come from the workforce integration; a cashier can still record a personal notification address. |
| Plugin | Loads a separate employee metadata provider from EmployeeMetadata:AssemblyPath. Authentication can use a different provider. |

`Fields` selects fields owned by the external metadata provider; all others belong to the workforce integration. An unavailable owner does not cause another provider to take over its fields. Exactly one external metadata provider is selected. Changing providers filters out the old provider's stored observations until the new provider supplies an answer.

For an installation without directory metadata, set `EmployeeMetadata:Mode` to `None`. Authentication remains required and must be configured separately; disabling metadata does not disable sign-in or permissions.

A custom assembly must export exactly one public, concrete `IEmployeeMetadataProvider` from Aegis.Cafeteria.Application. Its constructor can use DI services, including IConfiguration for provider settings. Return patches keyed by requested Cafeteria CustomerId; each supplied field's Source must equal the provider's stable SourceId. Do not reuse a SourceId for a different authority. Copy the plugin and its dependencies into the configured plugin directory. This change provides the extension point, not an HR/Entra connector implementation.

## Notification preference and review

Employee email is a suggestion. The confirmed notification address and consent remain in CustomerNotificationPreferences. Accepting an authoritative suggestion also saves its source and the exact acknowledged suggestion in CustomerEmailReviews. Choosing a different personal address removes that association.

A subsequent authoritative email change or clear marks a source-associated preference for review. It never silently replaces or deletes the delivery address. The pending state is derived from the durable latest observation and durable acknowledgement, so it survives restarts and is shared by all POS terminals. A missing lookup cannot trigger a review.

After a completed sale, Windows and Linux POS show an amber **Confirm email address** prompt when review is due. The employee may keep the current address, edit it, stop emails, or ask later. Closing the prompt or choosing Ask later saves nothing; the review remains pending. Employee Email Preferences also shows the amber review message. These indicators are in the post-sale prompt and preference editor; no customer-header badge is added.

Keeping an address acknowledges exactly the observation displayed, so a source change while the prompt is open still requires review next time. Changing to a personal address detaches it from directory changes. Stopping emails records the opt-out. The application continues using the previously confirmed address while review is pending. An unavailable preference service defers the prompt; it does not invent a new opt-in choice or affect the completed sale.

Existing preferences have no reliable provenance and are treated as employee-selected until reconfirmed. The migration does not guess that an address came from AD merely because the strings match. Existing queued emails are not rewritten by this change.

## Authentication scope

Common.Security already loads IAuthenticationPlugin. Employee metadata no longer needs to be supplied by that plugin: select None or a standalone metadata plugin. A future local-account authentication plugin must implement the existing Common.Security authentication contracts and retain Cafeteria authorization checks. The current credential exchange is username/password based; OIDC requires a browser/token sign-in flow, not just a different LDAP provider. This change does not implement a local password store or OIDC login.

## Deployment and validation

Upgrade POS first for this email fix, then Services: the new POS preserves ambiguous blanks from an older Services version. An old POS cannot understand Metadata and can still erase an email on an unavailable lookup. Deploy the Common.Security AD plugin LDAP-filter fix as well; the previous multi-employee filter omitted closing parentheses on each account clause.

Services adds EmployeeMetadata and CustomerEmailReviews tables idempotently; existing preferences, transactions, balances, and POS database schema remain intact. Deploy matching Application/Infrastructure binaries with each application.

Regression coverage includes legacy blanks, explicit clears, independent fields, field ownership, personal addresses, persisted review after restart, concurrent source change during confirmation, opt-out, and LDAP escaping/batch filter structure. Build and run the UnitTests and PersistenceTests projects plus Common.Security.UnitTests in the normal workspace with .NET 10 and the sibling Common repositories. This editing environment had no dotnet executable, so C# tests and UI behavior still need execution there.
