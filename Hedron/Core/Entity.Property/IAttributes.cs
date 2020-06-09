namespace Hedron.Core.Entity.Property
{
    public interface IAttributes
	{
		/// <summary>
		/// The base attributes
		/// </summary>
		Attributes BaseAttributes { get; set; }

		/// <summary>
		/// Retrieve modified attributes after affects
		/// </summary>
		/// <returns>The modified attributes</returns>
		Attributes ModifiedAttributes { get; }
	}
}