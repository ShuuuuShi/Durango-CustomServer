# Sailing / Travel System Map

Verified in LastHuman Core + client + `data/` on 7 Sep 2026.  
**Sailing here means cross-region travel via `Emigrated` (2099), not free-sail simulation.**  
There is no open-water boat physics, no simulated voyage, and no mid-ocean world. A successful sail writes the destination into the player save, pushes `Emigrated`, and the client disconnects and reconnects into that region.

Do not treat `Depart` (2448), tutorial-boat messages, cargo warpholes, or warp-accelerator rifts as sailing.

Source of truth for this map: `server/Core/Player.cs`, `Player.Travel.cs`, `Player.Warp.cs`, `Player.S02.cs`, `Player.Inventory.cs`, `Support/RegionCatalog.cs`, `Support/WorldRegistry.cs`, `Core/GameServer.cs`, `client/ExploreSystem.cs`, `client/GameManager.cs`, `data/islands.json`, `data/config.json`.  
`server/GameCode/**` is protocol-only and was not edited for this document.

---

## What sailing is (and is not)

| Is sailing | Is not sailing |
|---|---|
| Touch a Port artifact → `GetRoutes` → pick a `RegionId` → `TravelByRegion` / `TravelByRegionInArchipelago` | Walking, `Move`, or `Depart` (start-of-stride notify) |
| `HandleTravelMsg` → save `region_id` → `OK` + `Emigrated{Type=Unknown}` → client closes frontend | Same-island `WarpToPort` / warphole `Warp` / `ReturnToHome` (`Teleported`, stay connected) |
| Next Auth/Ready lands in `WorldRegistry.GetOrCreate(RegionId)` | Tutorial boat / `DepartTutorial` (S02, Abort-only) |
| `SailingBack(null)` / `Withdraw` → home / default region (no travel history yet) | Cargo send, clan cargo occupy, warp-accelerator events |
| Route list from `RegionCatalog` (terrain zips + `region_templates.json`) | Free-sail / boat sim / ocean tiles as a travel mode |

Happy-path sequence:

```
Port (component "Port")
  → Touched.Interactions includes SailingRoutes (303)
  → ExploreGroup.Open(RouteType.Normal)
  → GetRoutes (2030) → Routes (2032)
  → GetRegion (2120) / GetArchipelago (2121) as the UI fills labels
  → TravelByRegion (2029) or TravelByRegionInArchipelago (2054)
  → HandleTravelMsg(regionId)
  → context.RegionId = target; Movements = null; Save()
  → OK (1231) + Emigrated (2099, TeleportType.Unknown)
  → GameManager.EmigratedReceived → EmigratedType.Explore → Frontend.Close()
  → knock / sessions / entry / Auth / Ready
  → GameServer.WorldOf(context) → Worlds.GetOrCreate(RegionId)
  → Player ctor uses GetEntryPosition() because Movements was cleared
```

---

## Layers T-A … T-L

### T-A — Client port UI

**Files:** `client/Durango.UI/ExploreGroup.cs`, `client/ExploreSystem.cs`, `server/Core/Player.cs` (touch interactions).

The client never decides that a dock is a port. It trusts `Touched.Interactions`. Core adds `Interaction.SailingRoutes` (303) when the artifact blueprint has component `"Port"` (`Player.cs` ~1260–1264). World placement of docks comes from each terrain’s `pois.yml` → `TerrainPois.PortPoints` → entity type 7001 (`World.cs` PlaceTerrainPois).

`ExploreGroup.Open` branches on `RouteType`:

| RouteType | Interaction | Client send |
|---|---|---|
| Normal | `SailingRoutes` (303) | `GetRoutes` |
| Shared | `SailingRoutesOfParty` (308) | `GetRoutesOfParty` |
| Neighbor | `SailingArchipelagoRegion` (307) | `GetRouteOfArchipelago` |

