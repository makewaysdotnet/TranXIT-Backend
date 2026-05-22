using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcelotApiGw
{
	public static class OcelotCombiner
	{
		public static void Build()
		{
			string configDirectory = "Configurations";
			// Combined ocelot configuration object
			var combinedConfig = new
			{
				Routes = new List<object>(),
				GlobalConfiguration = new { BaseUrl = "http://localhost:61260" }
			};

			foreach (var file in Directory.GetFiles(configDirectory, "*.json"))
			{
				var json = File.ReadAllText(file);
				json = RemoveComments(json);
				var root = JsonDocument.Parse(json).RootElement;

				if (root.TryGetProperty("Routes", out var routes))
				{
					foreach (var route in routes.EnumerateArray())
					{
						combinedConfig.Routes.Add(route);
					}
				}
			}

			var combinedJson = JsonSerializer
				.Serialize(combinedConfig, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText("ocelot.json", combinedJson);
		}

		private static string RemoveComments(string json)
		{
			// Remove // comments
			json = Regex.Replace(json, @"\/\/.*", "");
			// Remove /* */ comments
			json = Regex.Replace(json, @"\/\*[\s\S]*?\*\/", "");
			return json;
		}
	}
}
