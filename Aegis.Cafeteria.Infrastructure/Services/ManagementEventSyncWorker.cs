namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Text.Json;

/// <summary>
/// Pulls immutable management/month-opening events from Aegis.Cafeteria.Services and commits them
/// to the POS local ledger. SignalR only wakes this durable pull path; polling remains the safety net.
/// </summary>
public sealed class ManagementEventSyncWorker : BackgroundService
{
    private readonly CafeteriaServicesClient client;
    private readonly CafeteriaServicesOptions options;
    private readonly IDbContextFactory<LocalCafeteriaDbContext> factory;
    private readonly ICafeteriaPersistenceStore persistence;
    private readonly ICustomerAccountQuery accountQuery;
    private readonly ICustomerAccountChangeNotifier accountChangeNotifier;
    private readonly ManagementEventWakeSignal wakeSignal;
    private readonly ILogger<ManagementEventSyncWorker> logger;

    public ManagementEventSyncWorker
    (
          CafeteriaServicesClient client
        , CafeteriaServicesOptions options
        , IDbContextFactory<LocalCafeteriaDbContext> factory
        , ICafeteriaPersistenceStore persistence
        , ICustomerAccountQuery accountQuery
        , ICustomerAccountChangeNotifier accountChangeNotifier
        , ManagementEventWakeSignal wakeSignal
        , ILogger<ManagementEventSyncWorker> logger
    )
    {
        this.client = client;
        this.options = options;
        this.factory = factory;
        this.persistence = persistence;
        this.accountQuery = accountQuery;
        this.accountChangeNotifier = accountChangeNotifier;
        this.wakeSignal = wakeSignal;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync
    (
        CancellationToken stoppingToken
    )
    {
        if (!this.options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PullAndApplyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                this.logger.LogError
                (
                      exception
                    , "Cafeteria Services event synchronization failed; POS will retry"
                );
            }

            try
            {
                await this.wakeSignal.WaitAsync
                (
                      TimeSpan.FromSeconds(this.options.PollSeconds)
                    , stoppingToken
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> PullAndApplyAsync
    (
        CancellationToken cancellationToken
    )
    {
        await using LocalCafeteriaDbContext checkpointDb = await this.factory.CreateDbContextAsync(cancellationToken);

        long lastSequence = await checkpointDb.ManagementEventInbox
            .Where(inbox => inbox.ProcessedAt != null)
            .Select(inbox => (long?)inbox.Sequence)
            .MaxAsync(cancellationToken)
            ?? 0L;

        IReadOnlyList<CafeteriaServiceEventEnvelope> serviceEvents = await this.client.GetEventsAsync
        (
              lastSequence
            , 100
            , cancellationToken
        );

        int processed = 0;

        foreach (CafeteriaServiceEventEnvelope serviceEvent in serviceEvents.OrderBy(serviceEvent => serviceEvent.Sequence))
        {
            if (await IsProcessedAsync(serviceEvent.OperationId, cancellationToken))
            {
                continue;
            }

            await SaveInboxAsync
            (
                  serviceEvent
                , cancellationToken
            );

            try
            {
                await ApplyAsync
                (
                      serviceEvent
                    , cancellationToken
                );

                await MarkProcessedAsync
                (
                      serviceEvent.OperationId
                    , null
                    , cancellationToken
                );

                processed++;

                if (serviceEvent.CustomerId > 0)
                {
                    await this.accountChangeNotifier.NotifyAsync(serviceEvent.CustomerId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await MarkProcessedAsync
                (
                      serviceEvent.OperationId
                    , exception.Message
                    , cancellationToken
                );

                throw;
            }
        }

        return processed;
    }

    private async Task ApplyAsync
    (
          CafeteriaServiceEventEnvelope envelope
        , CancellationToken cancellationToken
    )
    {
        switch (envelope.Kind)
        {
            case CafeteriaServiceEventKind.AdditionalCredit:
            {
                AdditionalCreditServiceEvent additionalCredit = JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(envelope.PayloadJson)
                    ?? throw new InvalidOperationException("Invalid Additional Credit event payload.");

                await UpdateAdditionalCreditLimitAsync(additionalCredit.CustomerId, additionalCredit.AdditionalCreditLimit, additionalCredit.CreditPolicyUpdatedAt, cancellationToken);

                AdditionalCreditApproval approval = new
                (
                      additionalCredit.OperationId
                    , additionalCredit.CustomerId
                    , additionalCredit.Amount
                    , additionalCredit.ApprovedBy
                    , additionalCredit.ApprovedAt
                    , additionalCredit.Reason
                );

                await this.persistence.SaveAdditionalCreditApprovalAsync(approval, cancellationToken);

                Aegis.Cafeteria.Application.Reporting.CustomerAccountView account = await this.accountQuery.GetCurrentAsync(additionalCredit.CustomerId, cancellationToken)
                    ?? throw new InvalidOperationException("Employee is not available in the local Cafeteria cache.");
                if (account.FinancialStatus.CreditShortfall > 0m)
                {
                    this.logger.LogWarning("Employee {CustomerId} has a credit shortfall of {Shortfall} after operation {OperationId}; further credit sales are blocked locally",
                        additionalCredit.CustomerId, account.FinancialStatus.CreditShortfall, envelope.OperationId);
                }

                break;
            }

            case CafeteriaServiceEventKind.Stipend:
            {
                StipendServiceEvent stipendEvent = JsonSerializer.Deserialize<StipendServiceEvent>(envelope.PayloadJson)
                    ?? throw new InvalidOperationException("Invalid Stipend event payload.");

                Stipend stipend = new(
                    stipendEvent.OperationId, stipendEvent.CustomerId, stipendEvent.Amount,
                    stipendEvent.ProcessedBy, stipendEvent.ProcessedAt, stipendEvent.Reason);

                await this.persistence.SaveStipendAsync(stipend, cancellationToken);

                Aegis.Cafeteria.Application.Reporting.CustomerAccountView account = await this.accountQuery.GetCurrentAsync(stipendEvent.CustomerId, cancellationToken)
                    ?? throw new InvalidOperationException("Employee is not available in the local Cafeteria cache.");
                if (account.FinancialStatus.CreditShortfall > 0m)
                {
                    this.logger.LogWarning("Employee {CustomerId} has a credit shortfall of {Shortfall} after operation {OperationId}; further credit sales are blocked locally",
                        stipendEvent.CustomerId, account.FinancialStatus.CreditShortfall, envelope.OperationId);
                }

                break;
            }

            case CafeteriaServiceEventKind.MonthlyAllocation:
            {
                MonthlyAllocationServiceEvent monthlyAllocation = JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(envelope.PayloadJson)
                    ?? throw new InvalidOperationException("Invalid Monthly Allocation event payload.");

                MonthlyCreditAllocation allocation = new
                (
                      monthlyAllocation.CustomerId
                    , monthlyAllocation.Year
                    , monthlyAllocation.Month
                    , monthlyAllocation.EligibleWorkingDays
                    , monthlyAllocation.IsEligibleForCredit
                    , monthlyAllocation.CreditLimitPrice
                    , monthlyAllocation.SubsidyCost
                    , monthlyAllocation.CalculatedAt
                );

                await this.persistence.SaveMonthlyCreditAllocationAsync(allocation, cancellationToken);

                _ = await this.accountQuery.GetAsync(monthlyAllocation.CustomerId, monthlyAllocation.Year, monthlyAllocation.Month, cancellationToken);
                break;
            }

            case CafeteriaServiceEventKind.MonthlyAllocationRecalculation:
            {
                MonthlyAllocationRecalculationServiceEvent recalculation = JsonSerializer.Deserialize<MonthlyAllocationRecalculationServiceEvent>(envelope.PayloadJson)
                    ?? throw new InvalidOperationException("Invalid Monthly Allocation Recalculation event payload.");

                MonthlyAllocationServiceEvent replacement = recalculation.ReplacementAllocation;
                MonthlyCreditAllocation allocation = new
                (
                      replacement.CustomerId
                    , replacement.Year
                    , replacement.Month
                    , replacement.EligibleWorkingDays
                    , replacement.IsEligibleForCredit
                    , replacement.CreditLimitPrice
                    , replacement.SubsidyCost
                    , replacement.CalculatedAt
                );

                await ReplaceMonthlyCreditAllocationAsync(allocation, cancellationToken);
                _ = await this.accountQuery.GetAsync(replacement.CustomerId, replacement.Year, replacement.Month, cancellationToken);
                break;
            }

            case CafeteriaServiceEventKind.AccountPeriodClose:
            {
                AccountPeriodCloseServiceEvent closeEvent = JsonSerializer.Deserialize<AccountPeriodCloseServiceEvent>(envelope.PayloadJson)
                    ?? throw new InvalidOperationException("Invalid Account Period Close event payload.");

                AccountPeriod period = new(closeEvent.Year, closeEvent.Month, closeEvent.WorkingDays);
                AccountPeriodClose close = new
                (
                      closeEvent.OperationId
                    , closeEvent.CustomerId
                    , period
                    , closeEvent.EmployeeAmountDue
                    , closeEvent.ClosedBy
                    , closeEvent.ClosedAt
                );

                await this.persistence.SaveAccountPeriodCloseAsync(close, cancellationToken);
                break;
            }

            case CafeteriaServiceEventKind.ProductCatalogImport:
            {
                ProductCatalogImportServiceEvent productImport = JsonSerializer.Deserialize<ProductCatalogImportServiceEvent>(envelope.PayloadJson)
                    ?? throw new InvalidOperationException("Invalid Product Catalog Import event payload.");

                await ApplyProductImportAsync(productImport, cancellationToken);
                break;
            }



            default:
            {
                throw new InvalidOperationException($"Unsupported Cafeteria Services event kind {envelope.Kind}.");
            }
        }
    }

    private async Task ApplyProductImportAsync
    (
          ProductCatalogImportServiceEvent import
        , CancellationToken cancellationToken
    )
    {
        await using LocalCafeteriaDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        string[] existingNames = await database.Products
            .AsNoTracking()
            .Select(product => product.Name)
            .ToArrayAsync(cancellationToken);

        if (existingNames.Any(name => string.Equals(name, import.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        int nextId = (await database.Products.Select(product => (int?)product.Id).MaxAsync(cancellationToken) ?? 0) + 1;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        database.Products.Add
        (
            new ProductRow
            {
                  Id = nextId
                , Name = import.Name.Trim()
                , Price = import.Price
                , Revision = now.UtcDateTime.Ticks
                , UpdatedAt = now
            }
        );

        await database.SaveChangesAsync(cancellationToken);
    }


    private async Task ReplaceMonthlyCreditAllocationAsync
    (
          MonthlyCreditAllocation allocation
        , CancellationToken cancellationToken
    )
    {
        await using LocalCafeteriaDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        MonthlyCreditAllocationRow? row = await database.MonthlyCreditAllocations.SingleOrDefaultAsync
        (
            candidate =>
                   candidate.CustomerId == allocation.CustomerId
                && candidate.Year == allocation.Year
                && candidate.Month == allocation.Month
            , cancellationToken
        );

        MonthlyCreditAllocationRow replacement = DomainPersistenceMapper.ToRow
        (
              allocation
            , DateTimeOffset.UtcNow.Ticks
            , DateTimeOffset.UtcNow
        );

        if (row is null)
        {
            database.MonthlyCreditAllocations.Add(replacement);
        }
        else
        {
            database.Entry(row).CurrentValues.SetValues(replacement);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateAdditionalCreditLimitAsync
    (
          int customerId
        , decimal limit
        , DateTimeOffset? policyUpdatedAt
        , CancellationToken cancellationToken
    )
    {
        await using LocalCafeteriaDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        CustomerRow customer = await database.Customers.SingleOrDefaultAsync
        (
              customer => customer.Id == customerId
            , cancellationToken
        )
            ?? throw new InvalidOperationException("Employee is not available in the local Cafeteria cache.");

        if (!CreditPolicyVersion.ShouldApply(policyUpdatedAt, customer.AdditionalCreditPolicyUpdatedAt))
        {
            return;
        }

        customer.AdditionalCreditLimit = limit;
        customer.AdditionalCreditPolicyUpdatedAt = policyUpdatedAt;
        customer.Revision = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> IsProcessedAsync
    (
          Guid operationId
        , CancellationToken cancellationToken
    )
    {
        await using LocalCafeteriaDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        return await database.ManagementEventInbox.AnyAsync
        (
              inbox => inbox.OperationId == operationId && inbox.ProcessedAt != null
            , cancellationToken
        );
    }

    private async Task SaveInboxAsync
    (
          CafeteriaServiceEventEnvelope serviceEvent
        , CancellationToken cancellationToken
    )
    {
        await using LocalCafeteriaDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        ManagementEventInboxRow? row = await database.ManagementEventInbox.SingleOrDefaultAsync
        (
              inbox => inbox.OperationId == serviceEvent.OperationId
            , cancellationToken
        );

        if (row is not null)
        {
            return;
        }

        database.ManagementEventInbox.Add
        (
            new ManagementEventInboxRow
            {
                  OperationId = serviceEvent.OperationId
                , Sequence = serviceEvent.Sequence
                , Kind = (int)serviceEvent.Kind
                , CustomerId = serviceEvent.CustomerId
                , OccurredAt = serviceEvent.OccurredAt
                , PayloadJson = serviceEvent.PayloadJson
                , ReceivedAt = DateTimeOffset.UtcNow
            }
        );

        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkProcessedAsync
    (
          Guid operationId
        , string? error
        , CancellationToken cancellationToken
    )
    {
        await using LocalCafeteriaDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        ManagementEventInboxRow row = await database.ManagementEventInbox.SingleAsync
        (
              inbox => inbox.OperationId == operationId
            , cancellationToken
        );

        row.LastError = error;
        row.ProcessedAt = error is null
            ? DateTimeOffset.UtcNow
            : null;

        await database.SaveChangesAsync(cancellationToken);
    }
}