`TravelRegion` then sends `TravelByRegionInArchipelago` when `goesNeighbor` is true, else `TravelByRegion` (with optional `PartierId`). Price `null` is treated as free (`ExploreGroup` → `Money.ForFree`) and skips the pay dialog.

**Not wired from Core today:** `SailingRoutesOfParty` is never added to `Touched.Interactions` (only `SailingRoutes`). `GetRoutesOfParty` is a keep-alive empty `Routes` reply in `Player.Party.cs`.

### T-B — Route catalog

**Files:** `Player.cs` `HandleGetRoutesMsg` / `HandleGetRegionMsg` / `HandleGetArchipelagoMsg`, `Support/RegionCatalog.cs`, `Player.Travel.cs` `SendRoutesOfCurrentArchipelago`.

`RegionCatalog` is the live destination list. It is **not** `data/islands.json`. It scans `data/terrains/*.zip`, takes the filename as `RegionId` / `TerrainId`, and binds `region_template` from the zip to `data/assets/region_templates.json` (level / role / biome). Templates the client does not know are skipped so `ExploreSystem` will not drop the whole group.

`GetRoutes` answers `Routes` (2032):

- Nested map `Role → templateId → Route[]` for every *other* catalog region.
- `Route.Price = null` (free; no wallet yet).
- `ArchipelagoRoutes` grouped by `arch_{role}_{level}_{biome}` with `UnstableFactor = 1`, `PrerequisiteQuest = null`, `IsEpic = false`.

`GetRegion` returns the catalog `Region` or `Error { Text = "ไม่พบเกาะปลายทาง" }`.  
`GetArchipelago` lists included regions with `Progess = 100` (archipelago missions are not implemented; a lower value would lock later islands in the client UI).

`GetRouteOfArchipelago` is fire-and-forget on the client (global `On<RoutesOfArchipelago>`). Core replies with `ReplyOf = 0` using the same price/UF rules as `GetRoutes`.

### T-C — Travel dispatch (`HandleTravelMsg`)

**File:** `server/Core/Player.cs` (~2126–2148). Registered in the Player constructor (~261–272) and also reached from `Player.Travel.cs` group 3.

All real island moves share one function:

```
HandleTravelMsg(string regionId, uint seq)
  unknown id (not catalog, not personal_*, not PersonalRegionId)
    → Abort "ไม่พบเกาะปลายทาง"
  else
    → _context.RegionId = target   // null = default / home island
    → AppearPlayer.Move.Movements = null
    → Save()
    → OK (seq)
    → Emigrated { Type = TeleportType.Unknown }   // not Warp
```

`TeleportType.Unknown` is required: `GameManager.EmigratedReceived` maps it to `EmigratedType.Explore` (or `FromSafeHouse` if current role is Safehouse). Any other type becomes `EmigratedType.Warp` or Warp Rush.

| Ingress | TypeCode | Argument to `HandleTravelMsg` |
|---|---:|---|
| `TravelByRegion` | 2029 | `msg.RegionId` |
| `TravelByRegionInArchipelago` | 2054 | `msg.RegionId` |
| `SailingBack` | 3130 | `null` (home / default) |
| `TravelToStableRegion` | 20321235 | `msg.RegionId` |
| `Withdraw` | 2028 | `null` (same meaning as `SailingBack`) |
| `TravelToRandomPersonalRegion` | 20314 | a random catalog region with `Role.Personal`, or Abort |

`TravelByRegion` and `TravelByRegionInArchipelago` are the same server path today. Neighbor vs normal is a client UI distinction only.

### T-D — Persist + `Emigrated` reconnect

**Files:** `PlayerContext.region_id`, `GameServer.WorldOf`, `WorldRegistry.GetOrCreate`, `client/GameManager.cs` `EmigratedReceived`.

`PlayerContext.RegionId` (`json: region_id`) is LastHuman-only. The original single-world server had no such field. Empty/null means the registry default terrain (`WorldRegistry.DefaultRegionId`).

After `Emigrated`:

