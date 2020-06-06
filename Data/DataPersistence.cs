using Newtonsoft.Json;
using System.IO;

namespace Hedron.Data
{
	public static class DataPersistence
	{
		public static string PersistencePath { get; set; } = "";

		/// <summary>
		/// Saves an object to the filesystem in json format
		/// </summary>
		/// <param name="obj">The object to serialize and save</param>
		/// <param name="rootFolderPath">The path of the root world folder</param>
		/// <param name="objName">Optional name to use for saving the file in the format of objName.json</param>
		/// <returns>Whether the save action succeeded</returns>
		/// <remarks>The object will be saved under the appropriate folder given its type and the master ID store will be updated.</remarks>
		public static bool SaveObject(ICacheableObject obj)
		{
			try
			{
				var fileName = obj.Prototype.ToString() + ".json";
				var savePath = string.Format("{0}\\{1}\\", PersistencePath, obj.GetType().ToString());
				var serialized = JsonConvert.SerializeObject(obj, Formatting.Indented);

				if (!Directory.Exists(savePath))
					Directory.CreateDirectory(savePath);

				File.WriteAllText(savePath + fileName, serialized);

				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Delete an object from the filesystem
		/// </summary>
		/// <param name="obj">The object to delete</param>
		/// <returns>Whether the save action succeeded</returns>
		public static bool DeleteObject(ICacheableObject obj)
		{
			try
			{
				var fileName = obj.Prototype.ToString() + ".json";
				var delPath = string.Format("{0}\\{1}\\", PersistencePath, obj.GetType().ToString());
				var serialized = JsonConvert.SerializeObject(obj, Formatting.Indented);

				File.Delete(delPath + fileName);

				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Loads an object in json format from the filesystem
		/// </summary>
		/// <typeparam name="T">The object type to deserialize to</typeparam>
		/// <param name="filePath">The path of the json file to load</param>
		/// <param name="objectToLoad">An option object to load to, or a new object if unspecified</param>
		/// <returns>Whether the save action succeeded</returns>
		/// <remarks>The object must be added to the cache.</remarks>
		public static bool LoadObject<T>(string filePath, out T objectToLoad) where T: ICacheableObject, new()
		{
			try
			{
				var fileText = File.ReadAllText(filePath);
				objectToLoad = JsonConvert.DeserializeObject<T>(fileText);

				return true;
			}
			catch
			{
				objectToLoad = default(T);
				return false;
			}
		}
	}
}