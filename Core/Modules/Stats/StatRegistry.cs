using System.Collections.Generic;

namespace Hedron.Core.Modules.Stats
{
    public sealed class StatRegistry : IStatRegistry
    {
        public IReadOnlyList<ScoreRegistration> All { get; } = new ScoreRegistration[]
        {
            new(ScoreId.Mind,            ScoreRole.Primary),
            new(ScoreId.Body,            ScoreRole.Primary),
            new(ScoreId.Spirit,          ScoreRole.Primary),
            new(ScoreId.Attunement,      ScoreRole.Primary),
            new(ScoreId.HpMax,           ScoreRole.Pool),
            new(ScoreId.HpCurrent,       ScoreRole.Pool),
            new(ScoreId.ManaMax,         ScoreRole.Pool,   GoverningAttribute: ScoreId.Mind),
            new(ScoreId.ManaCurrent,     ScoreRole.Pool,   GoverningAttribute: ScoreId.Mind),
            new(ScoreId.StaminaMax,      ScoreRole.Pool,   GoverningAttribute: ScoreId.Body),
            new(ScoreId.StaminaCurrent,  ScoreRole.Pool,   GoverningAttribute: ScoreId.Body),
            new(ScoreId.AstraMax,        ScoreRole.Pool,   GoverningAttribute: ScoreId.Attunement),
            new(ScoreId.AstraCurrent,    ScoreRole.Pool,   GoverningAttribute: ScoreId.Attunement),
            new(ScoreId.AttackPower,     ScoreRole.Derived),
            new(ScoreId.Defense,         ScoreRole.Derived),
        };
    }
}
