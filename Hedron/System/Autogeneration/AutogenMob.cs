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
            var parentRoom = DataAccess.Get<Room>(parentID, CacheType.Instance);
            var protoRoom = DataAccess.Get<Room>(parentRoom?.Prototype, CacheType.Prototype);

            // Fail gracefully upon an invalid room ID
            if (parentRoom == null)
            {
                Logger.Error(nameof(AutogenMob), nameof(CreateAndInstantiateAllLevels), "Invalid Room ID. Cannot create and instantiate mobs.");
                return new List<uint?>();
            }

            foreach (var level in Enum.GetValues(typeof(MobLevel)))
            {
                var mob = Mob.NewPrototype((MobLevel)level);
                var levelName = MobLevelModifier.MapName((MobLevel)level);
                var desc = $"{levelName} {name}";

                protoRoom.AddEntity(mob.Prototype, mob, true);

                mob.ShortDescription = $"a {desc}";
                mob.LongDescription = $"A {desc} is here.";
                mob.Name = $"{levelName} {name}";

                DataPersistence.SaveObject(mob);

                mobs.Add(mob.Spawn(true, parentRoom.Instance));
            }

            DataPersistence.SaveObject(protoRoom);

            return mobs;
        }
    }
}