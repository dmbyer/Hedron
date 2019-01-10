using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Hedron.System;
using Hedron.Core.Property;
using Hedron.Data;
using Hedron.Network;
using Newtonsoft.Json;

namespace Hedron.Core
{
	/// <summary>
	/// Base entity class for common attributes
	/// </summary>
	abstract public class Entity : EntityEvents, ISpawnableObject, ICopyableObject<Entity>, IEntity
	{
		protected List<Affect> _affects = new List<Affect>();

		/// <summary>
		/// The affects on the entity
		/// </summary>
		public IReadOnlyCollection<Affect> Affects
		{
			get
			{
				return _affects.AsReadOnly();
			}
		}

		/// <summary>
		/// The name of the entity
		/// </summary>
		public string       Name             { get; set; } = "[name]";

		/// <summary>
		/// The Short Description of the entity
		/// </summary>
		public string       ShortDescription { get; set; } = "[short]";

		/// <summary>
		/// The Long Description of the entity
		/// </summary>
		public string       LongDescription  { get; set; } = "[long]";

		/// <summary>
		/// The Tier of the entity
		/// </summary>
		public Tier         Tier             { get; protected set; } = new Tier();

		/// <summary>
		/// The IOHandler for network processing
		/// </summary>
		public IOHandler    IOHandler        { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public Entity() : base()
		{

		}

		/// <summary>
		/// Adds an affect to the entity
		/// </summary>
		/// <param name="affect">The affect to add</param>
		/// <param name="stack">Whether to stack the affect. If false, the affect will overwrite any affect(s) of the same name.</param>
		public virtual void AddAffect(Affect affect, bool stack)
		{
			OnAffectAdded(new AffectEventArgs(affect));
		}

		/// <summary>
		/// Removes all affects of the given name
		/// </summary>
		/// <param name="affectName">The affect name to remove</param>
		public void RemoveAffect(string affectName)
		{
			var indexes = new List<int>();

			indexes = Enumerable.Range(0, _affects.Count)
				.Where(i => _affects[i].Name == affectName)
				.ToList();

			for (var i = indexes.Count; i > 0; i--)
			{
				var affectRemoved = _affects[i - 1];
				RemoveAffectAt(i - 1);
				OnAffectRemoved(new AffectEventArgs(affectRemoved));
			}
		}

		/// <summary>
		/// Removes a specific affect at the given index in Affects
		/// </summary>
		/// <param name="index">The index of the affect to remove</param>
		public virtual void RemoveAffectAt(int index)
		{
			try
			{
				var affectRemoved = _affects[index];
				_affects.RemoveAt(index);
				OnAffectRemoved(new AffectEventArgs(affectRemoved));
			}
			catch
			{
				return;
			}
		}

		/// <summary>
		/// Removes all affects from the entity
		/// </summary>
		public virtual void RemoveAllAffects()
		{
			for (var i = _affects.Count; i > 0; i--)
				RemoveAffectAt(i - 1);
		}

		/// <summary>
		/// Override OnObjectDestroyed to ensure all affects are removed upon entity deletion
		/// </summary>
		/// <param name="source"></param>
		/// <param name="args"></param>
		protected override void OnObjectDestroyed(object source, CacheObjectEventArgs args)
		{
			RemoveAllAffects();
			base.OnObjectDestroyed(source, args);
		}

		/// <summary>
		/// Copies this entity's properties to another entity.
		/// </summary>
		/// <param name="entity">The entity to copy to.</param>
		/// <remarks>Doesn't copy IDs, cache type, or IOHandler.</remarks>
		public virtual void CopyTo(Entity entity)
		{
			if (entity == null)
				return;

			entity.Name = string.Copy(Name);
			entity.LongDescription = string.Copy(LongDescription);
			entity.ShortDescription = string.Copy(ShortDescription);
			entity.Tier.Level = Tier.Level;
		}

		public abstract T SpawnAsObject<T>(bool withEntities, uint? parent = null) where T : CacheableObject;
		public abstract uint? Spawn(bool withEntities, uint? parent = null);
	}
}