1. Client sets `GameManager.Emigrated` and `Connections.Frontend.Close()`.
2. Title flow reconnects (knock → sessions → entry → Auth → Ready).
3. `GameServer.WorldOf(playerContext)` → `Worlds.GetOrCreate(context.RegionId)`.
4. Unknown ids fall back to the default world. Personal ids (`personal_*`) resolve through `RegisterPersonalRegion` → template terrain file.
5. Per-region save: `offline/<cluster>/regions/<regionId>.world`.
6. `Player` constructor: if `Movements` is null, spawn at `GetEntryPosition()` of the destination world. Clearing movements in T-C is what prevents spawning at the old island’s coordinates (often ocean on the new map).

`Welcome.Region` is filled from the same `RegionId` (`GameServer.SendWelcome`). Terrain HTTP is `/terrains/<TerrainId>/…` (`Gateway.TerrainRoute`).

This is the entire “voyage.” There is no in-world boat ride between T-C and T-D.

### T-E — `SailingBack` + cost stub

**Files:** `Player.cs` ~269–276, `client/ExploreSystem.cs` ~188–202, `client/Durango.UI/ExploreGroup.cs` ~283–301, `Player.Warp.cs` `SendPoints`.

| Message | TypeCode | Core behavior |
|---|---:|---|
| `GetSailingBackCost` | 3110 | `SailingBackCost { Cost = 0L }` — stub, not a real fare |
| `SailingBackCost` | 3120 | reply payload |
| `SailingBack` | 3130 | `HandleTravelMsg(null)` — **home / default region**, not “previous island” |

Comments in Core state there is no travel history, so “back” cannot mean the last visited region.

**UI trap:** `ExploreGroup` only sends `SailingBack` when `MapSystem.Points.LastReturnPoint` has a value. `SendPoints` always sets `LastReturnPoint = null` (and `CampPoint = null`, `DeathPoint = null`). The port `SailingBack` (309) menu therefore never reaches the handler until T1 exists. `Withdraw` (305) is the working “go home” control from an unstable island’s port.

### T-F — Same-island `WarpToPort`

**File:** `server/Core/Player.Warp.cs` `HandleWarpToPortMsg` (~190–199). Client: `MapSystem.WarpToPort` → `WarpToPort` (9081241).

This is **not** sailing:

1. Load current terrain `pois.yml` → `PortPoints`.
2. Empty list → `Abort "เกาะนี้ไม่มีท่าเรือ"`.
3. Else `BeginWarp(NearestTo(ports), …)` — `Timer` on the request seq, then `Teleported { Type = Returning }` with `ReplyOf = 0`.
4. Connection stays up. `region_id` does not change.

`ReturnToHome` (2100) uses the same `BeginWarp` / `FinishWarp` pair and is also same-island only. Cross-island home would need `Emigrated` and is explicitly out of scope in the Warp file header.

### T-G — Intra-island warpholes

**File:** `Player.Travel.cs` group 1 + `BeginTravelWarp` / `FinishTravelWarp`.

| Message | TypeCode | Status |
|---|---:|---|
| `GetWarpCosts` | 2106 | Live. Explored warphole tiles only, `Cost = 0`, `Prohibited = false` |
| `Warp` | 2108 | Live. Tile must be an explored `neutral_warphole` / `cargo_warphole_in` on this island |
| `IsWarpholeAvailable` | 3021 | Live. Blueprint + reach check, no owner check (terrain holes have no owner) |

`BeginTravelWarp` sends `TeleportType.Warp` so play-guide `WarpToDo` can complete. `BeginWarp` in `Player.Warp.cs` is locked to `Returning` (home/port) and must not be reused here.

Still same island, still `Teleported`, not `Emigrated`.

### T-H — Island catalog vs `islands.json` vs Features

Two catalogs exist. Only one drives sailing.

