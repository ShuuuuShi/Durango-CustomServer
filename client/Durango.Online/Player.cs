using System;
using System.Collections.Generic;
using System.Linq;
using Building;
using Durango.Logic.Clusters;
using Durango.Logic.Social;
using Durango.Network;
using Durango.Terrain;
using Durango.UI.Control;
using Durango.Utils;
using Durango.Utils.Extensions;
using InteractionData;
using Messages;
using Shared.Ability;
using Shared.Item;
using Shared.Teleport;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

public class Player
{
	private const string EpicCategory = "sunset";

	private readonly Connection _connection;

	private readonly World _world;

	private readonly PlayerContext _context;

	private int _centerX;

	private int _centerY;

	private readonly World.ChunkVisit[,] _chunkVisited;

	private readonly HashSet<string> _artifactSet = new HashSet<string>();

	public string EntityId { get; private set; }

	public bool IsLocalPlayer { get; private set; }

	public event Action Closed;

	public event Action ContextChanged;

	public Player(string entityId, Connection connection, World world, PlayerContext context, bool isLocalPlayer)
	{
		EntityId = entityId;
		_connection = connection;
		_world = world;
		_context = context;
		IsLocalPlayer = isLocalPlayer;
		if (_context.AppearPlayer.Move.Movements == null || !IsLocalPlayer)
		{
			_context.AppearPlayer.Move.Movements = new Movement[1];
			_context.AppearPlayer.Move.Movements[0].Path = new Location[1];
			_context.AppearPlayer.Move.Movements[0].Path[0].Position = GetEntryPosition();
		}
		_centerX = _world.NumChunksX / 2;
		_centerY = _world.NumChunksY / 2;
		_chunkVisited = new World.ChunkVisit[_world.NumChunksX, _world.NumChunksY];
		_world.ArtifactAppeared += World_ArtifactAppeared;
		_world.ArtifactDisappeared += World_ArtifactDisappeared;
		_world.PlayerAppeared += World_PlayerAppeared;
		_world.PlayerDisappeared += World_PlayerDisappeared;
		_world.ArtifactManager.ArtifactDisplayUpdated += delegate(ArtifactDisplay msg)
		{
			Send(msg);
		};
		_world.ArtifactManager.ArtifactStateUpdated += delegate(ArtifactState msg)
		{
			Send(msg);
		};
		_world.NaturalAdded += delegate(Point2 chunk, byte[] bytes)
		{
			Send(new GardenDiff
			{
				Chunk = chunk,
				_GardenDiff = bytes
			});
		};
		_world.NaturalDestroyed += delegate(Point2 tile)
		{
			Send(new DisappearEntityOnTile
			{
				Tile = tile
			});
		};
		_connection.Recv(delegate(SetChunk msg, PacketHeader header)
		{
			SetCenterChunks(msg.Chunk.x, msg.Chunk.y);
		});
		_connection.Recv(delegate(Move msg, PacketHeader header)
		{
			_world.BroadCast(msg);
			HandleMoveMsg(msg.Movements);
		});
		_connection.Recv(delegate(Cheat msg, PacketHeader header)
		{
			HandleCheatMsg(msg._Cheat, header.Seq);
		});
		_connection.Recv(delegate(Messages.Touch msg, PacketHeader header)
		{
			HandleTouchMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(DestructArtifact msg, PacketHeader header)
		{
			HandleDestructMsg(msg);
		});
		_connection.Recv(delegate(RestOn msg, PacketHeader header)
		{
			Send(default(OK), header.Seq);
			OnContextChanged();
		});
		_connection.Recv(delegate(Wash msg, PacketHeader header)
		{
			Send(new Timer
			{
				Duration = 5f
			}, header.Seq);
			OnContextChanged();
		});
		_connection.Recv(delegate(PlantSeed msg, PacketHeader header)
		{
			HandlePlantSeedMsg(msg);
		});
		_connection.Recv(delegate(ChargeEffect msg, PacketHeader header)
		{
			HandleChargeEffectMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(Scribble msg, PacketHeader header)
		{
			HandleScribbleMsg(msg);
		});
		_connection.Recv(delegate(DumpItems msg, PacketHeader header)
		{
			HandleDumpItemsMsg(msg);
		});
		_connection.Recv(delegate(GetAddOns msg, PacketHeader header)
		{
			HandleGetAddOnsMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(PlaceAddOns msg, PacketHeader header)
		{
			HandlePlaceAddOnsMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(Messages.Display msg, PacketHeader header)
		{
			HandleChangeDecorationMsg(msg);
		});
		_connection.Recv(delegate(Equip msg, PacketHeader header)
		{
			HandleEquipMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(SearchProducts msg, PacketHeader header)
		{
			HandleSearchProductsMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(GetFavoriteProducts msg, PacketHeader header)
		{
			HandleGetFavoriteProductsMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(BuyProduct msg, PacketHeader header)
		{
			HandleBuyProductMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(GetRecipes msg, PacketHeader header)
		{
			HandleGetRecipesMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(GetArtifactBlueprints msg, PacketHeader header)
		{
			HandleGetArtifactBlueprintsMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(ExtendFloor msg, PacketHeader header)
		{
			HandleExtendFloorMsg(msg);
		});
		_connection.Recv(delegate(GetMusics msg, PacketHeader header)
		{
			HandleGetMusicsMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(SaveMusicToSlot msg, PacketHeader header)
		{
			HandleSaveMusicToSlotMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(RemoveMusicFromSlot msg, PacketHeader header)
		{
			HandleRemoveMusicFromSlotMsg(msg, header.Seq);
		});
		_connection.Recv(delegate(PlayMusic msg, PacketHeader header)
		{
			HandlePlayMusicMsg(msg);
		});
		_connection.Recv(delegate(StopMusic msg, PacketHeader header)
		{
			HandleStopMusicMsg(msg);
		});
		_connection.Recv(delegate(ArtifactDisplay msg, PacketHeader header)
		{
			HandleArtifactDisplayMsg(msg);
		});
		_connection.Recv<GetAvailableEmotions>(delegate
		{
			List<Durango.Logic.Social.Motion> motions = GameSystem<SocialSystem>.Instance().Emotional.Motions;
			AvailableEmotions msg2 = default(AvailableEmotions);
			msg2.Motions = motions.Select((Durango.Logic.Social.Motion motion) => motion.Key).ToArray();
			List<Durango.Logic.Social.Emoticon> emoticons = GameSystem<SocialSystem>.Instance().Emotional.Emoticons;
			msg2.Emoticons = emoticons.Select((Durango.Logic.Social.Emoticon emo) => emo.Key).ToArray();
			Send(msg2);
		});
		_connection.Recv(delegate(SayInExclusiveChannel msg, PacketHeader header)
		{
			Message_ message = msg.Message;
			message.Speaker = new RadioId
			{
				Name = _context.AppearPlayer.Name,
				Freq = _context.AppearPlayer.Freq
			};
			msg.Message = message;
			_world.BroadCast(msg);
		});
		_connection.Recv(delegate(DisappearEntityOnTile msg, PacketHeader header)
		{
			_world.DestroyNatural(msg.Tile);
		});
		_connection.Recv(delegate(GetEstateLicenses msg, PacketHeader header)
		{
			Send(default(EstateLicenses), header.Seq);
		});
		_connection.Recv(delegate(OpenGate msg, PacketHeader header)
		{
			_world.ArtifactManager.OpenGate(new PropKey
			{
				EntityId = msg.EntityId,
				Tile = msg.Tile
			}, open: true);
		});
		_connection.Recv(delegate(CloseGate msg, PacketHeader header)
		{
			_world.ArtifactManager.OpenGate(new PropKey
			{
				EntityId = msg.EntityId,
				Tile = msg.Tile
			}, open: false);
		});
		_connection.Recv(delegate(SetStorageItem msg, PacketHeader header)
		{
			_context.Storage[msg.Key] = msg.Value;
			OnContextChanged();
		});
		_connection.Recv(delegate(TurnOnMusic msg, PacketHeader header)
		{
			_world.ArtifactManager.TurnOnMusic(msg.EntityId);
		});
		_connection.Recv(delegate(TurnOffMusic msg, PacketHeader header)
		{
			_world.ArtifactManager.TurnOffMusic(msg.EntityId);
		});
		_connection.Recv(delegate(ChangeMannequinDisplay msg, PacketHeader header)
		{
			List<Item> inventoryItems = _context.InventoryItems;
			int num = inventoryItems.FindIndex((Item it) => it.Id == msg.ItemId);
			if (num != -1)
			{
				Item item = inventoryItems[num];
				if (_world.ArtifactManager.ChangeMannequin(msg.EntityId, msg.Slot, item))
				{
					Send(default(OK), header.ReplyOf);
					return;
				}
			}
			Send(default(Abort), header.ReplyOf);
		});
		_connection.Recv(delegate(TakeOutItem msg, PacketHeader header)
		{
			if (_world.ArtifactManager.TakeOutItems(msg.EntityId, msg.ItemIds))
			{
				Send(default(OK), header.ReplyOf);
			}
			else
			{
				Send(default(Abort), header.ReplyOf);
			}
		});
		_connection.Recv(delegate(GetGrazedPets msg, PacketHeader header)
		{
			Send(new GrazedPets
			{
				Data = _world.GetGrazedPets().ToArray()
			}, header.ReplyOf);
		});
		_connection.Recv(delegate(GetQuests msg, PacketHeader header)
		{
			Chapters chapters = SingletonDict<string, Chapters>.Instance.Get("sunset");
			if (chapters != null)
			{
				Send(new Quests
				{
					Category = "sunset",
					Todos = chapters.ChapterList.Select((Chapter chapter) => (chapter.Quests != null) ? chapter.Quests.Select((string q) => new QuestToDo
					{
						Finished = true,
						Id = q
					}) : Enumerable.Empty<QuestToDo>()).Aggregate(Enumerable.Concat).ToArray()
				}, header.Seq);
			}
		});
		_connection.ConnetionClosed += delegate
		{
			if (Closed != null)
			{
				Closed();
			}
		};
		_context.PlayerInfo.DisconnectedAt = Times.UnixTimeNow();
		SendStatistics();
		SendInventory();
		SendEquipments();
		SendDefoggedChunks();
		SendQuestCategories();
		Send(_context.AppearPlayer);
	}

	private WorldPosition GetEntryPosition()
	{
		return new WorldPosition(_world.EntryPoint.x * 200, _world.EntryPoint.y * 200);
	}

	private void World_ArtifactAppeared(AppearArtifact artifact)
	{
		if (IsOverlapped(artifact))
		{
			_artifactSet.Add(artifact.EntityId);
			Send(artifact);
		}
	}

	private void World_ArtifactDisappeared(AppearArtifact artifact)
	{
		if (_artifactSet.Contains(artifact.EntityId))
		{
			_artifactSet.Remove(artifact.EntityId);
			SendDisappear(artifact);
		}
	}

	private void SendDisappear(AppearArtifact artifact)
	{
		DisappearEntity msg = new DisappearEntity
		{
			EntityId = artifact.EntityId
		};
		Send(msg);
	}

	private void World_PlayerAppeared(Player player)
	{
		SendAppear(player);
	}

	public void SendAppear(Player player)
	{
		if (!(player.EntityId == EntityId))
		{
			Send(player._context.AppearPlayer);
		}
	}

	private void World_PlayerDisappeared(Player player)
	{
		SendDisappear(player);
	}

	private void SendDisappear(Player player)
	{
		if (!(player.EntityId == EntityId))
		{
			Send(new DisappearEntity
			{
				EntityId = player.EntityId
			});
		}
	}

	public void AddItems(IList<Item> items)
	{
		_context.InventoryItems.AddRange(items);
		OnContextChanged();
	}

	private void OnContextChanged()
	{
		if (ContextChanged != null)
		{
			ContextChanged();
		}
	}

	private void SendStatistics()
	{
		Statistics msg = default(Statistics);
		msg.DerivedsAbilities = new Dictionary<Derived, float>();
		msg.DerivedsAbilities.Add(Derived.Swimming, 100f);
		msg.BasicAbilities = new Dictionary<Basic, int>();
		msg.Level = _context.AppearPlayer.Level;
		msg.Exp = 3532536;
		Send(msg);
	}

	private void SetCenterChunks(int x, int y)
	{
		int num = Mathf.Clamp(x, 0, _world.NumChunksX - 1);
		int num2 = Mathf.Clamp(y, 0, _world.NumChunksY - 1);
		ClearVisited(num, num2);
		_centerX = num;
		_centerY = num2;
		MarkVisit();
		List<Chunk> list = _world.CreateChunkMessages(_centerX, _centerY, _chunkVisited);
		for (int i = 0; i < list.Count; i++)
		{
			Send(list[i]);
		}
		List<string> list2 = _artifactSet.ToList();
		foreach (string item in list2)
		{
			AppearArtifact? appearArtifact = _world.ArtifactManager.Get(item);
			if (appearArtifact.HasValue && !IsOverlapped(appearArtifact.Value))
			{
				SendDisappear(appearArtifact.Value);
				_artifactSet.Remove(item);
			}
		}
		foreach (AppearArtifact item2 in _world.ArtifactManager.Enumerable((AppearArtifact artifact) => !_artifactSet.Contains(artifact.EntityId) && IsOverlapped(artifact)))
		{
			_artifactSet.Add(item2.EntityId);
			Send(item2);
		}
	}

	private void ClearVisited(int newX, int newY)
	{
		for (int i = _centerX - 1; i <= _centerX + 1; i++)
		{
			for (int j = _centerY - 1; j <= _centerY + 1; j++)
			{
				if (i >= 0 && i < _world.NumChunksX && j >= 0 && j < _world.NumChunksY && (i < newX - 1 || i > newX + 1 || j < newY - 1 || j > newY + 1))
				{
					_chunkVisited[i, j] = World.ChunkVisit.None;
				}
			}
		}
	}

	private void MarkVisit()
	{
		for (int i = _centerX - 1; i <= _centerX + 1; i++)
		{
			for (int j = _centerY - 1; j <= _centerY + 1; j++)
			{
				if (i >= 0 && i < _world.NumChunksX && j >= 0 && j < _world.NumChunksY && _chunkVisited[i, j] != World.ChunkVisit.Sent)
				{
					_chunkVisited[i, j] = World.ChunkVisit.Visit;
				}
			}
		}
	}

	private void HandleMoveMsg(Movement[] movements)
	{
		int num = movements.Length - 1;
		if (num >= 0)
		{
			Movement movement = movements[num];
			_context.AppearPlayer.Move.Movements[0] = movement;
			int num2 = movement.Path.Length - 1;
			if (num2 >= 0)
			{
				_context.AppearPlayer.Move.Movements[0].Path[0].Position = movement.Path[num2].Position;
			}
		}
	}

	private bool IsOverlapped(AppearArtifact artifact)
	{
		int num = (_centerX - 1) * 16;
		int num2 = (_centerX + 2) * 16;
		int num3 = (_centerY - 1) * 16;
		int num4 = (_centerY + 2) * 16;
		bool flag = num - artifact.Size.x + 1 <= artifact.Tile.x && artifact.Tile.x < num2;
		bool flag2 = num3 - artifact.Size.y + 1 <= artifact.Tile.y && artifact.Tile.y < num4;
		return flag && flag2;
	}

	private void SendDefoggedChunks()
	{
		DefoggedChunks msg = _world.CreateDefoggedChunks();
		Send(msg);
	}

	private void SendQuestCategories()
	{
		QuestCategory value = new QuestCategory
		{
			Category = "sunset"
		};
		Send(new QuestCategories
		{
			Epic = value
		});
	}

	private void HandleCheatMsg(string cheat, uint seq)
	{
		cheat = cheat.ToLower();
		string[] array = cheat.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			return;
		}
		switch (array[0])
		{
		case "m":
			if (array.Length >= 3)
			{
				Teleported msg = default(Teleported);
				msg.Type = TeleportType.Unknown;
				if (int.TryParse(array[1], out msg.Tile.x) && int.TryParse(array[2], out msg.Tile.y))
				{
					Send(msg);
				}
			}
			break;
		case "it":
		{
			InventoryUpdated msg2 = new InventoryUpdated
			{
				EntityId = EntityId
			};
			int level = int.Parse(array[2]);
			int result = 0;
			if (KUtility.GetSize(array) >= 4)
			{
				int.TryParse(array[3], out result);
			}
			if (result == 0)
			{
				result = 1;
			}
			List<Item> list = new List<Item>();
			for (int num6 = 0; num6 < result; num6++)
			{
				Item? item2 = Cheats.MakeItem(array[1], level);
				if (item2.HasValue)
				{
					list.Add(item2.Value);
				}
			}
			msg2.Items = list.ToArray();
			AddItems(list);
			Send(new Info
			{
				Text = $"{list[0].Name} {result}개 획득"
			}, seq);
			Send(msg2);
			break;
		}
		case "immortal":
		case "prop":
		{
			AppearArtifact? appearArtifact = Cheats.MakeAppearArtifact(array, out var addons);
			if (!appearArtifact.HasValue)
			{
				return;
			}
			_world.ConstructArtifact(appearArtifact.Value, addons);
			break;
		}
		case "it_color":
			if (array.Length >= 2)
			{
				string itemId2 = array[1];
				List<Item> inventoryItems2 = _context.InventoryItems;
				int num5 = inventoryItems2.FindIndex((Item it) => it.Id == itemId2);
				if (num5 == -1)
				{
					return;
				}
				Item item = inventoryItems2[num5];
				Prototype itemPrototype = PrototypeYaml.GetItemPrototype(item.Prototype);
				if (itemPrototype == null)
				{
					return;
				}
				int value = UnityEngine.Random.Range(0, int.MaxValue);
				ItemIconTex.TryGetDefaultColor(itemPrototype.ColorR, out var col, value, Color.white);
				ItemIconTex.TryGetDefaultColor(itemPrototype.ColorG, out var col2, value, Color.white);
				ItemIconTex.TryGetDefaultColor(itemPrototype.ColorB, out var col3, value, Color.white);
				item.ColorR = col.ToHex();
				item.ColorG = col2.ToHex();
				item.ColorB = col3.ToHex();
				inventoryItems2[num5] = item;
				Send(new InventoryUpdated
				{
					EntityId = EntityId,
					Items = new Item[1] { item }
				});
				if (_context.EquippedItems.Any((KeyValuePair<string, string> pair) => pair.Value == itemId2))
				{
					UpdateEquipments();
					_world.BroadCast(_context.AppearPlayer.Display);
				}
			}
			break;
		case "natural":
			if (array.Length >= 4)
			{
				int x = array[1].ToInt();
				int y = array[2].ToInt();
				int num3 = array[3].ToInt();
				_world.AddNatural(new Point2(x, y), (ushort)num3);
			}
			break;
		case "weather":
		{
			string[] array2 = new string[6] { "sunny", "cloudy", "rainy", "heavy_rainy", "snowy", "heavy_snowy" };
			string text = null;
			for (int num4 = 0; num4 < 10; num4++)
			{
				string text2 = array2[UnityEngine.Random.Range(0, array2.Length)];
				if (text2 != _world.Weather)
				{
					text = text2;
					break;
				}
			}
			if (text != null)
			{
				_world.ChangeWeather(text);
			}
			return;
		}
		case "pet_grazing":
		{
			string itemId = array[1];
			List<Item> inventoryItems = _context.InventoryItems;
			int num2 = inventoryItems.FindIndex((Item it) => it.Id == itemId);
			if (num2 == -1)
			{
				return;
			}
			PerformanceYaml.Rein rein = PerformanceYaml.GetRein(inventoryItems[num2].Prototype);
			if (rein == null)
			{
				return;
			}
			List<Messages.Pet> grazedPets2 = _world.GetGrazedPets();
			grazedPets2.Add(new Messages.Pet
			{
				EntityId = itemId,
				EntityType = (ushort)rein.PetEntityType,
				Name = rein.PetName,
				Stat = new PetStats
				{
					GrazedAt = Times.UnixTimeNow(),
					PlaybackRate = rein.PlaybackRate
				}
			});
			_world.BroadCast(new GrazedPets
			{
				Data = grazedPets2.ToArray()
			});
			_context.InventoryItems.RemoveAt(num2);
			Send(new InventoryUpdated
			{
				EntityId = EntityId,
				RemovedItemIds = new string[1] { itemId }
			});
			_world.Save();
			break;
		}
		case "remove_grazing":
		{
			string id = array[1];
			List<Messages.Pet> grazedPets = _world.GetGrazedPets();
			int num = grazedPets.FindIndex((Messages.Pet p) => p.EntityId == id);
			if (num != -1)
			{
				grazedPets.RemoveAt(num);
				_world.BroadCast(new GrazedPets
				{
					Data = grazedPets.ToArray()
				});
				_world.Save();
			}
			break;
		}
		}
		OnContextChanged();
	}

	private void HandleTouchMsg(Messages.Touch touch, uint seq)
	{
		if (touch.EntityType == 0)
		{
			return;
		}
		Touched msg = new Touched
		{
			EntityId = touch.EntityId
		};
		bool flag = GameManager.ClusterMode == Mode.Editable;
		if (touch.EntityType < 10000)
		{
			Building.Blueprint blueprint = GameSystem<RecipeSystem>.Instance().RecipeContainer.GetBlueprint(touch.EntityType);
			if (blueprint != null)
			{
				msg.EntityName = blueprint.Name;
				List<Interaction> list = new List<Interaction>();
				if (flag)
				{
					list.Add(Interaction.DestructArtifact);
				}
				if (blueprint.Components.Contains("Washable"))
				{
					list.Add(Interaction.Wash);
				}
				if (blueprint.Components.Contains("Shelter"))
				{
					list.Add(Interaction.Rest);
				}
				if (blueprint.Components.Contains("Growable") && flag)
				{
					list.Add(Interaction.Plant);
				}
				if (blueprint.Components.Contains("Modular") && flag)
				{
					list.Add(Interaction.AddOnManage);
					list.Add(Interaction.RemodelArtifact);
				}
				if (blueprint.Components.Contains("Scribble") && flag)
				{
					list.Add(Interaction.ScribbleDrawing);
					list.Add(Interaction.ScribbleText);
				}
				if (blueprint.Components.Contains("Gate") && flag)
				{
					AppearArtifact? appearArtifact = _world.ArtifactManager.Get(touch.EntityId);
					if (appearArtifact.HasValue)
					{
						list.Add((!appearArtifact.Value.States.GateOpened) ? Interaction.OpenGate : Interaction.CloseGate);
					}
				}
				if (blueprint.Components.Contains("Mannequin") && flag)
				{
					list.Add(Interaction.ChangeMannequinHead);
					list.Add(Interaction.ChangeMannequinBody);
				}
				if (KUtility.GetSize(blueprint.Musics) > 0 && flag)
				{
					AppearArtifact? appearArtifact2 = _world.ArtifactManager.Get(touch.EntityId);
					if (appearArtifact2.HasValue)
					{
						list.Add((!appearArtifact2.Value.Display.Music.HasValue) ? Interaction.TurnOnMusic : Interaction.TurnOffMusic);
					}
				}
				if (RecipeDict.HasDecoration(blueprint.Id) && flag)
				{
					list.Add(Interaction.ChangeDecoration);
				}
				msg.Interactions = list.Select((Interaction o) => (int)o).ToArray();
			}
			msg.Mannequin = _world.ArtifactManager.GetMannequin(touch.EntityId);
		}
		else if (DataHelper.IsNaturalObject(touch.EntityType))
		{
			BiomeSpriteInfo biomeSpriteInfo = DataHelper.GetBiomeSpriteInfo(touch.EntityType);
			if (biomeSpriteInfo != null)
			{
				msg.EntityName = biomeSpriteInfo.Name;
			}
			if (flag)
			{
				msg.Interactions = new int[1] { 10268 };
			}
		}
		Send(msg, seq);
		OnContextChanged();
	}

	private void HandleDestructMsg(DestructArtifact msg)
	{
		_world.DestructArtifact(msg.EntityId);
	}

	private void HandleDumpItemsMsg(DumpItems msg)
	{
		_context.InventoryItems.RemoveAll((Item o) => msg.ItemIds.Any((string p) => p == o.Id));
		Send(new InventoryUpdated
		{
			EntityId = EntityId,
			RemovedItemIds = msg.ItemIds
		});
		OnContextChanged();
	}

	private void HandleGetAddOnsMsg(GetAddOns msg, uint seq)
	{
		Send(_world.ArtifactManager.GetAddons(msg.EntityId), seq);
	}

	private void HandlePlaceAddOnsMsg(PlaceAddOns msg, uint seq)
	{
		Dictionary<int, Item> dictionary = new Dictionary<int, Item>();
		AddOns addons = _world.ArtifactManager.GetAddons(msg.EntityId);
		foreach (KeyValuePair<int, string> pair in msg.AddOnPlacements)
		{
			Item? item = null;
			int num = _context.InventoryItems.FindIndex((Item o) => o.Id == pair.Value);
			if (num == -1)
			{
				if (addons._AddOns != null)
				{
					foreach (KeyValuePair<int, Item> addOn in addons._AddOns)
					{
						if (pair.Value == addOn.Value.Id)
						{
							item = addOn.Value;
							break;
						}
					}
				}
			}
			else
			{
				item = _context.InventoryItems[num];
			}
			if (item.HasValue)
			{
				dictionary.Add(pair.Key, item.Value);
			}
		}
		if (_world.ArtifactManager.PlaceAddOns(msg.EntityId, dictionary).HasValue)
		{
			Send(_world.ArtifactManager.GetAddons(msg.EntityId), seq);
		}
	}

	private void HandlePlantSeedMsg(PlantSeed msg)
	{
		foreach (Item inventoryItem in _context.InventoryItems)
		{
			if (inventoryItem.Id == msg.SeedItemId)
			{
				_world.ArtifactManager.SeedPlant(msg.EntityId, inventoryItem.Prototype);
				break;
			}
		}
	}

	private void HandleChargeEffectMsg(ChargeEffect msg, uint seq)
	{
		_world.ArtifactManager.ChargeEffect(msg.EntityId);
		Send(default(OK), seq);
	}

	private void HandleScribbleMsg(Scribble msg)
	{
		_world.ArtifactManager.Scribble(msg);
	}

	private void HandleChangeDecorationMsg(Messages.Display msg)
	{
		_world.ArtifactManager.ChangeDecoration(msg.EntityId);
	}

	private void HandleEquipMsg(Equip msg, uint headerSeq)
	{
		if (msg.Action == "equip")
		{
			int num = _context.InventoryItems.FindIndex((Item x) => x.Id == msg.ItemId);
			if (num < 0)
			{
				return;
			}
			_context.EquippedItems[msg.SlotName] = msg.ItemId;
		}
		else if (!_context.EquippedItems.Remove(msg.SlotName))
		{
			return;
		}
		SendEquipments(headerSeq);
		_world.BroadCast(_context.AppearPlayer.Display);
		OnContextChanged();
	}

	private void HandleSearchProductsMsg(SearchProducts msg, uint seq)
	{
		Products msg2 = _world.MarketManager.SearchProduct(msg);
		Send(msg2, seq);
	}

	private void HandleGetFavoriteProductsMsg(GetFavoriteProducts msg, uint seq)
	{
		Send(default(Products), seq);
	}

	private void HandleBuyProductMsg(BuyProduct msg, uint seq)
	{
		Item[] array = _world.MarketManager.BuyProduct(msg.ProductId);
		if (array == null)
		{
			Send(default(Messages.Error), seq);
			return;
		}
		InventoryUpdated msg2 = new InventoryUpdated
		{
			EntityId = EntityId,
			Items = array
		};
		AddItems(array);
		Send(msg2);
		Send(default(OK), seq);
	}

	private void HandleGetRecipesMsg(GetRecipes msg, uint seq)
	{
		Send(default(Recipes), seq);
	}

	private void HandleGetArtifactBlueprintsMsg(GetArtifactBlueprints msg, uint seq)
	{
		List<string> list = new List<string>();
		foreach (Building.Blueprint allBlueprint in GameSystem<RecipeSystem>.Instance().RecipeContainer.GetAllBlueprints())
		{
			if (allBlueprint.IsShowCraftMode)
			{
				list.Add(allBlueprint.Id);
			}
		}
		Send(new ArtifactBlueprints
		{
			Ids = list.ToArray()
		}, seq);
	}

	private void HandleExtendFloorMsg(ExtendFloor msg)
	{
		_world.ExtendFloor(msg.EntityId, msg.WithRoof);
	}

	private void HandleGetMusicsMsg(GetMusics msg, uint seq)
	{
		Send(new Musics
		{
			_Musics = _context.Musics
		}, seq);
	}

	private void HandleSaveMusicToSlotMsg(SaveMusicToSlot msg, uint seq)
	{
		Dictionary<int, Music> dictionary = _context.Musics;
		if (dictionary == null)
		{
			dictionary = new Dictionary<int, Music>();
			_context.Musics = dictionary;
		}
		dictionary[msg.Slot] = msg.Music;
		Send(default(OK), seq);
		OnContextChanged();
	}

	private void HandleRemoveMusicFromSlotMsg(RemoveMusicFromSlot msg, uint seq)
	{
		Dictionary<int, Music> musics = _context.Musics;
		if (musics != null && musics.Remove(msg.Slot))
		{
			Send(default(OK), seq);
			OnContextChanged();
		}
		else
		{
			Send(default(Abort), seq);
		}
	}

	private void HandlePlayMusicMsg(PlayMusic msg)
	{
		Messages.Musician msg2 = new Messages.Musician
		{
			EntityId = EntityId
		};
		Dictionary<int, Music> musics = _context.Musics;
		if (musics == null || !musics.TryGetValue(msg.Slot, out var value))
		{
			return;
		}
		msg2.Music = value;
		int num = _context.InventoryItems.FindIndex((Item item2) => item2.Id == msg.InstrumentItemId);
		if (num == -1)
		{
			return;
		}
		Item item = _context.InventoryItems[num];
		string text = null;
		if (item.Performance != null)
		{
			Performance performance = item.Performance.FirstOrDefault((Performance p) => p.Id == "instrument");
			if (performance.Strs != null)
			{
				text = performance.Strs.Get("timbre");
			}
		}
		if (!string.IsNullOrEmpty(text))
		{
			msg2.Timbre = text;
			msg2.PlayedAt = Times.UnixTimeNow();
			_world.BroadCast(msg2);
		}
	}

	private void HandleStopMusicMsg(StopMusic msg)
	{
		_world.BroadCast(new Messages.Musician
		{
			EntityId = EntityId
		});
	}

	private void HandleArtifactDisplayMsg(ArtifactDisplay msg)
	{
		_world.ArtifactManager.UpdateArtifactDisplay(msg);
	}

	private void SendInventory()
	{
		Inventory msg = default(Inventory);
		msg.EntityId = EntityId;
		msg.InventoryInfos.EntityId = EntityId;
		msg.InventoryItems.EntityId = EntityId;
		msg.InventoryInfos.MaxSize = 200;
		msg.InventoryItems.Items = _context.InventoryItems.ToArray();
		Send(msg);
	}

	private Equipments UpdateEquipments()
	{
		Equipments result = new Equipments
		{
			CurrentType = EquipSlotType.Slot1
		};
		EquipmentSlot value = default(EquipmentSlot);
		value.IsLocked = false;
		PlayerDisplay display = _context.AppearPlayer.Display;
		display.Body = display.DefaultBody;
		display.Head = null;
		display.BodyColor = new string[3] { "FFFFFF", "FFFFFF", "FFFFFF" };
		display.WeaponInfo = default(WeaponDisplayInfo);
		display.Equip = null;
		display.EquipColor = null;
		Dictionary<string, Item> dictionary = new Dictionary<string, Item>();
		foreach (KeyValuePair<string, string> pair in _context.EquippedItems)
		{
			int num = _context.InventoryItems.FindIndex((Item x) => x.Id == pair.Value);
			if (num < 0)
			{
				continue;
			}
			Item value2 = _context.InventoryItems[num];
			dictionary[pair.Key] = value2;
			string[] array = new string[3] { value2.ColorR, value2.ColorG, value2.ColorB };
			PerformanceYaml.Weapon weapon = PerformanceYaml.GetWeapon(value2.Prototype);
			if (weapon != null)
			{
				display.WeaponInfo = new WeaponDisplayInfo
				{
					WeaponFramework = weapon.WeaponFramework
				};
				display.Equip = weapon.Model;
				display.EquipColor = array;
			}
			bool flag = _context.AppearPlayer.IsMale();
			PerformanceYaml.Armor armor = PerformanceYaml.GetArmor(value2.Prototype);
			if (armor != null)
			{
				if (armor.Slot == "body")
				{
					display.Body = ((!flag) ? armor.FemaleModel : armor.MaleModel);
					display.BodyColor = array;
				}
				else if (armor.Slot == "head")
				{
					display.Head = ((!flag) ? armor.FemaleModel : armor.MaleModel);
					display.HeadColor = array;
				}
			}
		}
		_context.AppearPlayer.Display = display;
		value.ItemSlots = dictionary;
		value.UnlockSince = null;
		value.UnlockUntil = null;
		value.TitleId = string.Empty;
		result.Presets = new Dictionary<EquipSlotType, EquipmentSlot>();
		result.Presets[EquipSlotType.Slot1] = value;
		return result;
	}

	private void SendEquipments(uint replyOf = 0u)
	{
		Send(UpdateEquipments(), replyOf);
	}

	public void Process()
	{
		_connection.Process();
	}

	public void Stop()
	{
		_connection.Close();
	}

	public void Send<T>(T msg, uint replyOf = 0u)
	{
		_connection.Send(msg, replyOf);
	}
}
