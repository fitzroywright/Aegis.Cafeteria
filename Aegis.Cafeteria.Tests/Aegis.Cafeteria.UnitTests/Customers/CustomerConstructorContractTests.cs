using Aegis.Cafeteria.Domain.Customers;

namespace Aegis.Cafeteria.UnitTests.Customers;

public sealed class CustomerConstructorContractTests
{
    [Fact]
    public void Customer_HasSingleGreenfieldConstructor()
    {
        var constructors = typeof(Customer).GetConstructors();
        var constructor = Assert.Single(constructors);
        var parameters = constructor.GetParameters();

        Assert.Equal(
            ["id", "name", "photo", "isEligibleForCredit", "additionalCreditLimit", "isActive", "employmentStartDate"],
            parameters.Select(x => x.Name).ToArray());
    }
}