| Source | Role | Used by `HandleTravelMsg`? |
|---|---|---|
| `RegionCatalog` ← `data/terrains/*.zip` + `region_templates.json` | Live destinations, routes, Welcome region | **Yes** |
| `data/islands.json` | Admin / ops list of 5 named isles (`isle01`…`isle05`) with Host / GatewayPort / GamePort | **No** — `/admin/islands` read/write only |
| `data/islands/isleXX/config.json` | Per-process feature overlay if that isle is launched as its own server | Not read by travel handlers |

`data/islands.json` (verified):

| Id | Name | Terrain | IslandTravel in that isle’s `config.json` |
|---|---|---|---|
| isle01 | เกาะเริ่มต้น | `ri35te` | **true** |
| isle02 | เกาะหิมะ | `sn20snow` | **true** |
| isle03 | เกาะป่าร้อนชื้น | `tr40tropic` | false |
| isle04 | เกาะทะเลทราย | `de50desert` | false |
| isle05 | เกาะภูเขาไฟ | `vo60volcan` | false |

Default `data/config.json` → `Features`:

- `IslandTravel`: **true**
- `Cargo`: **false**
- `WarpAccelerator`: **false**
- `Archipelago`: **false**
- `Wallet`: **false**

`config-meta.json` still describes `IslandTravel` as “code exists, not yet tested” with default `v: false`. The live default config has it **true**.

**Callout:** no `FeatureConfig` / `Features.IslandTravel` reader exists under `server/Core` or `server/Support`. Travel handlers do not gate on the flag. The flag is admin/docs intent, not a runtime switch. Same for `Cargo` / `WarpAccelerator`: those systems are disabled by missing implementation (Abort / empty replies), not by reading the JSON flag.

`GetIslandTravelOptions` (2130) / `IslandTravelOptions` exist only as protocol structs. No Core `Recv` handler. No client C# send site (the protocol-coverage mention of `MapSystem.cs` does not match current client sources). `TravelByRegionTemplate` (2031) is the same: protocol only, no handler.

### T-I — Abort stubs (`Player.Travel.cs` group 4)

These are registered, answer immediately, and do **not** call `HandleTravelMsg`. Every `Abort` has a non-null Thai `Text` (bare `default(Abort)` crashes the client in `LimitText`).

| Message | TypeCode | Abort text (verbatim) | Why stub |
|---|---:|---|---|
| `GetWarpBackCost` | 2109 | ยังไม่เปิดใช้งานการวาร์ปกลับเกาะเดิม | Must not send empty `WarpCosts` — client indexes `Costs[0]` |
| `WarpBack` | 2110 | ยังไม่มีเกาะที่วาร์ปกลับได้ — ใช้ท่าเรือเดินทางแทน | No previous-island history; `LastReturnPoint` is always null |
| `OpenMap` | 915 | ยังไม่เปิดใช้งานการซื้อแผนที่ | Would either grant all POIs for free or lie about purchase |
| `ActivePersonalRegionWarphole` | 3022 | ยังไม่เปิดใช้งานระบบเกาะส่วนตัว | Estate / personal warphole not live |
| `WarpToUrbanRegion` | 3024 | ยังไม่เปิดใช้งานระบบที่ดินบนเกาะเมือง | Urban estate not live |
| `WarpToNextArchipelagoRegion` | 2035 | ยังไม่เปิดใช้งานภารกิจหมู่เกาะ — ใช้ท่าเรือเดินทางแทน | `Archipelago.Progess` is hardcoded 100; no real next island |
| `GetWarpCostToNextRegion` | 12033 | ยังไม่เปิดใช้งานภารกิจหมู่เกาะ | Same; empty `WarpCosts` would silence the button |
| `GetWarpAcceleratorCost` | 21112519 | ยังไม่เปิดใช้งานกิจกรรมเร่งวาร์ป | Never sends `ArtifactState.Warpaccelerator` |
| `ReturnToCamp` | 3462987 | ยังไม่เปิดใช้งานระบบแคมป์ | `Points.CampPoint` is always null |

`WarpToPersonalRegion` (3023) is **not** an Abort in this file: it forwards to `HandleReturnToEstate(PersonalPlayer)` in `Player.PersonalRegion.cs` / `Player.Estate.cs` (that path can still `Emigrated` if a personal region exists).

