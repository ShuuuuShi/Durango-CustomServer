# Building system map — Occupy / Build / Capsulate / Destruct / Estate

Verified against server source on 7 Sep 2026. **Docs only.** Do not treat this file as permission to change `server/GameCode/**`.

Sources (read, not guessed):

| File | Role |
|---|---|
| `server/Core/Player.Building.cs` | Occupy → PutMaterials → Estimate → Build → Complete → Capsulate → Place |
| `server/Core/Player.cs` | `HandleDestructMsg`, `GetArtifactBlueprints`, `ExtendFloor`, `GetEstateLicenses` |
| `server/Core/Player.Estate.cs` | Estate `Recv` wiring |
| `server/Core/Player.PersonalRegion.cs` | Estate world changes + warp |
| `server/Support/BuildTuning.cs` | Reads `constants.json` → `build` |
| `server/Support/ArtifactFloorTuning.cs` | Reads `constants.json` → `artifact_floor.max_stories` |
| `server/Support/BlueprintStore.cs` | Merges `building/blueprints.json` × `entity_types/artifact` |
| `server/data/assets/constants.json` | `build` block + `artifact_floor` |
| `server/data/assets/building/blueprints.json` | 556 blueprints |
| `server/data/assets/costs.json` | `estate.expanding_cost` / `estate.extending_cost` (unread by estate handlers) |
| `server/Core/Player.Wallet.cs` | T-stone wallet exists (`TrySpendTStone`) but no build/estate path calls it |

Status words in the tables:

| Status | Meaning |
|---|---|
| **works** | Implements the intended world change and replies with the type the client waits for |
| **stub** | Handler exists and unblocks the client, but skips a required cost, timer, or formula (or no-ops) |
| **abort** | Always (or on the only implemented path) replies `Abort` — feature not stored / not applied |

---

## 1. Player-facing flow (what the client actually sends)

```
OccupyArtifactSite (2057)
    → Timer + Occupied
GetArtifact (2018)
    → ArtifactMaterials          (open the material window)
PutMaterialsIntoArtifact (2092)  (repeatable)
    → OK + ArtifactMaterials
EstimateBuild (2414)             (preview while picking items)
    → BuildEstimation
BuildArtifact (2090)
    → Timer + ArtifactBuilt      (state Occupied → Built + Postprocess)
CompleteArtifact (2094)          (after Postprocess.EndsAt)
    → ArtifactCompleted          (state Built → Completed)

GetCapsulatingCost (4022)        (always before pack)
    → Cost                       ⚠️ Amount is hardcoded 0
CapsulateArtifact (4020)
    → Timer + inventory capsule + ArtifactCapsulated
PlaceCapsulatedArtifact (4021)
    → Timer + ArtifactPlaced

DestructArtifact (2051)
    → client waits for Destructing (2007)
    → server deletes immediately and never sends Destructing
```

Owner-only touch (`MayTouchArtifact`) applies to Put / Build / Complete / Capsulate. Occupy is reach + overlap + per-player occupied-site cap (8). Destruct uses the same owner check.

---

## 2. Handler table

### 2.1 Occupy / materials / build / complete

