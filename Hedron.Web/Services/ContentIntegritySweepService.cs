using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Systems;

namespace Hedron.Web.Services
{
    /// <summary>Lifecycle state of the integrity sweep.</summary>
    public enum IntegritySweepState
    {
        Idle,
        Running,
        Completed,
        Failed,
    }

    /// <summary>
    /// A read-only snapshot of the sweep for a polling page to render — the same shape
    /// <see cref="SimulationRunService.Snapshot"/> returns.
    /// </summary>
    public sealed record IntegritySweepStatus(
        IntegritySweepState State,
        IReadOnlyList<BrokenReference> Broken,
        BalanceAuditReport? AuditReport,
        string? ErrorMessage);

    /// <summary>
    /// Runs the Integrity page's two corpus sweeps — <see cref="IContentReferenceIndex.SweepBroken"/>
    /// and <see cref="IBalanceAuditSystem.Audit"/> — off the Blazor circuit thread, exposing an
    /// observable in-progress state instead of freezing the page for their duration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Deliberately not a generalization of <see cref="SimulationRunService"/>.</strong>
    /// <c>docs/architecture/08-blazor.md</c> records that a <em>second</em> long-running editor job
    /// wanting queue / progress / cancellation should generalize that service rather than hand-roll
    /// a second one. This sweep is <strong>progress-only — no queue, no cancellation</strong>, so it
    /// does not meet that shape and does not fire the trigger. The recorded candidate remains the
    /// bulk conformance apply (<c>PreviewAllFlagged</c>/<c>ApplyAllFlagged</c>), which still runs
    /// blocking on the circuit thread in this very page and is out of scope here.
    /// </para>
    /// <para>
    /// <strong>Concurrency posture (INV-31).</strong> The sweep runs on a background
    /// <see cref="Task"/>; the status record is owned by this service behind <c>_lock</c> and read
    /// by circuit threads via <see cref="Snapshot"/>. A concurrent <see cref="StartAsync"/> joins
    /// the in-flight sweep rather than starting a second one. It reads
    /// <see cref="IContentReferenceIndex"/> / <see cref="IBalanceAuditSystem"/> and mutates no
    /// live-world component (INV-12/22/23).
    /// </para>
    /// </remarks>
    public sealed class ContentIntegritySweepService
    {
        private static readonly IReadOnlyList<BrokenReference> NoBroken = Array.Empty<BrokenReference>();

        private readonly IContentReferenceIndex _referenceIndex;
        private readonly IBalanceAuditSystem _balanceAudit;

        private readonly object _lock = new();
        private IntegritySweepStatus _status = new(IntegritySweepState.Idle, NoBroken, null, null);
        private Task? _inFlight;

        public ContentIntegritySweepService(
            IContentReferenceIndex referenceIndex,
            IBalanceAuditSystem balanceAudit)
        {
            _referenceIndex = referenceIndex;
            _balanceAudit = balanceAudit;
        }

        /// <summary>The current sweep status. Never blocks on the sweep.</summary>
        public IntegritySweepStatus Snapshot()
        {
            lock (_lock)
            {
                return _status;
            }
        }

        /// <summary>
        /// Starts a sweep on a background thread and returns immediately with a task that completes
        /// when that sweep does. A call made while a sweep is in flight joins it — the page can
        /// await the returned task to know when to re-render.
        /// </summary>
        public Task StartAsync()
        {
            lock (_lock)
            {
                if (_inFlight is { } running)
                    return running;

                _status = new IntegritySweepStatus(IntegritySweepState.Running, NoBroken, null, null);
                _inFlight = Task.Run(Sweep);
                return _inFlight;
            }
        }

        private void Sweep()
        {
            IntegritySweepStatus result;
            try
            {
                var broken = _referenceIndex.SweepBroken();
                var audit = _balanceAudit.Audit();
                result = new IntegritySweepStatus(IntegritySweepState.Completed, broken, audit, null);
            }
            catch (Exception ex)
            {
                result = new IntegritySweepStatus(IntegritySweepState.Failed, NoBroken, null, ex.Message);
            }

            lock (_lock)
            {
                _status = result;
                _inFlight = null;
            }
        }
    }
}