`RecommendPersonalRegion` (3002) is handled (create/select personal terrain), not Abort.

Warp-style stubs must reply on the request seq so `MapSystem.TryWarp` `.Rest` stops the warp bar. Silent ignore leaves the character in the warp pose.

### T-J — Tutorial boat (S02) — separate from free travel

**File:** `server/Core/Player.S02.cs`. Client: `TutorialIslandSystem.cs`.

This is a crafted raft on the tutorial island (`AppearTutorialBoat` + `TutorialBoatSessions`). Core **never sends** either push (`grep AppearTutorialBoat` under `server/` is protocol-only). Without those, the client has no `_tutorialBoat` and does not offer the build/depart interactions in normal play.

| Message | TypeCode | Core behavior |
|---|---:|---|
| `ParticipateTutorialBoat` | 2303 | Abort — ยังเข้าร่วมต่อแพไม่ได้ |
| `PutMaterialsIntoTutorialBoat` | 2304 | Abort — ยังใส่วัสดุลงแพไม่ได้ (inventory not touched) |
| `DepartTutorial` | 2306 | Abort — ยังออกเรือจากเกาะบทเรียนไม่ได้ |

**Hard rule in S02:** do not reply `DepartTutorialReady` (2305). The client would fade to black and send `DepartTutorialFor` (2307), which has **no** Core handler — permanent black screen.

`DepartTutorial` can still arrive from the air-balloon delayed call in `EstateGroup` even when `MountAirBalloon` was aborted (`Player.Vehicle.cs`). The Abort on 2306 is what keeps that path safe.

S02 PvP / Warp Rush handlers in the same file are also Abort / empty lobby info. They are not sailing.

### T-K — Cargo + WarpAccelerator (flags false, handlers stub)

**Flags:** `Features.Cargo = false`, `Features.WarpAccelerator = false` in default `config.json` and every `islands/isleXX/config.json`.

**Cargo** (`Player.Inventory.cs` `RegisterUnimplementedItemHandlers`):

| Message | Reply |
|---|---|
| `GetCargoReceivers` | empty receivers, `CostPerSize = 0` (UI ForceClose) |
| `GetReceivedItems` | empty arrays |
| `SendCargo` | Abort ยังไม่มีระบบส่งของข้ามเกาะ |
| `ActivateCargoReceiver` | no-op |
| `OccupyCargoWarphole` | Abort ยังไม่มีระบบยึดครองตู้ขนส่ง |
| `SetCargoWarpholeTaxRate` | no-op |
| `CargoWarpholeTaxToClanFund` | Abort ยังไม่มีระบบแคลน |

Sending a fake `CargoReceiver` would delete items with nowhere to land.

**Warp accelerator:** `GetWarpAcceleratorCost` Abort (T-I). Vehicle-side participate/reward messages also Abort (`Player.Vehicle.cs`). Rift POIs may still be placed from `pois.yml` (`TerrainPois.Rifts`, blueprint `warp_accelerator`) but have no live event state.

Cargo warphole **tiles** can still be used as same-island warp destinations (T-G) when explored. That is a hole on this island, not cargo shipping.

### T-L — `Depart` no-op and other non-sail movement

**File:** `Player.cs` ~131–142.

`Depart` (2448) is the client’s “started walking” notify (`MoveMsgGenerator`). Core registers an **empty** handler:

- Must not reply (nothing on the client waits).
- Must not broadcast (no client `On<Depart>`).
- Must not clear rest (the game also fires `Depart` on stance changes, including sit-to-rest).

This is locomotion, not travel. Same bucket: `Dashed`, `Move`, `SetReturningPoint`, `Keepalive`.

Out of scope for sailing work: free-sail / boat physics, ocean as a region, `TravelByRegionTemplate` (2031, no handler), `GetIslandTravelOptions` (2130, no handler), Nexon-style population distributor, archipelago mission progress.

---

