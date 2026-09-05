using System;
using System.Collections.Generic;
using System.Linq;
using Building;
using Durango.Utils.Extensions;
using Messages;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

public class ArtifactManager
{
	private readonly Dictionary<string, AppearArtifact> _artifacts;

	private readonly Dictionary<string, AddOns> _addOns;

	private readonly Dictionary<string, Messages.Mannequin> _mannequins;

	public static readonly string[] AddOnTags = new string[4] { "door", "window", "wall_deco", "empty_door" };

	public event Action<ArtifactDisplay> ArtifactDisplayUpdated;

	public event Action<ArtifactState> ArtifactStateUpdated;

	public ArtifactManager(Dictionary<string, AppearArtifact> artifacts, Dictionary<string, AddOns> addons, Dictionary<string, Messages.Mannequin> mannequins)
	{
		_artifacts = artifacts;
		_addOns = addons;
		_mannequins = mannequins;
	}

	public void AddArtifact(AppearArtifact artifact)
	{
		_artifacts.Add(artifact.EntityId, artifact);
	}

	public AppearArtifact? Get(string entityId)
	{
		if (_artifacts.TryGetValue(entityId, out var value))
		{
			return value;
		}
		return null;
	}

	public IEnumerable<AppearArtifact> Enumerable(Predicate<AppearArtifact> func)
	{
		return from pair in _artifacts
			where func(pair.Value)
			select pair.Value;
	}

	public AppearArtifact? RemoveArtifact(string entityId)
	{
		_addOns.Remove(entityId);
		if (_artifacts.TryGetValue(entityId, out var value))
		{
			_artifacts.Remove(entityId);
			return value;
		}
		return null;
	}

	public Messages.Mannequin? GetMannequin(string entityId)
	{
		if (_mannequins.TryGetValue(entityId, out var value))
		{
			return value;
		}
		return null;
	}

	public void SeedPlant(string entityId, string prototypeId)
	{
		Crop crop = CropYaml.Get(prototypeId);
		if (crop != null && _artifacts.TryGetValue(entityId, out var value))
		{
			value.Display.Crop = crop.GrownLooks[KUtility.GetRandomHash(value.Tile.x, value.Tile.y) % crop.GrownLooks.Length];
			_artifacts[entityId] = value;
			if (ArtifactDisplayUpdated != null)
			{
				ArtifactDisplayUpdated(value.Display);
			}
		}
	}

	public void ChargeEffect(string entityId)
	{
		if (_artifacts.TryGetValue(entityId, out var value))
		{
			value.States.Effector = new Effector
			{
				RemainCount = 100
			};
			value.Display.Decorations = new Dictionary<string, Pair<string, string>>();
			value.Display.Decorations.Add("incense", new Pair<string, string>("clan_thurible_incense", string.Empty));
			_artifacts[entityId] = value;
			if (ArtifactDisplayUpdated != null)
			{
				ArtifactDisplayUpdated(value.Display);
			}
		}
	}

	public void Scribble(Scribble scribble)
	{
		if (_artifacts.TryGetValue(scribble.EntityId, out var value))
		{
			value.States.EntityId = value.EntityId;
			value.States.Scribble = new ScribbleContent
			{
				Data = scribble.Data,
				Type = scribble.Type
			};
			_artifacts[scribble.EntityId] = value;
			if (ArtifactStateUpdated != null)
			{
				ArtifactStateUpdated(value.States);
			}
		}
	}

	public void OpenGate(PropKey key, bool open)
	{
		if (_artifacts.TryGetValue(key.EntityId, out var value) && value.States.GateOpened != open)
		{
			value.States.EntityId = value.EntityId;
			value.States.GateOpened = open;
			_artifacts[key.EntityId] = value;
			if (ArtifactStateUpdated != null)
			{
				ArtifactStateUpdated(value.States);
			}
		}
	}

