namespace Aegis.Cafeteria.UnitTests.Integration;

using Aegis.Cafeteria.Application.Integration;
using System.Text.Json;

public sealed class CafeteriaPolicySnapshotTests
{
    [Fact]
    public void Deserialize_OmittedSubsidyConsumptionMode_UsesStandard()
    {
        const string json = """
        {
          "year": 2026,
          "month": 9,
          "dailyCreditLimitRate": 900,
          "dailyCompanySubsidyRate": 650,
          "workingDays": [
            "2026-09-01",
            "2026-09-02"
          ],
          "version": "ffp-2026-09-v1",
          "effectiveFrom": "2026-09-01T00:00:00Z"
        }
        """;

        CafeteriaPolicySnapshot? policy =
            JsonSerializer.Deserialize<CafeteriaPolicySnapshot>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(policy);
        Assert.Equal(SubsidyConsumptionMode.Standard, policy!.SubsidyConsumptionMode);
        policy.Validate();
    }

    [Fact]
    public void Validate_AcceptsExplicitSupportedMode()
    {
        CafeteriaPolicySnapshot policy = new(
            2026,
            9,
            900m,
            650m,
            [new DateOnly(2026, 9, 1)],
            "adapter-v1",
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            SubsidyConsumptionMode.EmployerFirst);

        policy.Validate();

        Assert.Equal(SubsidyConsumptionMode.EmployerFirst, policy.SubsidyConsumptionMode);
    }
}
