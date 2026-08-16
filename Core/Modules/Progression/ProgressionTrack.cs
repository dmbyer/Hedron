using System;
using System.Text.Json.Serialization;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// The key of a progression track — one vocabulary over the single improvement engine
    /// (<see cref="Systems.IProgressionSystem"/>), not a parallel key type. A track is either a
    /// <b>score</b> track (an attribute or pool, keyed by <see cref="ScoreId"/>) or an
    /// <b>ability</b> track (keyed by an ability id).
    ///
    /// <para>
    /// <b>Invalid states are unrepresentable.</b> The constructor is private; <see cref="Of"/> and
    /// <see cref="Ability"/> are the only entry points. <see cref="Ability"/> rejects null/empty/
    /// whitespace ids and ids containing the reserved <c>:</c> separator.
    /// <c>default(ProgressionTrack)</c> carries neither field and is invalid —
    /// <see cref="ToKey"/> throws on it rather than silently rendering an empty key.
    /// </para>
    ///
    /// <para>
    /// <b>Serialization is backward compatible by construction.</b> A score track renders as the
    /// bare enum name (<c>"Body"</c>, <c>"HpMax"</c>) — byte-identical to the pre-widening
    /// <c>Dictionary&lt;ScoreId, int&gt;</c> keys <c>ComponentSerializer</c> already emits (it sets
    /// <c>PropertyNamingPolicy</c> but not <c>DictionaryKeyPolicy</c>). Ability tracks take the
    /// reserved <see cref="AbilityPrefix"/>, which no <see cref="ScoreId"/> name can produce, so
    /// existing player snapshots load unchanged and no migration is needed.
    /// </para>
    /// </summary>
    [JsonConverter(typeof(ProgressionTrackJsonConverter))]
    public readonly record struct ProgressionTrack
    {
        /// <summary>Reserved key prefix for ability tracks. No <see cref="ScoreId"/> name can collide.</summary>
        public const string AbilityPrefix = "ability:";

        /// <summary>The score this track improves, or <see langword="null"/> for an ability track.</summary>
        public ScoreId? Score { get; }

        /// <summary>The ability this track improves, or <see langword="null"/> for a score track.</summary>
        public string? AbilityId { get; }

        private ProgressionTrack(ScoreId? score, string? abilityId)
        {
            Score = score;
            AbilityId = abilityId;
        }

        /// <summary>A score track — an attribute or pool that grants power on improvement.</summary>
        public static ProgressionTrack Of(ScoreId score) => new(score, null);

        /// <summary>
        /// An ability track. Display-only: ability rank grants no power (see
        /// <see cref="ProgressionEffectContributor"/>).
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="abilityId"/> is null/empty/whitespace, or contains <c>:</c>.
        /// </exception>
        public static ProgressionTrack Ability(string abilityId)
        {
            if (string.IsNullOrWhiteSpace(abilityId))
                throw new ArgumentException("Ability track id must be non-empty.", nameof(abilityId));
            if (abilityId.Contains(':'))
                throw new ArgumentException(
                    $"Ability track id '{abilityId}' must not contain ':' — the reserved key separator.",
                    nameof(abilityId));

            return new ProgressionTrack(null, abilityId);
        }

        /// <summary>True when this is an ability track.</summary>
        public bool IsAbility => AbilityId is not null;

        /// <summary>True when this is a score track.</summary>
        public bool IsScore => Score is not null;

        /// <summary>
        /// The stable serialized key: the bare <see cref="ScoreId"/> name for a score track,
        /// <c>ability:&lt;id&gt;</c> for an ability track.
        /// </summary>
        /// <exception cref="InvalidOperationException">This is <c>default(ProgressionTrack)</c>.</exception>
        public string ToKey()
        {
            if (Score is { } score)
                return score.ToString();
            if (AbilityId is { } abilityId)
                return AbilityPrefix + abilityId;

            throw new InvalidOperationException(
                "default(ProgressionTrack) is not a valid track — use ProgressionTrack.Of or ProgressionTrack.Ability.");
        }

        /// <summary>Parses a key produced by <see cref="ToKey"/>. Unknown score names are rejected.</summary>
        public static bool TryParse(string? key, out ProgressionTrack track)
        {
            track = default;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (key.StartsWith(AbilityPrefix, StringComparison.Ordinal))
            {
                var abilityId = key.Substring(AbilityPrefix.Length);
                if (string.IsNullOrWhiteSpace(abilityId) || abilityId.Contains(':'))
                    return false;

                track = Ability(abilityId);
                return true;
            }

            if (Enum.TryParse<ScoreId>(key, ignoreCase: false, out var score) && Enum.IsDefined(score))
            {
                track = Of(score);
                return true;
            }

            return false;
        }

        public override string ToString() => ToKey();
    }
}