| Message | TypeCode | Handler | Reply the client waits for | Status | What it actually does |
|---|---:|---|---|---|---|
| `OccupyArtifactSite` | 2057 | `HandleOccupyArtifactSiteMsg` | `Timer` then `Occupied` (same seq, wrapped in `ReplySequenceMark`) | **works** | Validates blueprint, reach, overlap, occupied-site cap. Creates `BuildingState.Occupied`. Spends `build.site_selection.energy`. Duration from `build.site_selection.duration`, clamped to 120s. |
| `GetArtifact` | 2018 | `HandleGetArtifactMsg` | `ArtifactMaterials` | **works** | Returns stored slot → `Item[]`. Abort if entity missing. |
| `PutMaterialsIntoArtifact` | 2092 | `HandlePutMaterialsIntoArtifactMsg` | `OK` | **works** | Slot filters match crafting (`required_tags` OR within group, AND across groups). Capacity includes already-inserted items and `size_factor`. Removes items from inventory, writes looks, saves world. |
| `EstimateBuild` | 2414 | `HandleEstimateBuildMsg` | `BuildEstimation` | **works** | Level = average of stored + pending material levels, clamped to blueprint min/max. Durability = `build.default_durability` (7). Tags from `WorkbenchTags` only. `UnrevealedRareTagCount` always 0. |
| `BuildArtifact` | 2090 | `HandleBuildArtifactMsg` | `Timer` then `ArtifactBuilt` | **works** | Requires Occupied + tool tags + every slot filled. Energy from `build.building.energy` (`"1"`), **not** `blueprints.json` `energy`. Timer reuses **site_selection.duration** (commented as our choice — file has no separate build-time). Sets `Built` + `Postprocess` from blueprint `postprocess_time` / `postprocess_helper_max`. |
| `CompleteArtifact` | 2094 | `HandleCompleteArtifactMsg` | `ArtifactCompleted` | **works** | Requires `Built` and `Postprocess.EndsAt` elapsed. Clears stored materials. Grants construct EXP (`SkillTuning.BuildWeight`). |

Hard caps in this file (our values, not from JSON): `MaxBuildSeconds = 120`, `MaxVariableSide = 32`, `MaxSlotItems = 200`, `MaxOccupiedSitesPerPlayer = 8`. Stories/floor clamped with `artifact_floor.max_stories` (3).

Intentionally not done here (comments in `Player.Building.cs`):

- `EnergyWarning` (3648) — not sent for build (same gap as craft)
- Time-decay durability — every new site uses a flat full gauge (`FullDurability`)
- `HelpPostprocess` — no handler (would need “others may touch this site”)
- `RemodelArtifact` / `PackArtifact` — no `Recv`

### 2.2 Capsulate / place — T-stone stub

| Message | TypeCode | Handler | Reply | Status | What it actually does |
|---|---:|---|---|---|---|
| `GetCapsulatingCost` | 4022 | `HandleGetCapsulatingCostMsg` | `Cost` | **stub** | **Always** `Currency.TStone`, **`Amount = 0`**. Does not read `build.capsulating.cost`. Does not look up the artifact. Does not call the wallet. |
| `CapsulateArtifact` | 4020 | `HandleCapsulateArtifactMsg` | `Timer` | **works** (pack) / **stub** (pay) | Packs a completed, capsulizable, non-permanent building into `artifact_capsule` (`Item.Ext = ArtifactCapsule`). Blocks mannequin / stored-chest items. Timer = `build.capsulating.capsulating_time.default` (0.5s). **Does not deduct T-stone.** |
| `PlaceCapsulatedArtifact` | 4021 | `HandlePlaceCapsulatedArtifactMsg` | `Timer` | **works** | Re-creates a **new** entity id from the capsule, Completed immediately. Overlap + reach checks. Timer = `build.capsulating.placing_time` (0.5s). |

Client path (must answer `GetCapsulatingCost` or the pack confirm never appears):

`client/Durango.Logic.Interactions/ArtifactInteractions.cs` Capsulate → `Send(GetCapsulatingCost).On<Cost>(ShowPayConfirm → DoCapsulateArtifact)`.

#### T-stone stub (called out)

`HandleGetCapsulatingCostMsg` is a one-liner:

```csharp
Send(new Cost { Currency = Shared.Economy.Currency.TStone, Amount = 0L }, seq);
```

Official formula in `constants.json`:

```json
"capsulating": {
  "cost": { "inside": "0", "outside": "t_stone_reference * level" }
}
```

`BuildTuning` does **not** load `capsulating.cost`. The formula cannot be evaluated from extracted data anyway: there is **no numeric `t_stone_reference` key** in `server/data/assets/**` (only the token inside other formulas).

The comment in `Player.Building.cs` (“server has no money”) is **stale**. `Player.Wallet.cs` already has unpaid T-stone + `TrySpendTStone` / `AddTStone`. Grep of `server/` shows **no caller** of `TrySpendTStone` outside its definition. Capsulate, estate extend, and visit all skip payment.

