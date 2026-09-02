namespace Aegis.Cafeteria.UnitTests.Email;

using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task NotificationPreferenceStore_NewCustomer_DefaultsToNotAsked()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        NotificationPreferenceStore store = new(database.Factory);

        CustomerNotificationPreference preference = await store.GetCustomerAsync(42);

        Assert.Equal(42, preference.CustomerId);
        Assert.False(preference.PreferenceAsked);
        Assert.False(preference.WeeklySummaryEnabled);
        Assert.Null(preference.EmailAddress);
    }

    [Fact]
    public async Task NotificationPreferenceStore_Enable_PersistsConfirmedEmail()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        NotificationPreferenceStore store = new(database.Factory);

        CustomerNotificationPreference saved = await store.SetCustomerAsync(42, true, " employee@example.com ");
        CustomerNotificationPreference loaded = await store.GetCustomerAsync(42);

        Assert.True(saved.PreferenceAsked);
        Assert.True(loaded.PreferenceAsked);
        Assert.True(loaded.WeeklySummaryEnabled);
        Assert.Equal("employee@example.com", loaded.EmailAddress);
    }

    [Fact]
    public async Task NotificationPreferenceStore_Disable_MarksAskedAndClearsEmail()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        NotificationPreferenceStore store = new(database.Factory);
        await store.SetCustomerAsync(42, true, "employee@example.com");

        CustomerNotificationPreference preference = await store.SetCustomerAsync(42, false, null);

        Assert.True(preference.PreferenceAsked);
        Assert.False(preference.WeeklySummaryEnabled);
        Assert.Null(preference.EmailAddress);
    }

    [Fact]
    public async Task NotificationPreferenceStore_EnableWithoutEmail_IsRejected()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        NotificationPreferenceStore store = new(database.Factory);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetCustomerAsync(42, true, null));
    }

    [Fact]
    public async Task ManagerSettings_DefaultsToDisabledMondayEight()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        NotificationPreferenceStore store = new(database.Factory);

        ManagerReportSettings settings = await store.GetManagerAsync();

        Assert.False(settings.WeeklySummaryEnabled);
        Assert.Null(settings.EmailAddress);
        Assert.Equal(DayOfWeek.Monday, settings.WeeklySummaryDay);
        Assert.Equal(8, settings.WeeklySummaryHourLocal);
    }

    [Fact]
    public async Task ManagerSettings_RejectsInvalidHour()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        NotificationPreferenceStore store = new(database.Factory);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.SetManagerAsync(true, "manager@example.com", DayOfWeek.Friday, 24));
    }

    [Fact]
    public async Task OutboundEmailQueue_SameDispatchKey_IsIdempotent()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        OutboundEmailQueue queue = new(database.Factory);

        await queue.EnqueueAsync("weekly:42:2026-09-04", "employee@example.com", "Weekly", "Body");
        await queue.EnqueueAsync("weekly:42:2026-09-04", "employee@example.com", "Weekly", "Body");

        await using ServiceDbContext context = await database.Factory.CreateDbContextAsync();
        Assert.Equal(1, await context.OutboundEmails.CountAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, IDbContextFactory<ServiceDbContext> factory)
        {
            this.connection = connection;
            this.Factory = factory;
        }

        public IDbContextFactory<ServiceDbContext> Factory { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            DbContextOptions<ServiceDbContext> options = new DbContextOptionsBuilder<ServiceDbContext>()
                .UseSqlite(connection)
                .Options;
            PooledDbContextFactory<ServiceDbContext> factory = new(options);
            await using ServiceDbContext context = await factory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, factory);
        }

        public async ValueTask DisposeAsync()
        {
            await this.connection.DisposeAsync();
        }
    }
}
