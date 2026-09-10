namespace Aegis.Cafeteria.UnitTests.Integration;

using Aegis.Cafeteria.Infrastructure.ActiveDirectory;

public sealed class EmployeeDirectoryMetadataCacheTests
{
    [Fact]
    public void Get_EmptyCache_ReturnsNull()
    {
        EmployeeDirectoryMetadataCache cache = new();

        Assert.Null(cache.Get("fwright"));
    }

    [Theory]
    [InlineData("fwright")]
    [InlineData("FWRIGHT")]
    [InlineData("FFPJM\\fwright")]
    [InlineData("fwright@fftpj.local")]
    public void Get_NormalizesLoginName(string loginName)
    {
        EmployeeDirectoryMetadataCache cache = new();
        ActiveDirectoryEmployeeRecord employee = new("fwright", "fwright@fftpj.local", "IT", "1234", "Manager");
        cache.Replace(new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["fwright"] = employee
        });

        ActiveDirectoryEmployeeRecord? result = cache.Get(loginName);

        Assert.Same(employee, result);
    }

    [Fact]
    public void Replace_ReplacesPriorSnapshot()
    {
        EmployeeDirectoryMetadataCache cache = new();
        cache.Replace(new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["olduser"] = new("olduser", "old@example.com", "Old", "", "")
        });

        ActiveDirectoryEmployeeRecord current = new("newuser", "new@example.com", "New", "", "");
        cache.Replace(new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["newuser"] = current
        });

        Assert.Null(cache.Get("olduser"));
        Assert.Same(current, cache.Get("newuser"));
    }

    [Fact]
    public void Replace_CopiesInputDictionary()
    {
        EmployeeDirectoryMetadataCache cache = new();
        Dictionary<string, ActiveDirectoryEmployeeRecord> source = new(StringComparer.OrdinalIgnoreCase)
        {
            ["fwright"] = new("fwright", "fwright@fftpj.local", "IT", "1234", "")
        };
        cache.Replace(source);

        source.Clear();

        Assert.NotNull(cache.Get("fwright"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_MissingLoginName_ReturnsNull(string? loginName)
    {
        EmployeeDirectoryMetadataCache cache = new();

        Assert.Null(cache.Get(loginName));
    }
}
