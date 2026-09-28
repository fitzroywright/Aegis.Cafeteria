namespace Aegis.Cafeteria.Services.Identity;

public sealed class EmployeeMetadataOptions
{
    // CommonSecurity preserves existing installations. None uses workforce facts only.
    public string Mode { get; set; } = "CommonSecurity";
    public string SourceId { get; set; } = "common-security-directory";
    public string AssemblyPath { get; set; } = string.Empty;
    public string[] Fields { get; set; } = ["EmailAddress", "Department", "Manager", "Extension"];

    public bool Owns(string field)
    {
        return !this.Mode.Equals("None", StringComparison.OrdinalIgnoreCase)
            && this.Fields.Contains(field, StringComparer.OrdinalIgnoreCase);
    }
}
