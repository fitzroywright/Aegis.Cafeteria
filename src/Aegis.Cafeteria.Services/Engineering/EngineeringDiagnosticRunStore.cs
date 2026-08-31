using System.Text.Json;

namespace Aegis.Cafeteria.Services.Engineering;

public sealed class EngineeringDiagnosticRunStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public EngineeringDiagnosticRunStore(IHostEnvironment environment)
    {
        string directory = Path.Combine(environment.ContentRootPath, "diagnostics");
        Directory.CreateDirectory(directory);
        this.path = Path.Combine(directory, "diagnostic-runs.json");
    }

    public async Task<IReadOnlyList<EngineeringDiagnosticRun>> GetRecentAsync
    (
          int take
        , CancellationToken cancellationToken = default
    )
    {
        await this.gate.WaitAsync(cancellationToken);

        try
        {
            List<EngineeringDiagnosticRun> runs = await ReadAsync(cancellationToken);
            return runs
                .OrderByDescending(run => run.StartedAt)
                .Take(Math.Clamp(take, 1, 100))
                .ToArray();
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<EngineeringDiagnosticRun?> GetAsync
    (
          Guid runId
        , CancellationToken cancellationToken = default
    )
    {
        await this.gate.WaitAsync(cancellationToken);

        try
        {
            List<EngineeringDiagnosticRun> runs = await ReadAsync(cancellationToken);
            return runs.SingleOrDefault(run => run.RunId == runId);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<EngineeringDiagnosticRun?> ResolveAsync
    (
          Guid runId
        , string resolvedBy
        , string resolution
        , CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(resolvedBy))
        {
            throw new ArgumentException("ResolvedBy is required.", nameof(resolvedBy));
        }

        if (string.IsNullOrWhiteSpace(resolution))
        {
            throw new ArgumentException("Resolution is required.", nameof(resolution));
        }

        await this.gate.WaitAsync(cancellationToken);

        try
        {
            List<EngineeringDiagnosticRun> runs = await ReadAsync(cancellationToken);
            int index = runs.FindIndex(run => run.RunId == runId);

            if (index < 0)
            {
                return null;
            }

            EngineeringDiagnosticRun resolved = runs[index] with
            {
                  ResolvedAt = DateTimeOffset.UtcNow
                , ResolvedBy = resolvedBy.Trim()
                , Resolution = resolution.Trim()
            };

            runs[index] = resolved;
            string json = JsonSerializer.Serialize
            (
                  runs.OrderByDescending(run => run.StartedAt).Take(500)
                , this.serializerOptions
            );

            string temporaryPath = this.path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, this.path, true);
            return resolved;
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task SaveAsync
    (
          EngineeringDiagnosticRun run
        , CancellationToken cancellationToken = default
    )
    {
        await this.gate.WaitAsync(cancellationToken);

        try
        {
            List<EngineeringDiagnosticRun> runs = await ReadAsync(cancellationToken);
            runs.RemoveAll(existing => existing.RunId == run.RunId);
            runs.Add(run);

            string json = JsonSerializer.Serialize
            (
                  runs.OrderByDescending(existing => existing.StartedAt).Take(500)
                , this.serializerOptions
            );

            string temporaryPath = this.path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, this.path, true);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task<List<EngineeringDiagnosticRun>> ReadAsync
    (
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(this.path))
        {
            return [];
        }

        string json = await File.ReadAllTextAsync(this.path, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<EngineeringDiagnosticRun>>(json, this.serializerOptions) ?? [];
    }
}