Effect today: pack confirm shows **free**, pack always succeeds on inventory/space/rules, wallet never moves.

### 2.3 Destruct

| Message | TypeCode | Handler | Reply the client waits for | Status | What it actually does |
|---|---:|---|---|---|---|
| `DestructArtifact` | 2051 | `HandleDestructMsg` in `Player.cs` | `Destructing` (2007) `{ Duration, ToolType }` | **works** (delete) / **stub** (pacing) | Owner check. Abort (no seq) if the building still has chest items. Then `_world.DestructArtifact` **immediately**. Nearby clients get `DisappearEntity`. |

Verified negatives:

- Handler signature is `HandleDestructMsg(DestructArtifact msg)` — **`header.Seq` is discarded**.
- No `Send(new Destructing …)`.
- No `SpendBuildEnergy`.
- `BuildTuning` documents `build.destruct` but **never reads** `destruct.energy` or `destruct.time`.
- `BuildTuning.CancelTime` is loaded from `build.cancel_time` and **never referenced** by any handler.

Official formulas (unused):

```json
"destruct": { "energy": "10 + durability / 2.", "time": "5 + durability / 10." }
```

Client (`ArtifactInteractions.SendDestructArtifact`) does `.On<Destructing>` and starts a destroy gauge + motion from `Duration` / `ToolType`. Because the server never sends `Destructing`, that gauge/motion path does not run. The building still vanishes via `DisappearEntity`.

Durability used by the unused formula is itself a stub: sites are created with a flat 1.0 gauge; nothing in Core/Support writes `States.Durability` down over time (`Player.Repair.cs` documents this).

### 2.4 Estate

Registration is `Player.Estate.cs`. Logic lives in `Player.PersonalRegion.cs` (and `GetEstateLicenses` in `Player.cs`). World persistence: `World.cs` `Estates` / `EstateCells` (4×4 tile cells).

| Message | TypeCode | Handler | Client wait | Status | Cost deducted? |
|---|---:|---|---|---|---|
| `DeclareEstate` | 2422 | `HandleDeclareEstate` | `EstateLicense` | **works** | No. `costs.json` `estate.expanding_cost` unused. PersonalPlayer requires standing on own personal island. Clan types Abort. |
| `ExpandEstate` | 2421 | `HandleExpandEstate` | `EstateLicense` | **works** | **No.** Cap `PersonalEstateMaxSize = 30` (our value). |
| `ShrinkEstate` | 2426 | `HandleShrinkEstate` | `EstateLicense` | **works** | n/a |
| `ExtendEstateActivation` | 3822 | `HandleExtendEstate` | `EstateLicense` | **works** (time) / **stub** (pay) | **`msg.Cost` ignored.** Adds 7 days free. `costs.json` `estate.extending_cost` unused. |
| `SetEstateLicense` | 2420 | `HandleSetEstateLicense` | `OK` | **works** (partial) | Persists `ForOthers` only. `ForFriends` / `ForClanMembers` dropped. |
| `SetArtifactAccess` | 987123450 | inline in `RegisterEstateHandlers` | success via `Packet.IsSuccess` | **abort** | Always `Abort { Text = "ยังไม่เปิดใช้งานการตั้งสิทธิ์ใช้สิ่งปลูกสร้าง" }`. No `ArtifactAccess` store in Core. |
| `VisitEstate` | 2104 | `HandleVisitEstate` | `Timer` then `Emigrated` | **works** (PersonalPlayer) / **abort** (other types) | **`msg.Cost` ignored.** |
| `ReturnToEstate` | 10190234 | `HandleReturnToEstate` | `Timer` then `Emigrated` | **works** (PersonalPlayer) / **abort** (other types) | No charge. |
| `RemoveEstate` | 9518234 | `HandleRemoveEstate` | none (fire-and-forget) | **works** | n/a — must not Abort (would toast). |
| `GetEstateLicenseById` | 879534 | inline | global `EstateLicense` | **works** | Missing id → **silence** (no Abort; client polls). `CycleEndsAt` not set, so client rarely sends this today. |
| `GetEstateLicenses` | 3820 | `Player.cs` → `BuildEstateLicenses()` | `EstateLicenses` | **works** | Read-only. |
| `KickVisitor` | 20424 | empty lambda | none | **stub** | No visitor tracking. Must not Abort (sent as a side effect of Block). |
| `SetPersonalRegionAdmission` | 20423 | `HandleSetPersonalRegionAdmission` | none | **works** | Saved on the player. Must not Abort (fired on popup hide). |

