namespace Hedron.Data
{
	/// <summary>
	/// Allows an object to copy itself to another object. DO NOT copy the Instance property of ICacheableObject.
	/// </summary>
	public interface ICopyableObject<T>
	{
		/// <summary>
		/// Copies <typeparamref name="T"/> to another <typeparamref name="T"/>. Should not copy the Instance, which needs to be set after caching.
		/// </summary>
		/// <typeparam name="T">The object type, which should match the caller's type.</typeparam>
		/// <param name="obj">The object to copy to.</param>
		void CopyTo(T obj);
	}
}