namespace Aegis.Cafeteria.Domain.Identification;

/// <summary>
/// Records how Universal Search identified the customer for a cafeteria transaction.
/// Enum names are part of the reporting and FFP EmployeeIDScans compatibility contract.
/// </summary>
public enum IDMethod
{
    NFC = 1,
    Code = 2,
    TRN = 3,
    Name = 4
}
