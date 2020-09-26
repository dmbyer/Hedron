namespace Hedron.Core.Entities.Properties
{
    public interface IPools
	{
		/// <summary>
		/// The base pools
		/// </summary>
		Pools BaseMaxPools { get; set; }

		/// <summary>
		/// Retrieve modified pools after Effects
		/// </summary>
		/// <returns>The modified pools</returns>
		Pools ModifiedPools { get; }
	}
}