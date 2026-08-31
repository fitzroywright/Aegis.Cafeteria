namespace Aegis.Cafeteria.Services.Email;

public sealed record EmailAttachment
(
      string FileName
    , string ContentType
    , byte[] Content
);
