using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Core.Systems.Definitions;
using Hedron.Tests.Spawn;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// T8/T9 — flow tier (temp dir, real loader): the reload-participant port. Participants run after
    /// a successful boot load or reload, in registration order, after the starting room resolves; a
    /// refused reload runs none; a throwing participant becomes one Error and the load still succeeds;
    /// diagnostics are logged as the boot report and returned to <c>reload</c>, which prints one line
    /// each after its unchanged confirmation.
    /// </summary>
    public sealed class WorldContentLoaderParticipantTests
    {
        private static readonly IReadOnlyDictionary<string, object?> NoArgs = new Dictionary<string, object?>();

        private sealed class RecordingParticipant : IContentReloadParticipant
        {
            private readonly List<string> _calls;
            private readonly Func<IReadOnlyList<ContentDiagnostic>> _result;

            public RecordingParticipant(string name, List<string> calls, Func<IReadOnlyList<ContentDiagnostic>>? result = null)
            {
                Name = name;
                _calls = calls;
                _result = result ?? (() => Array.Empty<ContentDiagnostic>());
            }

            public string Name { get; }
            public Func<bool>? Probe { get; init; }
            public bool? ProbeResult { get; private set; }

            public Task<IReadOnlyList<ContentDiagnostic>> RebuildAsync(CancellationToken ct = default)
            {
                _calls.Add(Name);
                if (Probe is not null)
                    ProbeResult = Probe();
                return Task.FromResult(_result());
            }
        }

        private static ContentDiagnostic Warn(string source) => new(ContentDiagnosticSeverity.Warning, source, "careful");
        private static ContentDiagnostic Err(string source) => new(ContentDiagnosticSeverity.Error, source, "broken");

        [Fact]
        public async Task Boot_load_runs_participants_in_registration_order()
        {
            var calls = new List<string>();
            var first = new RecordingParticipant("first", calls);
            var second = new RecordingParticipant("second", calls);
            using var w = new SpawnWorld(new IContentReloadParticipant[] { first, second });
            w.Room("room.start");

            await w.Loader.LoadAndSpawnAsync();

            Assert.Equal(new[] { "first", "second" }, calls);
        }

        [Fact]
        public async Task Participants_run_after_the_starting_room_resolves()
        {
            SpawnWorld? world = null;
            var probe = new RecordingParticipant("probe", new List<string>())
            {
                Probe = () => world!.Config.StartingRoom.EntityId != 0,
            };
            using var w = world = new SpawnWorld(new IContentReloadParticipant[] { probe });
            w.Room("room.start");

            await w.Loader.LoadAndSpawnAsync();

            Assert.True(probe.ProbeResult);
        }

        [Fact]
        public async Task Reload_returns_the_participants_diagnostics()
        {
            var calls = new List<string>();
            var p = new RecordingParticipant("p", calls, () => new[] { Warn("world/a.yaml"), Err("world/b.yaml") });
            using var w = new SpawnWorld(new IContentReloadParticipant[] { p });
            w.Room("room.start");
            await w.BootAsync();

            var result = await w.Loader.ReloadAsync();

            Assert.Equal(new[] { "world/a.yaml", "world/b.yaml" }, result.Diagnostics.Select(d => d.Source));
            Assert.Equal(2, calls.Count);
        }

        [Fact]
        public async Task A_refused_reload_runs_no_participant()
        {
            var calls = new List<string>();
            using var w = new SpawnWorld(new IContentReloadParticipant[] { new RecordingParticipant("p", calls) });
            w.Room("room.start");
            await w.BootAsync();
            calls.Clear();

            w.Room("room.legacy", "  - blueprintId: mob.rat\n    maxCount: 3\n");
            await Assert.ThrowsAsync<ContentLoadException>(() => w.Loader.ReloadAsync());

            Assert.Empty(calls);
        }

        [Fact]
        public async Task A_throwing_participant_becomes_one_error_and_the_load_succeeds()
        {
            var calls = new List<string>();
            var throwing = new RecordingParticipant("boom", calls, () => throw new InvalidOperationException("kaput"));
            var after = new RecordingParticipant("after", calls, () => new[] { Warn("after/x") });
            using var w = new SpawnWorld(new IContentReloadParticipant[] { throwing, after });
            w.Room("room.start");
            await w.BootAsync();

            var result = await w.Loader.ReloadAsync();

            Assert.Collection(result.Diagnostics,
                d => { Assert.Equal(ContentDiagnosticSeverity.Error, d.Severity); Assert.Equal("boom", d.Source); Assert.Contains("kaput", d.Message); },
                d => Assert.Equal("after/x", d.Source));
        }

        [Fact]
        public async Task No_participants_means_empty_diagnostics_and_default_result_is_empty_too()
        {
            using var w = new SpawnWorld();
            w.Room("room.start");
            await w.BootAsync();

            var result = await w.Loader.ReloadAsync();

            Assert.Empty(result.Diagnostics);
            Assert.NotNull(default(ContentReloadResult).Diagnostics);
            Assert.Empty(default(ContentReloadResult).Diagnostics);
        }

        [Fact]
        public async Task Diagnostics_are_logged_as_one_block_with_matching_levels()
        {
            var logger = new ListLogger<WorldContentLoader>();
            var p = new RecordingParticipant("p", new List<string>(), () => new[] { Warn("world/a.yaml"), Err("world/b.yaml") });
            using var w = new SpawnWorld(new IContentReloadParticipant[] { p });
            w.Room("room.start");
            var loader = w.LoaderWithLogger(logger, new IContentReloadParticipant[] { p });

            await loader.LoadAndSpawnAsync();

            var block = logger.Entries.SkipWhile(e => !e.Message.Contains("content participants reported")).ToList();
            Assert.Equal(3, block.Count(e => e.Message.Contains("content participants reported") || e.Message.Contains("world/")));
            Assert.Equal(LogLevel.Information, block[0].Level);
            Assert.Equal(LogLevel.Warning, block[1].Level);
            Assert.Contains("world/a.yaml", block[1].Message);
            Assert.Equal(LogLevel.Error, block[2].Level);
            Assert.Contains("world/b.yaml", block[2].Message);
        }

        [Fact]
        public async Task Reload_command_prints_one_line_per_diagnostic_after_the_confirmation()
        {
            var p = new RecordingParticipant("p", new List<string>(), () => new[] { Warn("world/a.yaml"), Err("world/b.yaml") });
            using var w = new SpawnWorld(new IContentReloadParticipant[] { p });
            w.Room("room.start");
            await w.BootAsync();
            var publishedBefore = w.Bus.Published.Count;

            await w.RunAsync(w.ReloadCommand(), NoArgs);

            var lines = w.Output.All.Select(r => r.Message).OfType<PlainMessage>().ToList();
            Assert.Equal(3, lines.Count);
            Assert.Equal(OutputSeverity.Confirmation, lines[0].Severity);
            Assert.StartsWith("World rebuilt from content:", lines[0].Text);
            Assert.Equal("  [warning] world/a.yaml: careful", lines[1].Text);
            Assert.Equal("  [error] world/b.yaml: broken", lines[2].Text);

            // The audit event is unchanged: still published once, after the output.
            Assert.Single(w.Bus.Published.Skip(publishedBefore).OfType<ContentReloadedEvent>());
        }
    }

    /// <summary>Minimal capturing logger for log-level assertions.</summary>
    internal sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