	public void ChangeDecoration(string entityId)
	{
		if (!_artifacts.TryGetValue(entityId, out var value))
		{
			return;
		}
		Building.Blueprint blueprint = GameSystem<RecipeSystem>.Instance().RecipeContainer.GetBlueprint(value.EntityType);
		List<string[]> list = RecipeDict.GetDecorations(blueprint.Id);
		if (KUtility.GetSize(list) == 0)
		{
			return;
		}
		if (value.Display.Decorations == null)
		{
			value.Display.Decorations = new Dictionary<string, Pair<string, string>>();
		}
		if (value.Display.Decorations.TryGetValue("deco", out var curDeco) && string.IsNullOrEmpty(curDeco.Item2))
		{
			List<string[]> list2 = list.Where((string[] o) => o[0] != curDeco.Item1 || !string.IsNullOrEmpty(o[1])).ToList();
			if (list2.Count > 0)
			{
				list = list2;
			}
		}
		string[] array = list[UnityEngine.Random.Range(0, list.Count)];
		string item = ((!string.IsNullOrEmpty(array[1])) ? UnityEngine.Random.ColorHSV().ToHex() : string.Empty);
		value.Display.Decorations["deco"] = new Pair<string, string>(array[0], item);
		_artifacts[entityId] = value;
		if (ArtifactDisplayUpdated != null)
		{
			ArtifactDisplayUpdated(value.Display);
		}
	}

	public AddOns GetAddons(string entityId)
	{
		if (!_addOns.ContainsKey(entityId))
		{
			_addOns.Add(entityId, default(AddOns));
		}
		return _addOns[entityId];
	}

	public AppearArtifact? PlaceAddOns(string entityId, Dictionary<int, Item> placements)
	{
		if (_artifacts.TryGetValue(entityId, out var value))
		{
			Dictionary<int, Pair<string, string>> dictionary = new Dictionary<int, Pair<string, string>>();
			foreach (KeyValuePair<int, Item> placement in placements)
			{
				Performance performance = placement.Value.Performance.FirstOrDefault((Performance o) => o.Id == "add_on");
				if (performance.Strs == null)
				{
					continue;
				}
				string item = performance.Strs.Get("add_on_model_key");
				Messages.Tag[] tags = placement.Value.Tags;
				string item2 = AddOnTags.FirstOrDefault((string o) => tags.Any((Messages.Tag p) => o == p.Id));
				dictionary.Add(placement.Key, new Pair<string, string>(item, item2));
			}
			value.Display.AddOns = dictionary;
			_artifacts[entityId] = value;
			_addOns[entityId] = new AddOns
			{
				_AddOns = placements
			};
			if (ArtifactDisplayUpdated != null)
			{
				ArtifactDisplayUpdated(value.Display);
			}
			return value;
		}
		return null;
	}

	public void UpdateArtifactDisplay(ArtifactDisplay display)
	{
		if (_artifacts.TryGetValue(display.EntityId, out var value))
		{
			value.Display = display;
			_artifacts[display.EntityId] = value;
			if (ArtifactDisplayUpdated != null)
			{
				ArtifactDisplayUpdated(value.Display);
			}
		}
	}

	public AppearArtifact? ExtendFloor(string entityId, bool withRoof)
	{
		if (_artifacts.TryGetValue(entityId, out var value))
		{
			int? stories = value.Stories;
			if (!stories.HasValue)
			{
				return null;
			}
			value.Stories++;
			value.HasRoof = withRoof;
			_artifacts[entityId] = value;
			return value;
		}
		return null;
	}

	public void TurnOnMusic(string entityId)
	{
		if (!_artifacts.TryGetValue(entityId, out var value))
		{
			return;
		}
		Building.Blueprint blueprint = GameSystem<RecipeSystem>.Instance().RecipeContainer.GetBlueprint(value.EntityType);
		if (blueprint != null)
		{
			string[] musics = blueprint.Musics;
			if (KUtility.GetSize(musics) > 0)
			{
				int num = UnityEngine.Random.Range(0, musics.Length);
				value.Display.Music = new Pair<string, double>(musics[num], 0.0);
				_artifacts[entityId] = value;
			}
			if (ArtifactDisplayUpdated != null)
			{
				ArtifactDisplayUpdated(value.Display);
			}
		}
	}

	public void TurnOffMusic(string entityId)
	{
		if (!_artifacts.TryGetValue(entityId, out var value))
		{
			return;
		}
		Pair<string, double>? music = value.Display.Music;
		if (music.HasValue)
		{
			value.Display.Music = null;
			_artifacts[entityId] = value;
			if (ArtifactDisplayUpdated != null)
			{
				ArtifactDisplayUpdated(value.Display);
			}
		}
	}