`SetArtifactAccess` is the only estate command that is a hard abort. Do not flip it to `OK` until per-artifact access is stored and `ArtifactState.Access` is actually sent (the UI tab is hidden until that field is set).

---

## 3. `constants.json` → `build` — used vs unused

`BuildTuning.EnsureLoaded` is the only reader of the `build` object.

| Key | In JSON | Loaded? | Used by a handler? |
|---|---|---|---|
| `build.site_selection.duration` | `"2 + (area * 1)"` | yes → `SiteDuration` | **yes** — Occupy timer; Build timer (reused) |
| `build.site_selection.energy` | `"1 + (area * 2)"` | yes → `SiteEnergy` | **yes** — Occupy `SpendBuildEnergy` |
| `build.building.energy` | `"1"` | yes → `BuildEnergy` | **yes** — Build (constant 1, not per-blueprint) |
| `build.default_durability` | `7` | yes | **yes** — Estimate only (display). Not written as decaying durability. |
| `build.cancel_time` | `3` | yes → `CancelTime` | **no** — property never read |
| `build.capsulating.capsulating_time.default` | `0.5` | yes | **yes** — Capsulate `Timer` |
| `build.capsulating.capsulating_time."0"|"2"|"4"` | `0.5` each | no | **no** — file values are identical; only `default` is read |
| `build.capsulating.placing_time` | `0.5` | yes | **yes** — Place `Timer` |
| `build.capsulating.cost.inside` | `"0"` | **no** | **no** — T-stone stub |
| `build.capsulating.cost.outside` | `"t_stone_reference * level"` | **no** | **no** — T-stone stub |
| `build.destruct.energy` | `"10 + durability / 2."` | **no** | **no** — Destruct is instant |
| `build.destruct.time` | `"5 + durability / 10."` | **no** | **no** — Destruct is instant |
| `build.default_time_limited_durability` | `60` | no | no |
| `build.postprocess_helpable_count` | `3` | no | no — blueprint `postprocess_helper_max` is used instead; `HelpPostprocess` has no handler |
| `build.modular.default_door_item` | `["door_01_none", 1]` | no | no |
| `build.ruin_destruct_reward` | `3` | no | no |
| `build.part_durability_factor` | `1` | no | no |
| `build.condition_scales` | `3/2/1/0 → 0/0.1/0.4/1` | no | no |

Related keys **outside** `build`:

| Key | Used? |
|---|---|
| `artifact_floor.max_stories` (3) | **yes** — `ArtifactFloorTuning` → Occupy stories/floor clamp |
| `artifact_floor.floorable_types` | no |
| `costs.json` `estate.expanding_cost` (`"0"` / `"2"`) | **no** |
| `costs.json` `estate.extending_cost` (`"0"` / `"2"`) | **no** |
| `costs.json` `artifact_extend_floor` | **no** — `HandleExtendFloorMsg` just calls `World.ExtendFloor` |
| `t_stone_reference` as a defined number | **missing from extracted assets** — token only |

---

## 4. Blueprint fields the build path uses

`BlueprintStore` merges 556 `blueprints.json` rows with artifact prototypes.

