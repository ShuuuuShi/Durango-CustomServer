using System;
using System.Collections.Generic;
using Durango.Network;
using Durango.Render.Particle;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using UnityEngine;
using Yaml;

public class AnimalManager : Singleton<AnimalManager>
{
	private readonly Dictionary<string, AnimalBehavior> _animals = new Dictionary<string, AnimalBehavior>();

	public event Action<AnimalBehavior> AnimalAppeared;

	public event Action<AnimalBehavior> AnimalDisappeared;

	private void Start()
	{
		Singleton<GameManager>.Instance().PreReconnect += GameManager_PreReconnect;
		GameSystem<GatheringSystem>.Instance().CollectiblePermissionChanged += GatheringSystem_CollectiblePermissionChanged;
		Connections.Frontend.On(delegate(AppearAnimal msg, PacketHeader header)
		{
			if (_animals.TryGetValue(msg.EntityId, out var value))
			{
				if (!(value == null))
				{
					value.Appear();
				}
				OnPostAppearAnimal(msg);
			}
			else
			{
				MakeAnimalObject(msg);
			}
		});
	}

	private void GatheringSystem_CollectiblePermissionChanged(string entityId, bool permission)
	{
		AnimalBehavior animal = GetAnimal(entityId);
		if (animal != null)
		{
			animal.IsLootable = permission;
		}
	}

	private void GameManager_PreReconnect()
	{
		StopAllCoroutines();
		AnimalBehavior[] array = new AnimalBehavior[_animals.Count];
		_animals.Values.CopyTo(array, 0);
		AnimalBehavior[] array2 = array;
		foreach (AnimalBehavior animalBehavior in array2)
		{
			UnityEngine.Object.Destroy(animalBehavior.gameObject);
		}
		_animals.Clear();
	}

	public AnimalBehavior GetAnimal(string id)
	{
		return _animals.Get(id);
	}

	private void PrepareLoad(string id)
	{
		_animals[id] = null;
	}

	private bool CheckPrepared(string id)
	{
		if (_animals.TryGetValue(id, out var value))
		{
			if (value == null)
			{
				_animals.Remove(id);
				return true;
			}
			return false;
		}
		return false;
	}

	[ExposedInEditor(null)]
	[UsedImplicitly]
	private void MakeAnimal(ushort type)
	{
		WorldPosition position = default(WorldPosition);
		position.SetFromClientPosition(PlayerBehavior.LocalPlayer.CurrentPosition);
		string entityId = (UnityEngine.Random.value * 1000000f).ToString();
		AppearAnimal msg = new AppearAnimal
		{
			EntityId = entityId,
			EntityType = type,
			IsAlive = true,
			Move = new Move
			{
				EntityId = entityId,
				Movements = new Movement[1]
				{
					new Movement
					{
						Path = new Location[1]
						{
							new Location
							{
								Position = position
							}
						}
					}
				}
			},
			Survival = new Survival
			{
				EntityId = entityId,
				Life = new Gauge(new GaugeNode[1]
				{
					new GaugeNode(0.0, 100f)
				}),
				Gauges = new Dictionary<string, Gauge>()
			},
			Display = new AnimalDisplay
			{
				EntityId = entityId,
				BaseScale = 1f
			}
		};
		MakeAnimalObject(msg);
	}

public void MakeAnimalObject(AppearAnimal msg)
		{
			PrepareLoad(msg.EntityId);
			string prefabPath = AnimalYaml.GetPrefabPath(msg.EntityType);
			if (string.IsNullOrEmpty(prefabPath))
			{
				// catalog ฝั่งเกมไม่มีชนิดนี้ — สัตว์จะหายเงียบถ้าไม่เตือน
				Debug.LogWarning("[AnimalManager] ไม่มี prefab ของ entityType=" + msg.EntityType + " id=" + msg.EntityId);
			}
			Singleton<AssetBundleManager>.Instance().RequestAsset(prefabPath, typeof(GameObject), delegate(UnityEngine.Object asset)
			{
				if (!CheckPrepared(msg.EntityId))
				{
					return;
				}
				if (asset == null)
				{
					Debug.LogWarning("[AnimalManager] โหลด prefab ไม่ได้ path='" + prefabPath + "' entityType=" + msg.EntityType + " id=" + msg.EntityId);
					return;
				}
				GameObject gameObject = UnityEngine.Object.Instantiate(asset, Vector3.zero, Quaternion.identity) as GameObject;
				if (gameObject == null)
				{
					return;
				}
				AnimalBehavior component = gameObject.GetComponent<AnimalBehavior>();
				Location location = PathMovable.GetLocation(msg.Move, Connections.Frontend.GetBufferedServerTime());
				component.CurrentPosition = location.Position.ToClientPosition();
				component.TurnToYaw(location.Yaw, bSnap: true);
				component.Floor.Value = location.Floor;
				component.EntityId = msg.EntityId;
				component.EntityTypeId = msg.EntityType;
				component.Level = msg.Level;
				component.Role = msg.Role;
				component.transform.localScale = new Vector3(msg.Display.BaseScale, msg.Display.BaseScale, msg.Display.BaseScale);
				component.SetAlive(msg.IsAlive, fromInit: true);
				component.Destroyed += Animal_Destroyed;
				_animals[msg.EntityId] = component;
				HandleMoveMsg(msg.Move);
				component.SetSurvivalGauge(msg.Survival.Life, msg.Survival.Gauges);
				string role = msg.Role;
				if (role != null && role == "warp_guard")
				{
					ParticleManager.EmitFollow("Particle/FX_Targeting_Common_02.prefab", Vector3.zero, Quaternion.identity, component.transform);
				}
				OnAppearAnimal(component);
				OnPostAppearAnimal(msg);
			});
		}

	private void Animal_Destroyed(AnimalBehavior animal)
	{
		OnDisappearAnimal(animal);
		_animals.Remove(animal.EntityId);
	}

	public bool HandleMoveMsg(Move msg)
	{
		AnimalBehavior animal = GetAnimal(msg.EntityId);
		if (animal != null)
		{
			animal.HandleMoveMsg(msg);
			return true;
		}
		return false;
	}

	public bool HandleDisappearMsg(DisappearEntity msg)
	{
		if (!_animals.ContainsKey(msg.EntityId))
		{
			return false;
		}
		AnimalBehavior animalBehavior = _animals[msg.EntityId];
		if (animalBehavior != null)
		{
			animalBehavior.Disappear();
		}
		else
		{
			_animals.Remove(msg.EntityId);
		}
		return true;
	}

	private void OnAppearAnimal(AnimalBehavior animal)
	{
		if (AnimalAppeared != null)
		{
			AnimalAppeared(animal);
		}
	}

	private void OnPostAppearAnimal(AppearAnimal msg)
	{
		GameSystem<GatheringSystem>.Instance().UpdateCollectibleDisplay(msg.EntityId, msg.Display.CollectibleDisplay);
	}

	private void OnDisappearAnimal(AnimalBehavior animal)
	{
		if (AnimalDisappeared != null)
		{
			AnimalDisappeared(animal);
		}
	}

	public void ForceAddAnimal(string id, AnimalBehavior animal)
	{
		_animals.Add(id, animal);
		animal.Destroyed += Animal_Destroyed;
	}
}
