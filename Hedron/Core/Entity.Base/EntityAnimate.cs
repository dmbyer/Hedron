using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Hedron.System;
using Hedron.Data;
using Hedron.Core.Property;
using Newtonsoft.Json;

namespace Hedron.Core
{
	/// <summary>
	/// For all living entities
	/// </summary>
	abstract public partial class EntityAnimate : Entity
	{
		/// <summary>
		/// Affect dictionary
		/// </summary>
		private Dictionary<string, Affect> _affects = new Dictionary<string, Affect>(); 

		// Stats and Attributes
		[JsonProperty]
		public Attributes BaseAttributes { get; set; } = Attributes.Default();
		[JsonIgnore]
		private Attributes _modifiedAttributes { get; set; } = new Attributes();

		[JsonProperty]
		public Aspects BaseAspects    { get; set; } = Aspects.Default();
		[JsonIgnore]
		private Aspects _modifiedAspects { get; set; } = new Aspects();

		[JsonProperty]
		public Qualities BaseQualities  { get; set; } = Qualities.Default();
		[JsonIgnore]
		private Qualities _modifiedQualities { get; set; } = new Qualities();

		// Equipment management
		[JsonConverter(typeof(InventoryPropertyConverter))]
		public Inventory WornEquipment { get; set; } = new Inventory();

		[JsonConverter(typeof(InventoryPropertyConverter))]
		public Inventory Inventory { get; set; } = new Inventory();

		/// <summary>
		/// Constructor
		/// </summary>
		public EntityAnimate() : base()
		{

		}

		/// <summary>
		/// Gets the entity's attributes after affects
		/// </summary>
		/// <returns></returns>
		public Attributes GetModifiedAttributes()
		{
			_modifiedAttributes.CopyTo(out var newAttribs);
			return newAttribs + BaseAttributes;
		}

		/// <summary>
		/// Gets the entity's aspects after affects
		/// </summary>
		/// <returns></returns>
		public Aspects GetModifiedAspects()
		{
			_modifiedAspects.CopyTo(out var newAspects);
			return newAspects + BaseAspects;
		}

		/// <summary>
		/// Gets the entity's qualities after affects
		/// </summary>
		/// <returns></returns>
		public Qualities GetModifiedQualities()
		{
			_modifiedQualities.CopyTo(out var newQualities);
			return newQualities + BaseQualities;
		}

		/// <summary>
		/// Add an affect to the entity
		/// </summary>
		/// <param name="affect">The affect to add</param>
		public void AddAffect<T>(Affect affect, T affectSource) where T: EntityInanimate
		{
			
		}

		/// <summary>
		/// Removes affect from the entity
		/// </summary>
		/// <param name="affectSource">The source of the affect</param>
		public void RemoveAffect<T>(T affectSource) where T : EntityInanimate
		{

		}

		/// <summary>
		/// Returns all items equipped at the specified slot
		/// </summary>
		/// <param name="slot">The slot to check for equipped items</param>
		/// <returns>A list of equipped items</returns>
		public List<EntityInanimate> EquippedAt(ItemSlot slot)
		{
			List<EntityInanimate> equippedItems = new List<EntityInanimate>();

			foreach (var item in DataAccess.GetMany<EntityInanimate>(WornEquipment.GetAllEntities(), CacheType.Instance))
				if (item.Slot == slot)
					equippedItems.Add(item);

			return equippedItems;
		}

		/// <summary>
		/// Copies this entity's properties to another entity.
		/// </summary>
		/// <param name="entityAnimate">The entity to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public virtual void CopyTo(EntityAnimate entityAnimate)
		{
			if (entityAnimate == null)
				return;

			base.CopyTo(entityAnimate);

			entityAnimate.BaseAspects.CopyTo(BaseAspects);
			entityAnimate.BaseAttributes.CopyTo(BaseAttributes);
			entityAnimate.BaseQualities.CopyTo(BaseQualities);
		}
	}
}