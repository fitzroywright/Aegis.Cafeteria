namespace Aegis.Cafeteria.UnitTests.Email;

using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Identity;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Services;
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

    [Theory]
    [InlineData("")]
    [InlineData("new-work@example.com")]
    public async Task SourceChange_RequiresReviewWithoutChangingDeliveryAddress(string replacement)
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        EmployeeMetadataOptions options = new() { Mode = "None" };
        EmployeeMetadataStore metadata = new(database.Factory, options, []);
        NotificationPreferenceStore preferences = new(database.Factory, metadata);
        await metadata.ApplyAsync(new Dictionary<int, EmployeeMetadataPatch>
        {
            [42] = new() { EmailAddress = new("work@example.com", "workforce") }
        });
        await preferences.SetCustomerAsync(42, true, "work@example.com", suggestedEmailSource: "workforce", suggestedEmailAddress: "work@example.com");
        await metadata.ApplyAsync(new Dictionary<int, EmployeeMetadataPatch>
        {
            [42] = new() { EmailAddress = new(replacement, "workforce") }
        });

        // Both the suggestion and acknowledgement survive a Services restart.
        EmployeeMetadataStore restartedMetadata = new(database.Factory, options, []);
        NotificationPreferenceStore restarted = new(database.Factory, restartedMetadata);
        CustomerNotificationPreference review = await restarted.GetCustomerAsync(42);
        Assert.True(review.NeedsEmailReview);
        Assert.Equal("work@example.com", review.EmailAddress);
        Assert.Equal(replacement, review.SuggestedEmailAddress);

        await restarted.SetCustomerAsync(42, true, review.EmailAddress, suggestedEmailSource: review.SuggestedEmailSource, suggestedEmailAddress: review.SuggestedEmailAddress);
        Assert.False((await restarted.GetCustomerAsync(42)).NeedsEmailReview);
        Assert.Equal("work@example.com", (await restarted.GetCustomerAsync(42)).EmailAddress);
    }

    [Fact]
    public async Task SavingUnchangedAddressWithoutObservation_PreservesSourceAssociation()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        NotificationPreferenceStore preferences = new(database.Factory);
        await preferences.SetCustomerAsync(42, true, "work@example.com", suggestedEmailSource: "directory", suggestedEmailAddress: "work@example.com");
        CustomerNotificationPreference saved = await preferences.SetCustomerAsync(42, true, "work@example.com");
        Assert.Equal("directory", saved.NotificationEmailSource);
        CustomerNotificationPreference personal = await preferences.SetCustomerAsync(42, true, "personal@example.com");
        Assert.Null(personal.NotificationEmailSource);
    }

    [Fact]
    public async Task MissingAnswer_PreservesSuggestion_AndPersonalChoiceNeverNeedsReview()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        EmployeeMetadataStore metadata = new(database.Factory, new() { Mode = "None" }, []);
        NotificationPreferenceStore preferences = new(database.Factory, metadata);
        await metadata.ApplyAsync(new Dictionary<int, EmployeeMetadataPatch>
        {
            [42] = new() { EmailAddress = new("work@example.com", "workforce") }
        });
        await preferences.SetCustomerAsync(42, true, "personal@example.com");
        await metadata.ApplyAsync(new Dictionary<int, EmployeeMetadataPatch> { [42] = new() });
        Assert.Equal("work@example.com", (await preferences.GetCustomerAsync(42)).SuggestedEmailAddress);
        await metadata.ApplyAsync(new Dictionary<int, EmployeeMetadataPatch>
        {
            [42] = new() { EmailAddress = new("", "workforce") }
        });
        CustomerNotificationPreference loaded = await preferences.GetCustomerAsync(42);
        Assert.Equal("personal@example.com", loaded.EmailAddress);
        Assert.False(loaded.NeedsEmailReview);
    }

    [Fact]
    public async Task AcknowledgingOldSuggestion_DoesNotHideConcurrentSourceChange()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        EmployeeMetadataStore metadata = new(database.Factory, new() { Mode = "None" }, []);
        NotificationPreferenceStore preferences = new(database.Factory, metadata);
        await metadata.ApplyAsync(new Dictionary<int, EmployeeMetadataPatch>
        {
            [42] = new() { EmailAddress = new("new@example.com", "workforce") }
        });
        CustomerNotificationPreference saved = await preferences.SetCustomerAsync(42, true, "old@example.com",
            suggestedEmailSource: "workforce", suggestedEmailAddress: "old@example.com");
        Assert.True(saved.NeedsEmailReview);
        await preferences.SetCustomerAsync(42, false, null);
        Assert.False((await preferences.GetCustomerAsync(42)).NeedsEmailReview);
    }

    [Fact]
    public async Task MetadataOwnership_IgnoresOtherSources_AndAllowsIndependentFields()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        EmployeeMetadataOptions options = new() { Mode = "Plugin", Fields = ["EmailAddress"] };
        EmployeeMetadataStore metadata = new(database.Factory, options, [new TestMetadataProvider()]);
        await metadata.ApplyAsync(new Dictionary<int, EmployeeMetadataPatch>
        {
            [42] = new() { EmailAddress = new("directory@example.com", "directory"), Department = new("ignored", "directory") }
        });
        ExternalCustomerSnapshot[] enriched = await metadata.EnrichAsync
        (
            [new() { CustomerId = 42, EmailAddress = "ignored@example.com", Department = "Finance" }], default
        );
        Assert.Equal("directory@example.com", enriched[0].EmailAddress);
        Assert.Equal("Finance", enriched[0].Department);
        Assert.Equal("workforce", enriched[0].Metadata!.Department!.Source);
    }

    [Theory]
    [InlineData("work@example.com", false, "directory")]
    [InlineData("personal@example.com", false, null)]
    [InlineData("old@example.com", true, "directory")]
    [InlineData("personal@example.com", true, null)]
    public void ConfirmingChoice_TracksOnlyAcceptedSuggestionOrKeptReview(string chosen, bool review, string? expected)
    {
        CustomerNotificationPreferenceSnapshot preference = new(42, review, review, "old@example.com", review, "work@example.com", "directory");
        Assert.Equal(expected, NotificationEmailChoice.AcknowledgedSource(preference, true, chosen));
        Assert.Null(NotificationEmailChoice.AcknowledgedSource(preference, false, chosen));
    }

    private sealed class TestMetadataProvider : IEmployeeMetadataProvider
    {
        public string SourceId => "directory";

        public Task<IReadOnlyDictionary<int, EmployeeMetadataPatch>> GetAsync(IReadOnlyList<ExternalCustomerSnapshot> employees, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<int, EmployeeMetadataPatch>>(new Dictionary<int, EmployeeMetadataPatch>());
        }
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