## Handler table

Status: **Live** = does the real job. **Stub** = replies a safe empty/zero/Abort. **No-op** = registered, no reply / no state. **Missing** = no Core `Recv`. **N/A** = server→client push.

| Message | TypeCode | File | Reply | Status | Layer |
|---|---:|---|---|---|---|
| `GetRoutes` | 2030 | `Player.cs` | `Routes` | Live (price null) | T-B |
| `GetRegion` | 2120 | `Player.cs` | `Region` / `Error` | Live | T-B |
| `GetArchipelago` | 2121 | `Player.cs` | `Archipelago` | Live (`Progess=100`) | T-B |
| `GetRouteOfArchipelago` | 20301 | `Player.Travel.cs` | `RoutesOfArchipelago` (ReplyOf=0) | Live | T-B |
| `GetRoutesOfParty` | 20300 | `Player.Party.cs` | empty `Routes` | Stub | T-A |
| `RecommendRegion` | 3001 | `Player.Travel.cs` | `Region` / `Error` | Live (pick existing) | T-B |
| `RecommendArchipelago` | 3012 | `Player.Travel.cs` | `Archipelago` | Live | T-B |
| `RecommendStableRegions` | 5792841 | `Player.Travel.cs` | `RecommendedStableRegions` | Live | T-B |
| `GetRegionMapInfo` | 205 | `Player.Travel.cs` | `RegionMapInfo` | Live | T-B |
| `GetPersonalRegionInfo` | 20420 | `Player.cs` | `PersonalRegionInfo` | Live / empty | T-A |
| `TravelByRegion` | 2029 | `Player.cs` | `OK` + `Emigrated` | **Live sail** | T-C |
| `TravelByRegionInArchipelago` | 2054 | `Player.cs` | `OK` + `Emigrated` | **Live sail** | T-C |
| `SailingBack` | 3130 | `Player.cs` | `OK` + `Emigrated` | Live, **null=home** | T-C / T-E |
| `Withdraw` | 2028 | `Player.Travel.cs` | `OK` + `Emigrated` | Live, **null=home** | T-C / T-E |
| `TravelToStableRegion` | 20321235 | `Player.Travel.cs` | `OK` + `Emigrated` | Live sail | T-C |
| `TravelToRandomPersonalRegion` | 20314 | `Player.Travel.cs` | sail or Abort | Live / Abort | T-C |
| `GetSailingBackCost` | 3110 | `Player.cs` | `SailingBackCost{Cost=0}` | **Stub** | T-E |
| `Emigrated` | 2099 | `Player.cs` (push) | — | N/A (closes client) | T-D |
| `WarpToPort` | 9081241 | `Player.Warp.cs` | `Timer` + `Teleported` | Live, same island | T-F |
| `ReturnToHome` | 2100 | `Player.Warp.cs` | `Timer` + `Teleported` | Live, same island | T-F |
| `SetAsHome` | 2102 | `Player.Warp.cs` | `OK` + `Points` | Live | T-F |
| `SetReturningPoint` | 2105 | `Player.Warp.cs` | `OK` + `Points` | Live | T-F |
| `GetWarpCosts` | 2106 | `Player.Travel.cs` | `WarpCosts` (0) | Live / free | T-G |
| `Warp` | 2108 | `Player.Travel.cs` | `Timer` + `Teleported` | Live, same island | T-G |
| `IsWarpholeAvailable` | 3021 | `Player.Travel.cs` | `OK` / Abort | Live | T-G |
| `GetWarpBackCost` | 2109 | `Player.Travel.cs` | Abort | Stub | T-I |
| `WarpBack` | 2110 | `Player.Travel.cs` | Abort | Stub | T-I |
| `OpenMap` | 915 | `Player.Travel.cs` | Abort | Stub | T-I |
| `ActivePersonalRegionWarphole` | 3022 | `Player.Travel.cs` | Abort | Stub | T-I |
| `WarpToPersonalRegion` | 3023 | `Player.Travel.cs` | estate path | Live / estate | T-I |
| `WarpToUrbanRegion` | 3024 | `Player.Travel.cs` | Abort | Stub | T-I |
| `WarpToNextArchipelagoRegion` | 2035 | `Player.Travel.cs` | Abort | Stub | T-I |
| `GetWarpCostToNextRegion` | 12033 | `Player.Travel.cs` | Abort | Stub | T-I |
| `GetWarpAcceleratorCost` | 21112519 | `Player.Travel.cs` | Abort | Stub | T-I / T-K |
| `ReturnToCamp` | 3462987 | `Player.Travel.cs` | Abort | Stub | T-I |
| `RecommendPersonalRegion` | 3002 | `Player.Travel.cs` | personal region | Live / personal | T-C |
| `ParticipateTutorialBoat` | 2303 | `Player.S02.cs` | Abort | Stub | T-J |
| `PutMaterialsIntoTutorialBoat` | 2304 | `Player.S02.cs` | Abort | Stub | T-J |
| `DepartTutorial` | 2306 | `Player.S02.cs` | Abort | Stub | T-J |
| `DepartTutorialReady` | 2305 | — | — | **Missing (must stay missing)** | T-J |
| `DepartTutorialFor` | 2307 | — | — | **Missing** | T-J |
| `GetIslandTravelOptions` | 2130 | — | — | **Missing** | T-H |
| `TravelByRegionTemplate` | 2031 | — | — | **Missing** | T-L |
| `SendCargo` / cargo family | various | `Player.Inventory.cs` | Abort / empty | Stub | T-K |
| `Depart` | 2448 | `Player.cs` | none | **No-op** | T-L |