| Field | Used in Occupy/Build/Capsulate? |
|---|---|
| `min_level` / `max_level` | yes — site level + Estimate clamp |
| `slots[]` (`slot_id`, `count`, `size_factor`, `required_tags`, `required_materials`, `looks`, `default_look_tag`) | yes — Put / Build / looks |
| `tool_tags` | yes — Build |
| `postprocess_time` / `postprocess_helper_max` | yes — written onto `Postprocess` (helpers never join) |
| `default_look` | yes — fallback display part at Build |
| prototype `size` / `height` / `is_size_variable` / `rotatable_directions` | yes — Occupy / Place |
| prototype `components` (`Modular`, `Burnable`) | yes — stories + burn look suffix |
| prototype `permanent` / `capsulizable` | yes — Capsulate reject (101 / 560 prototypes are not capsulizable) |
| prototype `is_craft` | yes — `GetArtifactBlueprints` list only |
| `energy` | **loaded onto `MergedBlueprint.Energy`, never spent** — Build spends `build.building.energy` instead |
| `effort` | loaded on YAML class, **not copied** onto `MergedBlueprint` |
| `required_ability` / `required_ability_value` | loaded on YAML class, **not enforced** |

---

## 5. Adjacent messages (not in the five verbs, listed so B-slices do not trip on them)

| Message | TypeCode | Status |
|---|---:|---|
| `GetArtifactBlueprints` | 2012 | **works** — ids where `is_craft` |
| `ExtendFloor` | 25565 | **works** (geometry) / **stub** (no `artifact_extend_floor` charge) |
| `RemodelArtifact` | 2098 | **no Recv** |
| `PackArtifact` | 3772 | **no Recv** |
| `HelpPostprocess` | 2442 | **no Recv** |
| `RepairArtifact` | 2055 | **abort** — durability never decays; see `Player.Repair.cs` |

---

## 6. Recommended next slices (B1–B3 only — no code in this PR)

These are the smallest closed loops that match the gaps above. Do not start Remodel / Pack / Repair / HelpPostprocess in these slices.

### B1 — Destruct pacing

**Why first:** the building already deletes; the official contract is `Destructing` + `build.destruct`. Client already has the gauge/motion.

**Do:**

1. Load `build.destruct.energy` / `time` in `BuildTuning` (same pattern as site formulas; variable `durability`).
2. Reply `Destructing` on the request seq with that duration (and a real or documented `ToolType`).
3. Spend energy; delay the actual `World.DestructArtifact` until the timer elapses (same “mutate on recv, Send later” pattern as Occupy, **or** delay the world delete if you need the prop to stay until the gauge ends — pick one and document it).
4. Keep the stored-items Abort. Send Abort **on seq** so `.On<Destructing>` unblocks.

**Out of scope for B1:** decaying durability, ruin rewards, `cancel_time`.

### B2 — Capsulate T-stone (stop lying with Amount=0)

**Why second:** wallet + `TrySpendTStone` already exist; only the cost path is stubbed. Pack/place already work.

**Do:**

1. Define a documented `t_stone_reference` (data is missing — this will be **our value**, not a Nexon extract).
2. Evaluate `build.capsulating.cost.inside` / `outside` (need a real inside/outside test).
3. `GetCapsulatingCost` returns that amount (still `Currency.TStone`).
4. `CapsulateArtifact` calls `TrySpendTStone` and Aborts if the player cannot pay.

**Out of scope for B2:** warp/estate/travel formulas that also mention `t_stone_reference`.

### B3 — Estate money + per-artifact access

**Why third:** declare/expand/shrink/grids already persist. The remaining holes are “free forever” and one hard abort.

**Do:**

1. Charge `costs.json` `estate.expanding_cost` / `extending_cost` (keys `"0"` / `"2"` = `OwnerType`) through `TrySpendTStone`. Do not trust `msg.Cost` from the client.
2. Optionally charge `VisitEstate.Cost` the same way (or keep visit free and say so).
3. Replace `SetArtifactAccess` Abort with a stored `Access` on the artifact, echo it on `AppearArtifact` / state updates, reply success. Until that store exists, leave the Abort in place.

**Out of scope for B3:** `ForFriends` / `ForClanMembers` maps, KickVisitor, clan estates, `CycleStartsAt` / deposit.

---

## 7. What this PR is not

- No changes under `server/GameCode/**`.
- No handler code, no `BuildTuning` edits, no wallet wiring.
- B1–B3 are recommendations only.
