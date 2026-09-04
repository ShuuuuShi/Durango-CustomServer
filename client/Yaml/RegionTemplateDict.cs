using System.Collections.Generic;
using Yaml.Util;

namespace Yaml;

public class RegionTemplateDict : SingletonDict<string, RegionTemplate>
{
	protected override void OnInitalized()
	{
		base.OnInitalized();
		using (Enumerator enumerator = GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				KeyValuePair<string, RegionTemplate> current = enumerator.Current;
				current.Value.Id = current.Key;
			}
		}
		List<RegionTemplate> list = new List<RegionTemplate>(base.Count);
		list.AddRange(base.Values);
		list.Sort((RegionTemplate a1, RegionTemplate a2) => a1.Level - a2.Level);
		int num = 0;
		int num2 = 0;
		for (int num3 = 0; num3 < list.Count; num3++)
		{
			if (list[num3].Active)
			{
				if (num2 < list[num3].Level)
				{
					num = num2;
					num2 = list[num3].Level;
				}
				list[num3].AvailableLevel = num + 1;
			}
		}
	}
}