Registration order (`Player.Systems.cs`): `RegisterWarpHandlers` → `RegisterS02Handlers` → `RegisterTravelHandlers`. Constructor-level travel `Recv`s in `Player.cs` are the sail core (T-C / T-E).

---

## Stub callouts

1. **`GetSailingBackCost` → `Cost = 0`**  
   Not “sailing is free by design” as a finished economy. `Wallet` is false and there is no t_stone debit. Same pattern as `Route.Price = null` and warphole `Cost = 0`.

2. **`SailingBack(null)` is home, not history**  
   Same as `Withdraw`. There is no previous-`RegionId` field. Comment at `Player.cs` ~271: no travel log, so “previous island” is unknown.

3. **`LastReturnPoint` is always null**  
   Port UI `SailingBack` (309) never sends 3130. The handler is live but unreachable from the intended menu until history exists (T1).

4. **`Depart` is an empty body**  
   Do not “fix” it by answering or clearing rest. It is not a travel handshake.

5. **Tutorial boat is a different product**  
   Do not route `DepartTutorial` into `HandleTravelMsg`. Do not invent `AppearTutorialBoat`. Do not send `DepartTutorialReady`.

6. **`GetIslandTravelOptions` / `TravelByRegionTemplate` have no Core handler**  
   Live sailing does not need them. `GetRoutes` + `TravelByRegion` is the working pair.

7. **`islands.json` does not authorize destinations**  
   `HandleTravelMsg` checks `RegionCatalog` / personal ids only. Admin isle flags (`IslandTravel` true/false per isle03–05) are not enforced in Core.

8. **Features flags are not runtime gates**  
   `IslandTravel=true` / `Cargo=false` / `WarpAccelerator=false` describe intent. Travel works because handlers exist, not because a flag is read.

9. **Abort must have `Text`**  
   `default(Abort)` / `default(Error)` → client NRE in `LimitText`. All travel stubs already follow this.

10. **`Emigrated` type must stay `Unknown` for boats**  
    `Warp` / `Returning` / `Season2` select the wrong `EmigratedType` and the wrong title reconnect flavor.

11. **Cargo / accelerator must not be faked**  
    Fake `CargoReceiver` loses items. Fake accelerator `Cost = 0` opens a pay sheet with no complete path.

---

## Next slices (T1–T2 only)