	public bool TakeOutItems(string entityId, string[] ids)
	{
		if (ids == null)
		{
			return false;
		}
		if (!_artifacts.TryGetValue(entityId, out var _))
		{
			return false;
		}
		Messages.Mannequin? mannequin = GetMannequin(entityId);
		if (!mannequin.HasValue)
		{
			return false;
		}
		Messages.Mannequin value2 = mannequin.Value;
		string text = null;
		Item? head = value2.Head;
		if (head.HasValue && ids.Contains(value2.Head.Value.Id))
		{
			text = "head";
			value2.Head = null;
		}
		else
		{
			Item? body = value2.Body;
			if (body.HasValue && ids.Contains(value2.Body.Value.Id))
			{
				text = "body";
				value2.Body = null;
			}
		}
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		if (value2.Head.HasValue || value2.Body.HasValue)
		{
			_mannequins[entityId] = value2;
		}
		else
		{
			_mannequins.Remove(entityId);
		}
		TakeOffMannequin(entityId, text);
		return true;
	}

	public bool TakeOffMannequin(string entityId, string slot)
	{
		if (!_artifacts.TryGetValue(entityId, out var value))
		{
			return false;
		}
		ArtifactDisplay display = value.Display;
		MannequinDisplayInfo valueOrDefault = display.MannequinInfo.GetValueOrDefault();
		Messages.Mannequin value2 = _mannequins.Get(entityId);
		switch (slot)
		{
		case "head":
			valueOrDefault.Head = null;
			valueOrDefault.HeadColor = null;
			value2.Head = null;
			break;
		case "body":
			valueOrDefault.Body = null;
			valueOrDefault.BodyColor = null;
			value2.Body = null;
			break;
		default:
			return false;
		}
		value2.EntityId = entityId;
		_mannequins[entityId] = value2;
		display.MannequinInfo = valueOrDefault;
		value.Display = display;
		_artifacts[entityId] = value;
		if (ArtifactDisplayUpdated != null)
		{
			ArtifactDisplayUpdated(value.Display);
		}
		return true;
	}

	public bool ChangeMannequin(string entityId, string slot, Item item)
	{
		if (!_artifacts.TryGetValue(entityId, out var value))
		{
			return false;
		}
		if (!SingletonDict<int, ArtifactPrototype>.TryGetValue(value.EntityType, out var value2))
		{
			return false;
		}
		bool flag;
		switch (value2.gender)
		{
		case "male":
			flag = true;
			break;
		case "female":
			flag = false;
			break;
		default:
			return false;
		}
		string text = null;
		string key = ((!flag) ? "female_model" : "male_model");
		if (item.Performance != null)
		{
			Performance[] performance = item.Performance;
			for (int i = 0; i < performance.Length; i++)
			{
				Performance performance2 = performance[i];
				if (performance2.Strs != null && performance2.Strs.TryGetValue(key, out var value3))
				{
					text = value3;
					break;
				}
			}
		}
		if (string.IsNullOrEmpty(text) || text.Equals("None", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		ArtifactDisplay display = value.Display;
		MannequinDisplayInfo valueOrDefault = display.MannequinInfo.GetValueOrDefault();
		Messages.Mannequin value4 = _mannequins.Get(entityId);
		switch (slot)
		{
		case "head":
			valueOrDefault.Head = text;
			valueOrDefault.HeadColor = new string[3] { item.ColorR, item.ColorG, item.ColorB };
			value4.Head = item;
			break;
		case "body":
			valueOrDefault.Body = text;
			valueOrDefault.BodyColor = new string[3] { item.ColorR, item.ColorG, item.ColorB };
			value4.Body = item;
			break;
		default:
			return false;
		}
		value4.EntityId = entityId;
		_mannequins[entityId] = value4;
		display.MannequinInfo = valueOrDefault;
		value.Display = display;
		_artifacts[entityId] = value;
		if (ArtifactDisplayUpdated != null)
		{
			ArtifactDisplayUpdated(value.Display);
		}
		return true;
	}
}
