namespace Hedron.Core.Entities.Properties
{
    public interface IAttributes
	{
		/// <summary>
		/// The base attributes
		/// </summary>
		Attributes BaseAttributes { get; set; }

		/// <summary>
		/// Retrieve modified attributes after Effects
		/// </summary>
		/// <returns>The modified attributes</returns>
		Attributes ModifiedAttributes { get; }
	}
}