Do not start cargo, accelerator, tutorial boat, free-sail, or archipelago missions from this map. Those are later systems with their own blockers (wallet, estate, S02 world setup).

### T1 — Travel history so “back” means back

**Why this is first:** `SailingBack` / `Withdraw` / `WarpBack` / `GetWarpBackCost` / `Points.LastReturnPoint` all collapse to “no previous island.” The sail *forward* path (T-C/T-D) already works. The broken product promise is “return.”

**In scope:**

- Persist last stable (or last) `RegionId` + tile on successful `HandleTravelMsg`.
- `SailingBack` / `Withdraw` pass that id into `HandleTravelMsg` instead of `null`, falling back to default only when unset.
- `SendPoints.LastReturnPoint` so ExploreGroup 309 can open the confirm and send 3130.
- Keep `GetSailingBackCost = 0` until T2 (do not invent a fare).

**Out of scope for T1:** charging t_stone, cargo, camp, urban warp, archipelago next-region.

**Done when:** sail A→B, use port SailingBack (or Withdraw), land on A’s entry/port; `LastReturnPoint` non-null on B; still `Emigrated{Unknown}` (not `Teleported`).

### T2 — Real sailing fare (after wallet)

**Why second:** every live price is already a zero/null stub (`GetSailingBackCost`, `Route.Price`, warphole `GetWarpCosts`). Inventing numbers without `Features.Wallet` / t_stone would be a fake economy. T1 must land first so a charged `SailingBack` has a real destination.

**In scope:**

- Debit the same currency the client already shows (`Currency.TStone` on the SailingBack confirm; `Route.Price` on the route list).
- Reject with Abort (non-null Text) when the player cannot pay. Do not send `Emigrated` on failed pay.
- Replace `GetSailingBackCost { Cost = 0 }` with the real figure. Never send empty `WarpCosts` for related queries.

**Out of scope for T2:** cargo tax, accelerator buy-in, `OpenMap` vouchers, urban/personal warp fees.

**Done when:** `Route.Price` non-null on a paid route, confirm dialog shows that amount, success deducts and still takes T-C/T-D, failure Aborts and the player stays on the current island.

---

## File index

| Path | Role |
|---|---|
| `server/Core/Player.cs` | Port interaction, GetRoutes/GetRegion/GetArchipelago, sail Recv, `HandleTravelMsg`, Depart no-op, GetSailingBackCost |
| `server/Core/Player.Travel.cs` | Warpholes, recommend/routes-of-archipelago, extra sail ingress, Abort stubs |
| `server/Core/Player.Warp.cs` | Home / WarpToPort / Points |
| `server/Core/Player.S02.cs` | Tutorial boat + S02 PvP (Abort) |
| `server/Core/Player.Inventory.cs` | Cargo stubs |
| `server/Core/Player.Party.cs` | GetRoutesOfParty empty |
| `server/Core/Player.PersonalRegion.cs` | Personal `Emigrated` path |
| `server/Core/PlayerContext.cs` | `region_id` / `personal_region_id` |
| `server/Core/GameServer.cs` | `WorldOf` + Welcome region |
| `server/Support/WorldRegistry.cs` | Lazy worlds, default fallback |
| `server/Support/RegionCatalog.cs` | Live destination catalog |
| `server/Support/TerrainPois.cs` | Port / warphole / rift points |
| `server/data/islands.json` | Admin 5-isle table (not the sail catalog) |
| `server/data/config.json` | `Features.IslandTravel/Cargo/WarpAccelerator` |
| `client/ExploreSystem.cs` | Travel / SailingBack / GetRoutes sends |
| `client/Durango.UI/ExploreGroup.cs` | Port menus |
| `client/GameManager.cs` | `EmigratedReceived` → close frontend |
| `client/MapSystem.cs` | WarpToPort / TryWarp Timer contract |

Related: `docs/protocol-coverage.md` (แผนที่/เดินทาง), `docs/ROADMAP.md` (หลาย region + ล่องเรือ).
