namespace Aegis.Cafeteria.Plugin.Integration;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aegis.Cafeteria.Application.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public sealed class FfpManagerIntegrationProvider :
      ICafeteriaPolicyProvider
    , IEmployeeFactSource
    , IEmployeeIdScanPublisher
    , IWorkforceReferenceSource
{
    private readonly IConfiguration configuration;
    private readonly ICafeteriaPolicyCache policyCache;
    private readonly ILogger<FfpManagerIntegrationProvider> logger;

    public FfpManagerIntegrationProvider(
        IConfiguration configuration,
        ICafeteriaPolicyCache policyCache,
        ILogger<FfpManagerIntegrationProvider> logger)
    {
        this.configuration = configuration;
        this.policyCache = policyCache;
        this.logger = logger;
    }

    public async Task<CafeteriaPolicySnapshot> GetPolicyAsync(
        int year,
        int month,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(year, month);

        try
        {
            CafeteriaPolicySnapshot policy = await ReadPolicyAsync(year, month, cancellationToken);
            policy.Validate();
            await policyCache.SaveAsync(policy, cancellationToken);
            return policy;
        }
        catch when (!forceReload && !cancellationToken.IsCancellationRequested)
        {
            CafeteriaPolicySnapshot? cached = await policyCache.GetAsync(year, month, cancellationToken);
            if (cached is null)
            {
                throw;
            }

            logger.LogWarning(
                "FFP Manager policy source is unavailable; using last validated policy {Version} for {Year}-{Month:D2}",
                cached.Version,
                year,
                month);

            return cached;
        }
    }

    public async Task<EmployeeFactChangeSet> GetChangesAsync(
        string? afterVersion,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EmployeeFact> employees = await ReadEmployeeFactsAsync(cancellationToken);
        string version = BuildVersion(
            employees.Select(employee =>
                $"{employee.EmployeeId}|{employee.Name}|{employee.CardIdentifier}|{employee.IsActive}|{employee.IsEligible}|{employee.EligibleFrom:yyyy-MM-dd}|{employee.Version}"));

        if (string.Equals(afterVersion, version, StringComparison.Ordinal))
        {
            return new EmployeeFactChangeSet([], version);
        }

        return new EmployeeFactChangeSet(employees, version);
    }

    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        string connectionString = ResolveConnectionString();
        List<ExternalCustomerSnapshot> result = [];

        const string sql = """
            SELECT
                  ID
                , ISNULL(CONVERT(nvarchar(256), Name), '')
                , NFCID
                , RFID
                , Code
                , TRN
                , ISNULL(IsActive, 0)
                , ISNULL(EligibleForLunchTicket, 0)
                , DateEmployed
                , Photo
                , ISNULL(CONVERT(nvarchar(256), LoginName), '')
            FROM dbo.Employees
            WHERE ID IS NOT NULL
            ORDER BY ID;
            """;

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            int employeeId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            string? nfc = Normalize(reader, 2);
            string? rfid = Normalize(reader, 3);
            string? code = Normalize(reader, 4);

            result.Add(new ExternalCustomerSnapshot
            {
                CustomerId = employeeId,
                Name = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
                Nfc = nfc ?? rfid,
                Code = code,
                Trn = Normalize(reader, 5),
                IsActive = ToBoolean(reader.GetValue(6)),
                IsEligibleForCafeteriaCredit = ToBoolean(reader.GetValue(7)) && ToBoolean(reader.GetValue(6)),
                EmploymentStartDate = reader.IsDBNull(8)
                    ? null
                    : DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(8), CultureInfo.InvariantCulture)),
                Photo = reader.IsDBNull(9) ? null : (byte[])reader.GetValue(9),
                LoginName = Convert.ToString(reader.GetValue(10), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
            });
        }

        return result;
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(year, month);
        return await ReadHolidaysAsync(year, month, cancellationToken);
    }

    public async Task PublishAsync(
        CafeteriaOutboundTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        string connectionString = ResolveConnectionString();
        int scanType = ResolveScanType(transaction.TransactionKind);
        string idName = ResolveIdName(transaction.TransactionKind);

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction sqlTransaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string sql = """
                SET XACT_ABORT ON;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.EmployeeIDScans WITH (UPDLOCK, HOLDLOCK)
                    WHERE TransmissionID = @TransmissionID
                )
                BEGIN
                    INSERT INTO dbo.EmployeeIDScans
                    (
                          Code
                        , Name
                        , [Description]
                        , UserName
                        , DateCreated
                        , DateUpdated
                        , ScanCode
                        , ScanDate
                        , ScanType
                        , ScanValue
                        , IDName
                        , EmployeeID
                        , TransmissionID
                        , CreatedBy
                    )
                    SELECT
                          CASE
                              WHEN @UseIdentificationCode = 1 THEN @IdentificationCode
                              ELSE CONVERT(nvarchar(255), Code)
                          END
                        , CONVERT(nvarchar(255), Name)
                        , @Description
                        , @UserName
                        , @OccurredAt
                        , @OccurredAt
                        , @ScanCode
                        , @OccurredAt
                        , @ScanType
                        , @ScanValue
                        , @IDName
                        , ID
                        , @TransmissionID
                        , @CreatedBy
                    FROM dbo.Employees
                    WHERE ID = @EmployeeID;

                    IF @@ROWCOUNT = 0
                        THROW 51000, 'FFP Manager employee not found for Cafeteria transaction publication.', 1;
                END;
                """;

            await using SqlCommand command = new(sql, connection, sqlTransaction);
            command.Parameters.AddWithValue("@TransmissionID", transaction.TransactionId.ToString("D"));
            command.Parameters.AddWithValue("@EmployeeID", transaction.EmployeeId);
            command.Parameters.AddWithValue("@Description", transaction.Description);
            command.Parameters.AddWithValue("@UserName", NormalizeActor(transaction.UserName));
            command.Parameters.AddWithValue("@OccurredAt", transaction.OccurredAt.LocalDateTime);
            command.Parameters.AddWithValue("@ScanCode", transaction.IdentificationMethod);
            command.Parameters.AddWithValue("@ScanType", scanType);
            command.Parameters.AddWithValue("@ScanValue", transaction.Amount);
            command.Parameters.AddWithValue("@IDName", idName);
            command.Parameters.AddWithValue("@CreatedBy", "Aegis.Cafeteria.Services");

            bool useIdentificationCode =
                string.Equals(transaction.TransactionKind, "Purchase", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(transaction.IdentificationCode);

            command.Parameters.AddWithValue("@UseIdentificationCode", useIdentificationCode ? 1 : 0);
            command.Parameters.AddWithValue(
                "@IdentificationCode",
                (object?)transaction.IdentificationCode?.Trim() ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await sqlTransaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Published Cafeteria transaction {TransactionId} ({Kind}) for employee {EmployeeId} to FFP Manager",
                transaction.TransactionId,
                transaction.TransactionKind,
                transaction.EmployeeId);
        }
        catch
        {
            try
            {
                await sqlTransaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(
                    rollbackException,
                    "Rollback failed for Cafeteria transaction {TransactionId}",
                    transaction.TransactionId);
            }

            throw;
        }
    }

    private async Task<CafeteriaPolicySnapshot> ReadPolicyAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        string connectionString = ResolveConnectionString();
        decimal price;
        decimal cost;

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (SqlCommand policyCommand = new(
            """
            SELECT TOP 1 ISNULL(Price, 0), ISNULL(Cost, 0)
            FROM dbo.LunchItems
            WHERE UPPER(CONVERT(nvarchar(64), Code)) = 'FFP00';
            """,
            connection))
        {
            await using SqlDataReader reader = await policyCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "FFP Manager does not contain the required FFP00 LunchItems policy row.");
            }

            price = Convert.ToDecimal(reader.GetValue(0), CultureInfo.InvariantCulture);
            cost = Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture);
        }

        IReadOnlyCollection<DateOnly> holidays =
            await ReadHolidaysAsync(connection, year, month, cancellationToken);

        HashSet<DateOnly> excluded = holidays.ToHashSet();
        List<DateOnly> workingDays = [];
        DateOnly date = new(year, month, 1);
        DateOnly end = new(year, month, DateTime.DaysInMonth(year, month));

        while (date <= end)
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                && !excluded.Contains(date))
            {
                workingDays.Add(date);
            }

            date = date.AddDays(1);
        }

        string version = BuildVersion(
        [
            year.ToString(CultureInfo.InvariantCulture),
            month.ToString(CultureInfo.InvariantCulture),
            price.ToString(CultureInfo.InvariantCulture),
            cost.ToString(CultureInfo.InvariantCulture),
            .. holidays.OrderBy(value => value).Select(value => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        ]);

        return new CafeteriaPolicySnapshot(
            year,
            month,
            price,
            cost,
            workingDays,
            version,
            new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private async Task<IReadOnlyList<EmployeeFact>> ReadEmployeeFactsAsync(
        CancellationToken cancellationToken)
    {
        string connectionString = ResolveConnectionString();
        List<EmployeeFact> result = [];

        const string sql = """
            SELECT
                  ID
                , ISNULL(CONVERT(nvarchar(256), Name), '')
                , NFCID
                , RFID
                , Code
                , ISNULL(IsActive, 0)
                , ISNULL(EligibleForLunchTicket, 0)
                , DateEmployed
            FROM dbo.Employees
            WHERE ID IS NOT NULL
            ORDER BY ID;
            """;

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            int employeeId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            string name = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            string? cardIdentifier = Normalize(reader, 2) ?? Normalize(reader, 3) ?? Normalize(reader, 4);
            bool isActive = ToBoolean(reader.GetValue(5));
            bool isEligible = ToBoolean(reader.GetValue(6)) && isActive;
            DateOnly? eligibleFrom = reader.IsDBNull(7)
                ? null
                : DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(7), CultureInfo.InvariantCulture));

            string employeeVersion = BuildVersion(
            [
                employeeId.ToString(CultureInfo.InvariantCulture),
                name,
                cardIdentifier ?? string.Empty,
                isActive.ToString(CultureInfo.InvariantCulture),
                isEligible.ToString(CultureInfo.InvariantCulture),
                eligibleFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty
            ]);

            result.Add(new EmployeeFact(
                employeeId,
                name,
                cardIdentifier,
                isActive,
                isEligible,
                eligibleFrom,
                employeeVersion));
        }

        return result;
    }

    private async Task<IReadOnlyCollection<DateOnly>> ReadHolidaysAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        string connectionString = ResolveConnectionString();
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadHolidaysAsync(connection, year, month, cancellationToken);
    }

    private static async Task<IReadOnlyCollection<DateOnly>> ReadHolidaysAsync(
        SqlConnection connection,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        DateTime start = new(year, month, 1);
        DateTime end = start.AddMonths(1);

        await using SqlCommand command = new(
            """
            SELECT CalendarDate
            FROM dbo.Holidays
            WHERE CalendarDate >= @Start
              AND CalendarDate < @End
              AND CalendarDate IS NOT NULL
            ORDER BY CalendarDate;
            """,
            connection);

        command.Parameters.AddWithValue("@Start", start);
        command.Parameters.AddWithValue("@End", end);

        List<DateOnly> result = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(DateOnly.FromDateTime(Convert.ToDateTime(
                reader.GetValue(0),
                CultureInfo.InvariantCulture)));
        }

        return result.Distinct().ToArray();
    }

    private string ResolveConnectionString()
    {
        string? value =
            configuration["IntegrationPlugin:ConnectionString"]?.Trim();

        value ??= configuration.GetConnectionString("FfpManager")?.Trim();
        value ??= Environment.GetEnvironmentVariable("FFPMANAGER_CONNECTION_STRING")?.Trim();
        value ??= Environment.GetEnvironmentVariable("FFPMANAGER_TEST")?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Cafeteria Services is the integration plane, but no FFP Manager connection string is configured. Set IntegrationPlugin:ConnectionString, ConnectionStrings:FfpManager, FFPMANAGER_CONNECTION_STRING, or FFPMANAGER_TEST.");
        }

        return value;
    }

    private static int ResolveScanType(string transactionKind)
        => transactionKind.Trim() switch
        {
            "MonthlyAllocation" => 3,
            "MonthlyAllocationRecalculation" => 3,
            "StipendCardReduction" => 4,
            "Purchase" => 8,
            "Refund" => 9,
            "AdditionalCredit" => 9,
            _ => 9
        };

    private static string ResolveIdName(string transactionKind)
        => transactionKind.Trim() switch
        {
            "MonthlyAllocation" => "Tickets Assigned",
            "MonthlyAllocationRecalculation" => "Monthly Allocation Recalculation",
            "StipendCardReduction" => "Stipend Card Reduction",
            "Purchase" => "Cafeteria Purchase",
            "Refund" => "Cafeteria Refund",
            "AdditionalCredit" => "Additional Credit",
            _ => "Cafeteria Adjustment"
        };

    private static string NormalizeActor(string? value)
        => string.IsNullOrWhiteSpace(value) ? "SYSTEM" : value.Trim();

    private static string? Normalize(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        string? value = Convert.ToString(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture)?.Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool ToBoolean(object value)
        => value is bool boolean
            ? boolean
            : Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;

    private static void ValidatePeriod(int year, int month)
    {
        if (year < 2000 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }
    }

    private static string BuildVersion(IEnumerable<string> values)
    {
        string canonical = string.Join("\n", values);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
