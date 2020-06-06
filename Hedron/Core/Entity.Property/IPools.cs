namespace Hedron.Core.Entity.Property
{
    public interface IPools
	{
		/// <summary>
		/// The base pools
		/// </summary>
		Pools BaseMaxPools { get; set; }

		/// <summary>
		/// Retrieve modified pools after affects
		/// </summary>
		/// <returns>The modified pools</returns>
		Pools ModifiedPools { get; }
	}
}