using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Systems;
using Hedron.Web.Services;
using Xunit;

namespace Hedron.Tests.Web
{
    /// <summary>
    /// Tier 1 — <see cref="ContentIntegritySweepService"/> against faked sweep seams. Asserts the
    /// observable in-progress state the Integrity page renders instead of blocking its circuit.
    /// <para>
    /// Deterministic by construction: the fake reference index blocks on a gate the test controls,
    /// so "in progress" is asserted while the sweep is provably mid-flight rather than after a sleep.
    /// </para>
    /// </summary>
    public sealed class ContentIntegritySweepServiceTests
    {
        private sealed class GatedReferenceIndex : IContentReferenceIndex
        {
            public readonly ManualResetEventSlim Gate = new(initialState: false);
            public readonly ManualResetEventSlim Entered = new(initialState: false);
            public int SweepCount;
            public Exception? Throw;

            public IReadOnlyList<BrokenReference> SweepBroken()
            {
                Interlocked.Increment(ref SweepCount);
                Entered.Set();
                Gate.Wait();
                if (Throw is { } ex)
                    throw ex;
                return new[] { new BrokenReference(ContentKind.Room, "room.one", "AreaId", "area.missing") };
            }

            public bool Resolves(ContentKind targetKind, string targetBlueprintId) => throw new NotSupportedException();
            public IReadOnlyList<ReferrerEdit> Referrers(ContentKind targetKind, string targetBlueprintId) => throw new NotSupportedException();
            public IReadOnlyList<BrokenReference> BrokenFor(IEntityTemplate definition) => throw new NotSupportedException();
        }

        private sealed class FakeBalanceAudit : IBalanceAuditSystem
        {
            public BalanceAuditReport Audit() =>
                new(Array.Empty<BalanceAuditEntry>(), new Dictionary<(int Tier, int Band), int>());
        }

        private static void WaitFor(ManualResetEventSlim signal)
        {
            Assert.True(signal.Wait(TimeSpan.FromSeconds(5)), "sweep did not start within the timeout.");
        }

        [Fact]
        public void Snapshot_BeforeAnyStart_IsIdle()
        {
            var service = new ContentIntegritySweepService(new GatedReferenceIndex(), new FakeBalanceAudit());

            var status = service.Snapshot();

            Assert.Equal(IntegritySweepState.Idle, status.State);
            Assert.Empty(status.Broken);
            Assert.Null(status.AuditReport);
        }

        [Fact]
        public async Task StartAsync_ReportsInProgressWhileRunning_ThenTheCompletedResult()
        {
            var index = new GatedReferenceIndex();
            var service = new ContentIntegritySweepService(index, new FakeBalanceAudit());

            var sweep = service.StartAsync();      // returns immediately — the sweep is off-thread
            WaitFor(index.Entered);

            Assert.False(sweep.IsCompleted);
            Assert.Equal(IntegritySweepState.Running, service.Snapshot().State);

            index.Gate.Set();
            await sweep;

            var status = service.Snapshot();
            Assert.Equal(IntegritySweepState.Completed, status.State);
            Assert.Single(status.Broken);
            Assert.NotNull(status.AuditReport);
            Assert.Null(status.ErrorMessage);
        }

        [Fact]
        public async Task StartAsync_WhileASweepIsInFlight_JoinsItRatherThanStartingASecond()
        {
            var index = new GatedReferenceIndex();
            var service = new ContentIntegritySweepService(index, new FakeBalanceAudit());

            var first = service.StartAsync();
            WaitFor(index.Entered);
            var second = service.StartAsync();

            Assert.Same(first, second);

            index.Gate.Set();
            await first;

            Assert.Equal(1, index.SweepCount);
        }

        [Fact]
        public async Task SweepFailure_IsReportedAsFailedWithTheMessage_NotThrownAtTheCaller()
        {
            var index = new GatedReferenceIndex { Throw = new InvalidOperationException("corpus unreadable") };
            var service = new ContentIntegritySweepService(index, new FakeBalanceAudit());

            var sweep = service.StartAsync();
            WaitFor(index.Entered);
            index.Gate.Set();
            await sweep;

            var status = service.Snapshot();
            Assert.Equal(IntegritySweepState.Failed, status.State);
            Assert.Equal("corpus unreadable", status.ErrorMessage);
        }

        [Fact]
        public async Task StartAsync_AfterACompletedSweep_RunsAgain()
        {
            var index = new GatedReferenceIndex();
            var service = new ContentIntegritySweepService(index, new FakeBalanceAudit());

            index.Gate.Set();
            await service.StartAsync();
            Assert.Equal(IntegritySweepState.Completed, service.Snapshot().State);

            index.Entered.Reset();
            await service.StartAsync();

            Assert.Equal(2, index.SweepCount);
            Assert.Equal(IntegritySweepState.Completed, service.Snapshot().State);
        }
    }
}
