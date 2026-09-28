namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;
using System.Reflection;

public sealed class CustomerAccountCalculatorContractTests
{
    [Fact]
    public void Calculate_HasOneCanonicalSignature_WithRequiredStipends()
    {
        MethodInfo method = Assert.Single(
            typeof(CustomerAccountCalculator)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public),
            x => x.Name == nameof(CustomerAccountCalculator.Calculate));

        ParameterInfo[] parameters = method.GetParameters();

        Assert.Equal(
            new[]
            {
                "customer",
                "period",
                "allocation",
                "orders",
                "additionalCreditApprovals",
                "payments",
                "carriedForwardDebts",
                "priorPeriodSettlements",
                "stipends",
                "priorPeriodRefundCredits"
            },
            parameters.Select(x => x.Name).ToArray());

        Assert.False(parameters.Single(x => x.Name == "stipends").IsOptional);
        Assert.True(parameters.Single(x => x.Name == "priorPeriodRefundCredits").IsOptional);
    }
}

