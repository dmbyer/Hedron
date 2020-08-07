using Hedron.Core.Entity.Living;
using Hedron.Core.Entity.Property;
using Hedron.Core.Locale;
using Hedron.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.System.Autogeneration
{
    public static class AutogenMob
    {
        /// <summary>
        /// Creates a mob of all levels and instantiates them
        /// </summary>
        /// <param name="name">The base name of the mob</param>
        /// <param name="parentID">The prototype ID of the parent room</param>
        /// <returns>A list of instance mob IDs</returns>
        public static List<uint?> CreateAndInstantiateAllLevels(string name, uint? parentID)
        {
            var mobs = new List<uint?>();
            var parentRoom = DataAccess.Get<Room>(parentID, CacheType.Prototype);

            foreach (var level in Enum.GetValues(typeof(MobLevel)))
            {
                var mob = Mob.NewPrototype();
                var levelName = MobLevelModifier.MapName((MobLevel)level);
                var desc = $"{levelName} {name}";

                parentRoom?.AddEntity(mob.Prototype, mob, true);

                mob.ShortDescription = $"a {desc}";
                mob.LongDescription = $"A {desc} is here.";
                mob.Name = $"{levelName} {name}";

                DataPersistence.SaveObject(mob);
                DataPersistence.SaveObject(parentRoom);

                mobs.Add(mob.Spawn(true, parentRoom?.Prototype));
            }

            return mobs;
        }
    }
}