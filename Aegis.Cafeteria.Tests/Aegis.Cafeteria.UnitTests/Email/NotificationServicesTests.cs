namespace Aegis.Cafeteria.UnitTests.Email;

using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Common.Messaging;

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

    [Fact]
    public async Task QueuedReport_ReachesCommonMessagingWithAttachmentAndStableIdentity()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        OutboundEmailQueue queue = new(database.Factory);
        await queue.EnqueueAsync("report:1", "manager@example.org", "Daily Sales", "Line one\nLine two",
            [new EmailAttachment("report.pdf", "application/pdf", [1, 2, 3])]);
        RecordingEmailChannel channel = new();
        OutboundEmailWorker worker = CreateWorker(database, channel);
        await worker.DeliverPendingAsync();
        await worker.DeliverPendingAsync();

        await using ServiceDbContext context = await database.Factory.CreateDbContextAsync();
        OutboundEmailRow row = await context.OutboundEmails.SingleAsync();
        Assert.NotNull(row.SentAt);
        Assert.Equal("graph-accepted:" + row.Id.ToString("N"), row.MessageId);
        Assert.Equal(row.Id, channel.NotificationId);
        Assert.Equal(1, channel.Attempts);
        Assert.Equal("manager@example.org", channel.Recipient!.Email);
        Assert.False(channel.Request!.BodyIsHtml);
        Assert.Equal("Line one\nLine two", channel.Request.Body);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(channel.Request.Attachments).Content);
    }

    [Fact]
    public async Task ProviderFailure_RemainsQueuedAndRetriesWithOriginalIdentity()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await new OutboundEmailQueue(database.Factory).EnqueueAsync("report:2", "manager@example.org", "Report", "Body");
        RecordingEmailChannel channel = new() { Fail = true };
        await CreateWorker(database, channel).DeliverPendingAsync();
        Guid originalId;
        await using (ServiceDbContext context = await database.Factory.CreateDbContextAsync())
        {
            OutboundEmailRow row = await context.OutboundEmails.SingleAsync();
            originalId = row.Id;
            Assert.Null(row.SentAt);
            Assert.NotNull(row.LastError);
            Assert.Equal(1, row.AttemptCount);
            Assert.True(row.NextAttemptAt > row.LastAttemptAt);
            row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        channel.Fail = false;
        await CreateWorker(database, channel).DeliverPendingAsync();
        await using ServiceDbContext verified = await database.Factory.CreateDbContextAsync();
        OutboundEmailRow delivered = await verified.OutboundEmails.SingleAsync();
        Assert.NotNull(delivered.SentAt);
        Assert.Null(delivered.LastError);
        Assert.Equal(originalId, channel.NotificationId);
        Assert.Equal(2, channel.Attempts);
    }

    [Fact]
    public async Task DisabledDelivery_PreservesPendingMailWithoutAttemptingSend()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await new OutboundEmailQueue(database.Factory).EnqueueAsync("report:3", "manager@example.org", "Report", "Body");
        RecordingEmailChannel channel = new();
        EmailDeliveryOptions options = new();
        OutboundEmailWorker worker = new(database.Factory, new CommonMessagingEmailSender(channel, options), options,
            NullLogger<OutboundEmailWorker>.Instance);
        await worker.DeliverPendingAsync();
        await using ServiceDbContext context = await database.Factory.CreateDbContextAsync();
        OutboundEmailRow row = await context.OutboundEmails.SingleAsync();
        Assert.Null(row.SentAt);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(0, channel.Attempts);
    }

    private static OutboundEmailWorker CreateWorker(TestDatabase database, RecordingEmailChannel channel)
    {
        EmailDeliveryOptions options = new()
        {
              Enabled = true
            , TenantId = "tenant"
            , ClientId = "client"
            , SenderUpn = "sender@example.org"
        };
        return new OutboundEmailWorker(database.Factory, new CommonMessagingEmailSender(channel, options), options,
            NullLogger<OutboundEmailWorker>.Instance);
    }

    private sealed class RecordingEmailChannel : IExternalMessageChannel
    {
        public MessageChannel Channel => MessageChannel.MsEmail;
        public bool Fail { get; set; }
        public int Attempts { get; private set; }
        public Guid NotificationId { get; private set; }
        public MessageRequest? Request { get; private set; }
        public MessageRecipient? Recipient { get; private set; }

        public Task SendAsync(MessageRecipient recipient, MessageRequest request, Guid notificationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts++;
            NotificationId = notificationId;
            Request = request;
            Recipient = recipient;
            if (Fail)
            {
                throw new HttpRequestException("Graph unavailable");
            }
            return Task.CompletedTask;
        }
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
            TestDbContextFactory factory = new(options);
            await using ServiceDbContext context = await factory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, factory);
        }

        public async ValueTask DisposeAsync()
        {
            await this.connection.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ServiceDbContext> options) : IDbContextFactory<ServiceDbContext>
    {
        public ServiceDbContext CreateDbContext()
        {
            return new ServiceDbContext(options);
        }

        public Task<ServiceDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ServiceDbContext(options));
        }
    }
}
