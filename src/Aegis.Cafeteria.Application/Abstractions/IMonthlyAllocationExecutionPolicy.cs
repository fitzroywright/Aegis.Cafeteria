namespace Aegis.Cafeteria.Application.Abstractions;

public interface IMonthlyAllocationExecutionPolicy
{
    bool AllowLocalCalculation { get; }
}
