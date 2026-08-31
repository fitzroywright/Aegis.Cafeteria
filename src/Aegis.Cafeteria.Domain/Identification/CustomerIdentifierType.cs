namespace Aegis.Cafeteria.Domain.Identification;

public enum CustomerIdentifierType
{
    NfcTag = 1,
    Code = 2,
    // Compatibility alias for older code/tests that called the FFP employee Code a barcode.
    Barcode = Code,
    Trn = 3
}
