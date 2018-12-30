using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Hedron.System;
using Hedron.Data;
using Newtonsoft.Json;

namespace Hedron.Core
{
	/// <summary>
	/// Base entity class for common attributes
	/// </summary>
	abstract public partial class Entity : CacheableObject, ISpawnableObject, ICopyableObject<Entity>, IEntity
	{
		// Public Fields
		public string    Name             { get; set; } = "[name]";
		public string    ShortDescription { get; set; } = "[short]";
		public string    LongDescription  { get; set; } = "[long]";
		public Tier      Tier             { get; protected set; } = new Tier();

		public Entity() : base()
		{

		}

		/// <summary>
		/// Copies this entity's properties to another entity.
		/// </summary>
		/// <param name="item">The entity to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public virtual void CopyTo(Entity item)
		{
			if (item == null)
				return;

			item.Name = string.Copy(Name);
			item.LongDescription = string.Copy(LongDescription);
			item.ShortDescription = string.Copy(ShortDescription);
			item.Tier.Level = Tier.Level;
		}

		public abstract T SpawnAsObject<T>(bool withEntities, uint? parent = null) where T : CacheableObject;
		public abstract uint? Spawn(bool withEntities, uint? parent = null);
	}
}