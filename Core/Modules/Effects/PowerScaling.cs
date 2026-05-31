using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Effects
{
    public static class PowerScaling
    {
        private static readonly Dictionary<string, Func<EffectDefinition, EntityService, uint, int>> _formulas = new()
        {
            ["fixed"] = (def, _, _) => def.Params.BaseMagnitude,
            ["byAttunement"] = (def, entityService, sourceEntityId) =>
            {
                var attunement = entityService.TryGet<AttributesComponent>(sourceEntityId, out var a)
                    ? a.Attunement : 10;
                return def.Params.BaseMagnitude + attunement / 5;
            },
        };

        public static int Evaluate(string formula, EffectDefinition def, EntityService entityService, uint sourceEntityId)
        {
            if (_formulas.TryGetValue(formula, out var fn))
                return fn(def, entityService, sourceEntityId);
            return def.Params.BaseMagnitude;
        }
    }
}
