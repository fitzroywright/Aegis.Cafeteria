namespace Aegis.Cafeteria.UnitTests.Integration;

using Aegis.Cafeteria.Infrastructure.Identity;
using Common.Security.Models;

public sealed class DirectoryProfileCacheTests
{
    [Fact]
    public void Get_EmptyCache_ReturnsNull()
    {
        DirectoryProfileCache cache = new();

        Assert.Null(cache.Get("fwright"));
    }

    [Theory]
    [InlineData("fwright")]
    [InlineData("FWRIGHT")]
    [InlineData("FFPJM\\fwright")]
    [InlineData("fwright@fftpj.local")]
    public void Get_NormalizesLoginName(string loginName)
    {
        DirectoryProfileCache cache = new();
        DirectoryUserProfile employee = new("fwright", "Fitzroy Wright", "fwright@fftpj.local", "IT", "1234", "Manager", []);
        cache.Replace(new Dictionary<string, DirectoryUserProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["fwright"] = employee
        });

        DirectoryUserProfile? result = cache.Get(loginName);

        Assert.Same(employee, result);
    }

    [Fact]
    public void Replace_ReplacesPriorSnapshot()
    {
        DirectoryProfileCache cache = new();
        cache.Replace(new Dictionary<string, DirectoryUserProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["olduser"] = new("olduser", "Old User", "old@example.com", "Old", "", "", [])
        });

        DirectoryUserProfile current = new("newuser", "New User", "new@example.com", "New", "", "", []);
        cache.Replace(new Dictionary<string, DirectoryUserProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["newuser"] = current
        });

        Assert.Null(cache.Get("olduser"));
        Assert.Same(current, cache.Get("newuser"));
    }

    [Fact]
    public void Replace_CopiesInputDictionary()
    {
        DirectoryProfileCache cache = new();
        Dictionary<string, DirectoryUserProfile> source = new(StringComparer.OrdinalIgnoreCase)
        {
            ["fwright"] = new("fwright", "Fitzroy Wright", "fwright@fftpj.local", "IT", "1234", "", [])
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
        DirectoryProfileCache cache = new();

        Assert.Null(cache.Get(loginName));
    }
}
