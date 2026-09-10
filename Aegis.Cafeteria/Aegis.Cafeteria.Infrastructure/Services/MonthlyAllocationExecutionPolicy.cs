namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class MonthlyAllocationExecutionPolicy(CafeteriaServicesOptions options) : IMonthlyAllocationExecutionPolicy
{
    public bool AllowLocalCalculation => !options.Enabled || !options.UseServiceMonthlyAllocations;
}
