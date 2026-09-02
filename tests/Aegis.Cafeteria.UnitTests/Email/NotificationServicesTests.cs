namespace Aegis.Cafeteria.UnitTests.Email;

using Aegis.Cafeteria.Services.Email;

public sealed class NotificationServicesTests
{
    [Theory]
    [InlineData("employee@example.com", "employee@example.com")]
    [InlineData("  employee@example.com  ", "employee@example.com")]
    public void EmailAddressValidator_NormalizesValidAddresses(string value, string expected)
    {
        Assert.Equal(expected, EmailAddressValidator.Normalize(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void EmailAddressValidator_RejectsOrIgnoresInvalidAddresses(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Assert.Null(EmailAddressValidator.Normalize(value));
            return;
        }

        Assert.Throws<ArgumentException>(() => EmailAddressValidator.Normalize(value));
    }
}
