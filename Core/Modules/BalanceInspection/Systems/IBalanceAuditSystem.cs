namespace Hedron.Core.Modules.BalanceInspection.Systems
{
    /// <summary>
    /// Domain-tier (BalanceInspection) bulk band-drift audit: sweeps every authored item/mob
    /// template, classifies each via the core <c>IPowerBudgetSystem</c> oracle (through the shared
    /// item/mob power-projection seams), and reports drift plus a computed-cell bucket count. One
    /// callable method so the Blazor Integrity page, a possible future headless/admin command, and
    /// the prog-4 sim all consume the same sweep (INV-19). Never a build/reload/CI gate — advisory.
    /// </summary>
    public interface IBalanceAuditSystem
    {
        BalanceAuditReport Audit();
    }
}
