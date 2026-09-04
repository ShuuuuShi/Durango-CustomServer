using System.Collections.Generic;
using System.IO;
using Durango.Logic.Clusters;
using Durango.Utils;

namespace Durango.Online;

public static class Servers
{
	public static IEnumerable<Server> GetServers(Dictionary<string, Cluster> clusters)
	{
		string[] directories = AppData.GetDirectories("offline", "*", SearchOption.TopDirectoryOnly);
		string[] array = directories;
		foreach (string dir in array)
		{
			string key = new DirectoryInfo(dir).Name;
			Cluster cluster = clusters.Get(key);
			if (cluster == null)
			{
				continue;
			}
			Dictionary<string, string> names = new Dictionary<string, string>();
			foreach (KeyValuePair<string, string> name in cluster.Names)
			{
				string text = ((!(name.Key == "en_US")) ? "[기록] " : "[Saved] ");
				names.Add(name.Key, text + name.Value);
			}
			Server server = new Server(key, names);
			if (server.Contexts.Count > 0)
			{
				yield return server;
			}
		}
		yield return new Server("free", new Dictionary<string, string>
		{
			{ "en_US", "Creative Island " },
			{ "ko_KR", "창작섬" }
		});
	}
}
