# Cafeteria email delivery

Cafeteria Services delivers queued emails through the Common.Messaging Microsoft
Graph email channel (Microsoft 365 / Office 365). The direct SMTP sender has been
removed. Daily Sales, employee summaries, Employee Amount Due, manual resend, and
Test Email keep using the existing Services email queue.

## Deployment

1. Update Common.Messaging with attachment support before building Cafeteria
   Services. Publish Services with its referenced Common.Messaging assembly.
2. Configure an Entra application with Microsoft Graph application `Mail.Send`
   permission and administrator consent, authorized for the chosen sender mailbox.
3. Store its client secret in Common.Secrets/OpenBao. Configure the secret name,
   never the secret value, in the email settings.
4. Configure Services:

```json
"EmailDelivery": {
  "Enabled": true,
  "TenantId": "your-tenant-id",
  "ClientId": "your-application-id",
  "SenderUpn": "your-sender-mailbox@example.org",
  "ClientSecretName": "messaging/msgraph/client-secret"
}
```

`EmailDelivery:Enabled` controls sending all queued emails. Its default is false,
so deploying this change does not immediately drain old queued mail. Review the
pending queue, then enable delivery and restart Services. `ScheduledEmail:Enabled`
separately controls generating scheduled reports. Manual/Test Email messages can
be queued while delivery is disabled and will remain pending.

Old `ScheduledEmail:SmtpHost`, `SmtpPort`, `EnableSsl`, `FromAddress`, and
`SmtpCredential` settings are no longer used. There is no SMTP fallback.

## Verification and behavior

- Run Test Email to an approved test recipient and verify the actual mailbox.
  It can also target the sender mailbox; Cafeteria disables sender suppression.
- Verify a Daily Sales/Amount Due report with an attachment.
- Queue a message with delivery disabled, restart, enable delivery, and verify
  the same queued row is processed. Provider failures remain pending with retry
  details. No credentials are passed to POS.
- Existing plain-body rows and `AEGIS-EMAIL-ENVELOPE-V1` attachment envelopes stay
  readable; no email-table migration is required.
- Graph acceptance is recorded as `graph-accepted:<queue-id>`. The existing
  `SentAt` field means provider acceptance, not proof of final mailbox delivery.
- Dispatch keys continue preventing duplicate queue insertion. Delivery remains
  at-least-once: a crash after Graph accepts mail but before the local success
  save can produce a duplicate. Graph sendMail does not provide exactly-once
  delivery from the local queue ID.
- The shared provider supports small file attachments, capped conservatively at
  2.5 MB combined raw attachment bytes and 4 MB serialized request size. Larger
  reports remain pending with a clear error; split them before retrying. This
  change does not implement Graph large-attachment upload sessions.
- Email failure never blocks POS sales or external ledger publication.

The separate optional POS HTTP weekly-summary gateway is unchanged. The Services
email path does not use that gateway or require another messaging server.

Reference: https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0
