# Weekly Messaging and Scheduled Email — v88

There are two independent messaging paths.

## POS weekly messaging

The POS can generate previous Monday-Sunday customer summaries from local SQLite and dispatch through `ICafeteriaMessagingGateway`. `WeeklySummaryDispatches` prevents duplicate sends. If Messaging is enabled and a bearer token is required, production resolves `MessagingBearerToken` from Bitwarden.

## Services scheduled email

`ScheduledEmailWorker` can send:

- daily management summary;
- previous-week employee ledger email using the configured FFP Employee email column.

It is disabled by default. The current SMTP implementation assumes an SMTP relay that does not require username/password authentication. If FFP's production SMTP relay requires authenticated SMTP, add credential support before enabling this feature; do not place SMTP credentials in appsettings.

Email failure must never block POS sales or FFP ledger publication.
