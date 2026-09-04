# โปรโตคอล — เกมยิงอะไร เซิร์ฟรับอะไรแล้ว

สร้างด้วย `python tools/scan-protocol.py --md` — อ่านจากซอร์สจริง ไม่ได้เดา

| | จำนวน |
|---|---:|
| message ทั้งหมดในโปรโตคอล | 852 |
| เซิร์ฟรับได้ตอนนี้ | 42 |
| ตัวเกมยิงออกมาจริง | 391 |
| ยังไม่มี handler | 358 |

## ต่อสู้/ล่า

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `ExitBattle` | 3496 | 1 | `client/CombatSystem.cs` |
| `Revive` | 2101 | 1 | `client/PlayerController.cs` |
| `UseBattleAction` | 3440 | 1 | `client/Durango.Logic.Combat/UsingAction.cs` |
| `ReviveImmediately` | 210201 | 1 | `client/PlayerController.cs` |

## สัตว์/เลี้ยง

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `AcceptPetRank` | 74016 | 1 | `client/PetManager.cs` |
| `RevertPetRank` | 74014 | 1 | `client/PetManager.cs` |
| `CancelPetTask` | 65103 | 1 | `client/PetManager.cs` |
| `GetPetInventory` | 49823 | 1 | `client/Durango.Logic.Item/Inventory.cs` |
| `PutInItemsIntoPet` | 806 | 1 | `client/InventorySystem.cs` |
| `TakeOutItemsFromPet` | 807 | 1 | `client/InventorySystem.cs` |
| `StartPetTask` | 65102 | 1 | `client/PetManager.cs` |
| `ReinifyPet` | 74013 | 1 | `client/PetManager.cs` |
| `GrazedPets` | 29912241 | 1 | `client/Durango.Online/Player.cs` |
| `PutItemsForDomestication` | 694352 | 1 | `client/PetManager.cs` |
| `GetPetsInfo` | 14198037 | 1 | `client/PetManager.cs` |
| `ReleasePet` | 74012 | 1 | `client/PetManager.cs` |
| `ResurrectPet` | 239187 | 1 | `client/PetManager.cs` |
| `GetPreviewPet` | 181120 | 1 | `client/PetManager.cs` |
| `FinishPetTask` | 65104 | 1 | `client/PetManager.cs` |
| `RenamePet` | 804 | 1 | `client/PetManager.cs` |
| `DiscoverAnimal` | 5002 | 1 | `client/MapSystem.cs` |
| `StartDomestication` | 694357 | 1 | `client/PetManager.cs` |
| `CancelDomestication` | 694354 | 1 | `client/PetManager.cs` |
| `FinishDomestication` | 694355 | 1 | `client/PetManager.cs` |
| `GrazePets` | 800000 | 1 | `client/PetManager.cs` |
| `DisappearPet` | 198246 | 1 | `client/Durango.Online/Player.cs` |
| `UsePetActiveSkill` | 800200 | 1 | `client/PetManager.cs` |
| `ReturnPet` | 808 | 1 | `client/PetManager.cs` |
| `SpawnPet` | 923570 | 1 | `client/PetManager.cs` |

## คราฟต์/สูตร

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `MakeSection` | 3686 | 1 | `client/InventorySystem.cs` |
| `Recipes` | 120 | 1 | `client/Durango.Online/Player.cs` |
| `ArtifactBlueprints` | 316 | 1 | `client/Durango.Online/Player.cs` |
| `SetBlueprintLike` | 3801 | 1 | `client/RecipeSystem.cs` |
| `SkipEntrustedCraft` | 7498153 | 1 | `client/CraftSystem.cs` |
| `MakeClan` | 3651 | 1 | `client/ClanSystem.cs` |
| `CancelCrafting` | 2023 | 1 | `client/CraftSystem.cs` |
| `MakeParty` | 20003 | 1 | `client/Durango.Logic/PartySystem.cs` |
| `SetRecipeLike` | 3800 | 1 | `client/RecipeSystem.cs` |

## สกิล/เลเวล

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `ToggleStatusEffect` | 2081 | 2 | `client/SleepChecker.cs` |
| `GetExploredPOIs` | 902 | 2 | `client/MapSystem.cs` |
| `GetQuestState` | 398132 | 2 | `client/Durango.Development/Commands.cs` |
| `EstateLicenses` | 3821 | 1 | `client/Durango.Online/Player.cs` |
| `VisitEstate` | 2104 | 1 | `client/EstateSystem.cs` |
| `ExplorePOI` | 908 | 1 | `client/Durango.Logic.Map/POIUpdater.cs` |
| `DeclareEstate` | 2422 | 1 | `client/EstateSystem.cs` |
| `CancelSkillCategoryResearch` | 36431 | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `GetExpectedCropBooster` | 37123 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetStatusEffects` | 2016 | 1 | `client/StatusEffectSystem.cs` |
| `Statistics` | 2040 | 1 | `client/Durango.Online/Player.cs` |
| `SkipSkillCategoryResearch` | 3643 | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `ReturnToEstate` | 10190234 | 1 | `client/EstateSystem.cs` |
| `SetEstateLicense` | 2420 | 1 | `client/EstateSystem.cs` |
| `GetStatistics` | 2039 | 1 | `client/StatisticsSystem.cs` |
| `RedrawActiveSkill` | 800102 | 1 | `client/PetManager.cs` |
| `RemoveEstate` | 9518234 | 1 | `client/EstateSystem.cs` |
| `ExpandEstate` | 2421 | 1 | `client/EstateSystem.cs` |
| `GetEstateLicenseById` | 879534 | 1 | `client/EstateSystem.cs` |
| `GetResistanceExpCaps` | 349378781 | 1 | `client/StatisticsSystem.cs` |
| `RequestClanStatusEffects` | 3704 | 1 | `client/ClanSystem.cs` |
| `DrawActiveSkill` | 800101 | 1 | `client/PetManager.cs` |
| `ResearchSkillCategory` | 2446 | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `GetSkills` | 2047 | 1 | `client/Durango.Logic/SkillSystem.cs` |
| `ExtendEstateActivation` | 3822 | 1 | `client/EstateSystem.cs` |
| `Skills` | 123 | 1 | `client/Durango.Online/Player.cs` |
| `ShrinkEstate` | 2426 | 1 | `client/EstateSystem.cs` |

## เอาชีวิตรอด

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `GetCheatFlags` | 2088 | 2 | `client/Durango.UI/CommandButtonGroup.cs` |
| `Weather` | 331 | 1 | `client/Durango.Online/World.cs` |
| `GetClanCreationCosts` | 3667 | 1 | `client/ClanSystem.cs` |
| `RemoveDeathPoint` | 2034 | 1 | `client/MapSystem.cs` |
| `GiveAttendanceReward` | 1097854 | 1 | `client/Durango.Logic/EventSystem.cs` |
| `DrinkWater` | 3492 | 1 | `client/InteractionSystem.cs` |
| `GiveAttendanceAppendix` | 1097855 | 1 | `client/Durango.Logic/EventSystem.cs` |

## เควส

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `GetMissions` | 3620 | 2 | `client/FactionSystem.cs` |
| `RecommendMissions` | 3621 | 2 | `client/FactionSystem.cs` |
| `GetQuestScoreInfos` | 237920 | 2 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestDumpedPersonalIsland` | 381922 | 2 | `client/Durango.Development/Commands.cs` |
| `GetSupportRequests` | 2347809 | 2 | `client/FactionSystem.cs` |
| `RequestNearestPOI` | 911 | 2 | `client/Durango.Development/Commands.cs` |
| `CancelMission` | 3624 | 2 | `client/FactionSystem.cs` |
| `RequestEpicWarp` | 77777 | 1 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestFullCountPOIsReward` | 9031 | 1 | `client/MapSystem.cs` |
| `RequestReturnerGuideAction` | 3450984 | 1 | `client/PlayGuideSystem.cs` |
| `AcceptMission` | 3623 | 1 | `client/FactionSystem.cs` |
| `RequestResetReformSlot` | 59145 | 1 | `client/TechSupportSystem.cs` |
| `RechargeMissionShuffleCount` | 3626 | 1 | `client/FactionSystem.cs` |
| `RecommendMissionImmediately` | 3629 | 1 | `client/FactionSystem.cs` |
| `QuestCategories` | 237902 | 1 | `client/Durango.Online/Player.cs` |
| `GetRecommendMissionCost` | 3628 | 1 | `client/FactionSystem.cs` |
| `InteractWithEpicNPC` | 3141593 | 1 | `client/ClientInteractionQuest.cs` |
| `RefuseFriendRequest` | 1451216 | 1 | `client/SocialSystem.cs` |
| `CustomQuestEvent` | 312798 | 1 | `client/Durango.Logic.PlayGuide/CustomCommand.cs` |
| `ShuffleMission` | 3627 | 1 | `client/FactionSystem.cs` |
| `CancelClanJoinRequest` | 1923487521 | 1 | `client/ClanSystem.cs` |
| `SkipTutorialMission` | 3633 | 1 | `client/FactionSystem.cs` |
| `AcceptFriendRequest` | 1451215 | 1 | `client/SocialSystem.cs` |
| `SetPersonalRegionAdmission` | 20423 | 1 | `client/Durango.UI.Popup/PersonalRegionAdmissionPopup.cs` |
| `RequestTechSupportEstimate` | 59141 | 1 | `client/TechSupportSystem.cs` |
| `SendFactionSupportRequest` | 725982 | 1 | `client/FactionSystem.cs` |
| `RequestQuestScoreReward` | 237925 | 1 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestQuestReward` | 237923 | 1 | `client/Durango.Logic/QuestSystem.cs` |
| `RequestFriend` | 1451212 | 1 | `client/SocialSystem.cs` |
| `RequestTechSupport` | 59144 | 1 | `client/CraftSystem.cs` |
| `CheckSequenceMissionCleared` | 3631 | 1 | `client/FactionSystem.cs` |
| `GetAttendanceRewards` | 1097852 | 1 | `client/Durango.Logic/EventSystem.cs` |
| `RequestArchipelagoRegionClear` | 240002 | 1 | `client/Durango.Logic/ArchipelagoMissionSystem.cs` |
| `CancelFriendRequest` | 1451220 | 1 | `client/SocialSystem.cs` |
| `RequestClanRewards` | 3706 | 1 | `client/ClanSystem.cs` |

## ของ/กระเป๋า

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `InventoryUpdated` | 113 | 11 | `client/Durango.Online/Player.cs` |
| `InventoryOrder` | 15 | 2 | `client/InventorySystem.cs` |
| `SendCargo` | 3814 | 1 | `client/CargoWarpholeSystem.cs` |
| `SetSectionItemOrder` | 3689 | 1 | `client/InventorySystem.cs` |
| `AddItemsToWarehouse` | 3690 | 1 | `client/InventorySystem.cs` |
| `GetInventory` | 2010 | 1 | `client/Durango.Logic.Item/Inventory.cs` |
| `PutInItem` | 2434 | 1 | `client/InventorySystem.cs` |
| `GetReceivedItems` | 3809 | 1 | `client/CargoWarpholeSystem.cs` |
| `TakeOutReinFromCage` | 694359 | 1 | `client/PetManager.cs` |
| `UseItemsForPioneerPoint` | 812234572 | 1 | `client/EstateSystem.cs` |
| `CargoWarpholeTaxToClanFund` | 3815 | 1 | `client/EstateSystem.cs` |
| `GetSectionItems` | 3692 | 1 | `client/InventorySystem.cs` |
| `OccupyCargoWarphole` | 3819 | 1 | `client/Durango.UI/CargoWarpholeGroup.cs` |
| `RepairItem` | 3717 | 1 | `client/RepairSystem.cs` |
| `LockOrUnlockItems` | 3497 | 1 | `client/InventorySystem.cs` |
| `UseItem` | 17 | 1 | `client/InventorySystem.cs` |
| `ChangeEquipSlotType` | 81534 | 1 | `client/EquipSystem.cs` |
| `PopItemsFromWarehouse` | 3691 | 1 | `client/InventorySystem.cs` |
| `ActivateCargoReceiver` | 3811 | 1 | `client/CargoWarpholeSystem.cs` |
| `SetCargoWarpholeTaxRate` | 3816 | 1 | `client/EstateSystem.cs` |
| `TakeOutFromCage` | 810 | 1 | `client/PetManager.cs` |
| `GetCargoReceivers` | 3812 | 1 | `client/CargoWarpholeSystem.cs` |
| `DeliverItems` | 3614 | 1 | `client/FactionSystem.cs` |
| `Dye` | 3666 | 1 | `client/CraftSystem.cs` |
| `MoveItemsInWarehouse` | 3685 | 1 | `client/InventorySystem.cs` |
| `CheckUnstableItem` | 1590123 | 1 | `client/MapSystem.cs` |

## สร้าง/ที่ดิน

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `PlaceCapsulatedArtifact` | 4021 | 1 | `client/BuildSystem.cs` |
| `SetArtifactAccess` | 987123450 | 1 | `client/EstateSystem.cs` |
| `CompleteArtifact` | 2094 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `CapsulateArtifact` | 4020 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `RepairArtifact` | 2055 | 1 | `client/RepairSystem.cs` |

## สังคม

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `AcceptUserMails` | 9786523 | 2 | `client/MailSystem.cs` |
| `RejectPartyInvitation` | 20007 | 2 | `client/Durango.Logic/PartySystem.cs` |
| `GetFactions` | 3600 | 1 | `client/FactionSystem.cs` |
| `SetClanEmblem` | 3695 | 1 | `client/ClanSystem.cs` |
| `DeleteUserMails` | 9786524 | 1 | `client/MailSystem.cs` |
| `RenameClan` | 36510 | 1 | `client/ClanSystem.cs` |
| `MarkUserMailsAsRead` | 98712436 | 1 | `client/MailSystem.cs` |
| `GetClanFund` | 3678 | 1 | `client/ClanSystem.cs` |
| `GetClanNotificationEnabled` | 4027 | 1 | `client/SocialSystem.cs` |
| `GetFactionDeliveryCondition` | 3612 | 1 | `client/FactionSystem.cs` |
| `ResubscribeClanChannel` | 24 | 1 | `client/SocialSystem.cs` |
| `LeaveParty` | 20008 | 1 | `client/Durango.Logic/PartySystem.cs` |
| `DropClanApplier` | 3659 | 1 | `client/ClanSystem.cs` |
| `GetRoutesOfParty` | 20300 | 1 | `client/ExploreSystem.cs` |
| `GetMyFriendType` | 78209743 | 1 | `client/SocialSystem.cs` |
| `JoinIntoParty` | 20006 | 1 | `client/Durango.Logic/PartySystem.cs` |
| `ActivateFaction` | 3610 | 1 | `client/PlayGuideSystem.cs` |
| `StartClanResearch` | 3702 | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `SendMail` | 2077 | 1 | `client/MailSystem.cs` |
| `ElectPartyLeader` | 20010 | 1 | `client/Durango.Logic/PartySystem.cs` |
| `RemoveFriend` | 1451217 | 1 | `client/SocialSystem.cs` |
| `InviteIntoParty` | 20004 | 1 | `client/Durango.Logic/PartySystem.cs` |
| `GetLatestChatLog` | 25 | 1 | `client/SocialSystem.cs` |
| `GetSocial` | 2402 | 1 | `client/SocialSystem.cs` |
| `MarkMailsAsRead` | 98712435 | 1 | `client/MailSystem.cs` |
| `SetFriendType` | 908134 | 1 | `client/SocialSystem.cs` |
| `KickClanMember` | 3661 | 1 | `client/ClanSystem.cs` |
| `SetClanMemberRole` | 3662 | 1 | `client/ClanSystem.cs` |
| `ReportFactionProp` | 3611 | 1 | `client/Durango.UI/MissionGroup.cs` |
| `KickPartyMember` | 20009 | 1 | `client/Durango.Logic/PartySystem.cs` |
| `DeleteMails` | 2076 | 1 | `client/MailSystem.cs` |
| `JoinClan` | 3655 | 1 | `client/ClanSystem.cs` |
| `ToggleClanNotification` | 4025 | 1 | `client/SocialSystem.cs` |
| `SetClanInfo` | 3699 | 1 | `client/ClanSystem.cs` |
| `AcceptMails` | 2075 | 1 | `client/MailSystem.cs` |
| `ApproveClanApplier` | 3657 | 1 | `client/ClanSystem.cs` |
| `DonateToClanFund` | 3679 | 1 | `client/Durango.UI/ClanInfoPage.cs` |
| `GetAvailableClanResearch` | 5987333 | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `InviteToClan` | 3660 | 1 | `client/ClanSystem.cs` |
| `GetClanResearch` | 5987341 | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `SetSocialOptions` | 24002 | 1 | `client/SocialSystem.cs` |
| `LeaveClan` | 3652 | 1 | `client/ClanSystem.cs` |
| `GetParty` | 20001 | 1 | `client/Durango.Logic/PartySystem.cs` |

## ตลาด/เงิน

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `AcceptPurchase` | 5247809 | 2 | `client/ShopSystem.cs` |
| `PurchaseCommodity` | 856710 | 1 | `client/ShopSystem.cs` |
| `PurchaseCommodityWithVoucher` | 841253 | 1 | `client/ShopSystem.cs` |
| `GetUserFirstPurchaseHistory` | 856720 | 1 | `client/ShopSystem.cs` |
| `Products` | 5100 | 1 | `client/Durango.Online/Player.cs` |
| `GetAcceptableSubPurchases` | 259674 | 1 | `client/ShopSystem.cs` |
| `GetPurchases` | 510397 | 1 | `client/ShopSystem.cs` |
| `MarketCollectAllPayments` | 5102 | 1 | `client/Durango.UI/MarketHistoryWidget.cs` |

## แผนที่/เดินทาง

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `GetWarpCosts` | 2106 | 2 | `client/Durango.UI/InteractionGroup.cs` |
| `TravelByRegion` | 2029 | 2 | `client/ExploreSystem.cs` |
| `Warp` | 2108 | 1 | `client/MapSystem.cs` |
| `GetWarpAcceleratorCost` | 21112519 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetDefoggedChunks` | 204 | 1 | `client/Durango.UI/MapContext.cs` |
| `WarpToPort` | 9081241 | 1 | `client/MapSystem.cs` |
| `RecommendStableRegions` | 5792841 | 1 | `client/MapSystem.cs` |
| `GetIslandTravelOptions` | 2130 | 1 | `client/MapSystem.cs` |
| `AddFavoriteRegionOwners` | 20011 | 1 | `client/SocialSystem.cs` |
| `WarpToPersonalRegion` | 3023 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `WarpToNextArchipelagoRegion` | 2035 | 1 | `client/ExploreSystem.cs` |
| `GetPersonalRegionInfo` | 20420 | 1 | `client/EstateSystem.cs` |
| `ActivePersonalRegionWarphole` | 3022 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `TravelToStableRegion` | 20321235 | 1 | `client/MapSystem.cs` |
| `IsWarpholeAvailable` | 3021 | 1 | `client/Durango.UI/WorldMapGroup.cs` |
| `RecommendRegion` | 3001 | 1 | `client/ExploreSystem.cs` |
| `GetRegion` | 2120 | 1 | `client/MapSystem.cs` |
| `RemoveSection` | 3687 | 1 | `client/InventorySystem.cs` |
| `RecommendPersonalRegion` | 3002 | 1 | `client/Durango.UI/EstateGroup.cs` |
| `OpenMap` | 915 | 1 | `client/MapSystem.cs` |
| `GetWarpBackCost` | 2109 | 1 | `client/MapSystem.cs` |
| `GetRegionMapInfo` | 205 | 1 | `client/Durango.UI/SharedMapContext.cs` |
| `RemoveFavoriteRegionOwners` | 20012 | 1 | `client/SocialSystem.cs` |
| `WarpBack` | 2110 | 1 | `client/MapSystem.cs` |
| `TravelByRegionInArchipelago` | 2054 | 1 | `client/ExploreSystem.cs` |
| `GetWarpCostToNextRegion` | 12033 | 1 | `client/Durango.Logic/ArchipelagoMissionSystem.cs` |
| `TravelToRandomPersonalRegion` | 20314 | 1 | `client/ExploreSystem.cs` |
| `WarpToUrbanRegion` | 3024 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |

## อื่น ๆ

| message | TypeCode | เกมเรียกกี่จุด | ไฟล์ตัวอย่างในเกม |
|---|---:|---:|---|
| `OK` | 1231 | 16 | `client/Durango.Online/GameServer.cs` |
| `Timer` | 1134 | 5 | `client/Durango.Online/Player.cs` |
| `Abort` | 1024 | 4 | `client/Durango.Online/Player.cs` |
| `TutorialEvent` | 701 | 4 | `client/Durango.Logic.PlayGuide/CustomCommand.cs` |
| `MountAirBalloon` | 123987 | 3 | `client/Durango.UI/EstateGroup.cs` |
| `S02Leave` | 222221 | 3 | `client/Durango.Logic/PvpIslandSystem.cs` |
| `Unmount` | 803 | 3 | `client/PetManager.cs` |
| `SetConcertMusic` | 63459080 | 2 | `client/MusicManager.cs` |
| `GetAdvisorTargets` | 3708 | 2 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `GetReturnerInfo` | 3450983 | 2 | `client/PlayGuideSystem.cs` |
| `Rename` | 324 | 2 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `RegisterConcert` | 63459082 | 2 | `client/MusicManager.cs` |
| `InviteToConversation` | 2411 | 2 | `client/SocialSystem.cs` |
| `GetPOICount` | 900 | 2 | `client/Durango.Logic.Map/POIUpdater.cs` |
| `ContactReactingProp` | 78452083 | 2 | `client/Durango.Logic.Interactions/ReactingPropInteractions.cs` |
| `PlaySharedMusic` | 47852451 | 1 | `client/MusicManager.cs` |
| `RepairImmediate` | 2056 | 1 | `client/RepairSystem.cs` |
| `SearchPOIs` | 904 | 1 | `client/InteractionSystem.cs` |
| `PutInCage` | 809 | 1 | `client/PetManager.cs` |
| `GetTitles` | 2044 | 1 | `client/StatisticsSystem.cs` |
| `GetEncyclopedia` | 37125 | 1 | `client/FarmingEncyclopediaSystem.cs` |
| `PickMilestone` | 800012 | 1 | `client/PetManager.cs` |
| `SuggestAlly` | 9138747 | 1 | `client/ClanSystem.cs` |
| `Sprinkle` | 37121 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `StartPersonalResearch` | 5987338 | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `GetAvailableTask` | 65106 | 1 | `client/PetManager.cs` |
| `KickVisitor` | 20424 | 1 | `client/SocialSystem.cs` |
| `SetResurrectionRewards` | 133 | 1 | `client/InventorySystem.cs` |
| `HostConcert` | 63459079 | 1 | `client/MusicManager.cs` |
| `ResetAccessory` | 9823460 | 1 | `client/EquipSystem.cs` |
| `GetPioneerGradeInfo` | 812234574 | 1 | `client/EstateSystem.cs` |
| `RecommendArchipelago` | 3012 | 1 | `client/ExploreSystem.cs` |
| `GetLastSearchedTime` | 906 | 1 | `client/InteractionSystem.cs` |
| `MountVehicle` | 327918 | 1 | `client/PetManager.cs` |
| `SetReturningPoint` | 2105 | 1 | `client/PlayerTriggerMakeCheckPoint.cs` |
| `ReceiveAdvisorReward` | 3908 | 1 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `GetDiscoveryInfo` | 5000 | 1 | `client/MapSystem.cs` |
| `Musics` | 47852454 | 1 | `client/Durango.Online/Player.cs` |
| `FindTargetEntityPosition` | 3950 | 1 | `client/Durango.UI/PlayGuideHelperGroupBase.cs` |
| `GetActions` | 314 | 1 | `client/CombatSystem.cs` |
| `Error` | 1022 | 1 | `client/Durango.Online/Player.cs` |
| `GetPunchMachineLeaderboard` | 785103 | 1 | `client/PunchingLeaderboardSystem.cs` |
| `GetArchipelago` | 2121 | 1 | `client/ExploreSystem.cs` |
| `GetTechSupportEstimates` | 59138 | 1 | `client/TechSupportSystem.cs` |
| `PutInReinsToCage` | 694351 | 1 | `client/PetManager.cs` |
| `Info` | 1023 | 1 | `client/Durango.Online/Player.cs` |
| `GetRechargeShuffleCost` | 3625 | 1 | `client/FactionSystem.cs` |
| `LookAroundMood` | 234789 | 1 | `client/InteractionSystem.cs` |
| `Unblock` | 4017 | 1 | `client/SocialSystem.cs` |
| `SelectTitle` | 2046 | 1 | `client/StatisticsSystem.cs` |
| `S02GetLobbyInfo` | 222214 | 1 | `client/Durango.Logic/WarpRushSystem.cs` |
| `RenameWarehouseSection` | 3696 | 1 | `client/InventorySystem.cs` |
| `FeedInCage` | 65101 | 1 | `client/PetManager.cs` |
| `ReceiveAcceleratorRewards` | 21112514 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `FireProjectileFromVehicle` | 203493 | 1 | `client/Durango.UI/CombatGroup.cs` |
| `Mount` | 802 | 1 | `client/PetManager.cs` |
| `BreakAlly` | 9138751 | 1 | `client/ClanSystem.cs` |
| `GetCapsulatingCost` | 4022 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetCommodities` | 856700 | 1 | `client/ShopSystem.cs` |
| `ParticipateTutorialBoat` | 2303 | 1 | `client/TutorialIslandSystem.cs` |
| `GetMilestoneCandidate` | 800010 | 1 | `client/PetManager.cs` |
| `S02EnqueueEntree` | 222201 | 1 | `client/Durango.Logic/WarpRushSystem.cs` |
| `GetNomadInfo` | 100000 | 1 | `client/PlayGuideSystem.cs` |
| `Follow` | 2401 | 1 | `client/SocialSystem.cs` |
| `MiniGameDanceStarted` | 4625401 | 1 | `client/Durango.UI/MiniGameDanceGroup.cs` |
| `PutMaterialsIntoTutorialBoat` | 2304 | 1 | `client/TutorialIslandSystem.cs` |
| `PublishMusic` | 47852557 | 1 | `client/MusicManager.cs` |
| `FinishConcert` | 63459101 | 1 | `client/MusicManager.cs` |
| `UnmountAirBalloon` | 135867 | 1 | `client/PetManager.cs` |
| `DeleteEngagementData` | 1444251 | 1 | `client/Durango.UI.Popup/EngagementConfigPopup.cs` |
| `EngagementAgreementChanged` | 1444250 | 1 | `client/Durango.Logic/EngagementSystem.cs` |
| `InvestToCrack` | 3663 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `CancelTargetTitle` | 3901 | 1 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `UseTamingAction` | 1900 | 1 | `client/Durango.Logic.Combat/UsingAction.cs` |
| `GetTargetTitle` | 3906 | 1 | `client/Durango.Logic/LearningGuideSystem.cs` |
| `RefuseSuggestion` | 9138750 | 1 | `client/ClanSystem.cs` |
| `Unfollow` | 2410 | 1 | `client/SocialSystem.cs` |
| `GetAvailableEmotions` | 9592634 | 1 | `client/SocialSystem.cs` |
| `GetSeasons` | 871245 | 1 | `client/Durango.Logic/SeasonSystem.cs` |
| `AttachAccessory` | 9823459 | 1 | `client/EquipSystem.cs` |
| `Tool_Collectibles` | 328 | 1 | `client/Durango.UI/GatheringCheatWidget.cs` |
| `GetMusic` | 47852452 | 1 | `client/MusicManager.cs` |
| `GetRouteOfArchipelago` | 20301 | 1 | `client/ExploreSystem.cs` |
| `AcceptMilestone` | 800015 | 1 | `client/PetManager.cs` |
| `GetTimelineOption` | 81234526 | 1 | `client/Durango.Logic.Timeline/TimelineLogList.cs` |
| `WashBody` | 3494 | 1 | `client/InteractionSystem.cs` |
| `ChangeFarmingEncyclopediaMastery` | 37128 | 1 | `client/FarmingEncyclopediaSystem.cs` |
| `GrowRapidly` | 3712 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `PlayConcert` | 63459081 | 1 | `client/MusicManager.cs` |
| `ParticipateAcceleration` | 21112513 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `Collected` | 104 | 1 | `client/Durango.Online/Player.cs` |
| `Block` | 4016 | 1 | `client/SocialSystem.cs` |
| `Depart` | 2448 | 1 | `client/MoveMsgGenerator.cs` |
| `Tune` | 2400 | 1 | `client/SocialSystem.cs` |
| `Keepalive` | 254 | 1 | `client/Durango.Network/Connection.cs` |
| `Bleach` | 3669 | 1 | `client/CraftSystem.cs` |
| `AcceptSuggestion` | 9138749 | 1 | `client/ClanSystem.cs` |
| `GetAllySlots` | 9138745 | 1 | `client/ClanSystem.cs` |
| `Withdraw` | 2028 | 1 | `client/ExploreSystem.cs` |
| `SuggestBreak` | 9138748 | 1 | `client/ClanSystem.cs` |
| `ReturnToCamp` | 3462987 | 1 | `client/MapSystem.cs` |
| `AcceptTENCoupon` | 2345690 | 1 | `client/Durango.System.Config/ConfigInstance.cs` |
| `GiveUpDistribution` | 451390 | 1 | `client/GatheringSystem.cs` |
| `GetAttachableAccessories` | 9823457 | 1 | `client/EquipSystem.cs` |
| `ExtinguishBurnable` | 2097 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `DepartTutorial` | 2306 | 1 | `client/TutorialIslandSystem.cs` |
| `SailingBack` | 3130 | 1 | `client/ExploreSystem.cs` |
| `Dashed` | 2491 | 1 | `client/PlayerController.cs` |
| `ToggleConversationNotification` | 4011 | 1 | `client/SocialSystem.cs` |
| `ReleaseReinFromCage` | 694353 | 1 | `client/PetManager.cs` |
| `SetSectionOrder` | 3688 | 1 | `client/InventorySystem.cs` |
| `GetSpecialDeals` | 259680 | 1 | `client/ShopSystem.cs` |
| `ChangeFollowMusic` | 47852459 | 1 | `client/MusicManager.cs` |
| `ExitConversation` | 4010 | 1 | `client/SocialSystem.cs` |
| `Collect` | 2026 | 1 | `client/GatheringSystem.cs` |
| `SetSharedConcertMusic` | 63459180 | 1 | `client/MusicManager.cs` |
| `UnmountVehicle` | 192834 | 1 | `client/PetManager.cs` |
| `GardenDiff` | 202 | 1 | `client/Durango.Online/Player.cs` |
| `SkipPostprocess` | 2450 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `GetMemos` | 2439 | 1 | `client/MemoSystem.cs` |
| `FireBurnable` | 2096 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `ReturnToHome` | 2100 | 1 | `client/MapSystem.cs` |
| `Resurrect` | 132 | 1 | `client/CPRSystem.cs` |
| `DeregisterUser` | 1999 | 1 | `client/Durango.System.Config/ConfigInstance.cs` |
| `GetAvailablePersonalResearch` | 5987336 | 1 | `client/Durango.Logic/ResearchSystem.cs` |
| `GetSharedMusic` | 47852457 | 1 | `client/MusicManager.cs` |
| `S02PVPRefresh` | 222207 | 1 | `client/Durango.Logic/PvpIslandSystem.cs` |
| `TakeEffect` | 821 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `UprootPlant` | 3807 | 1 | `client/Durango.Logic.Interactions/ArtifactInteractions.cs` |
| `Feeding` | 805 | 1 | `client/PetManager.cs` |
| `DisappearEntity` | 101 | 1 | `client/Durango.Online/Player.cs` |
| `SetTimelineOption` | 81234528 | 1 | `client/Durango.Logic.Timeline/TimelineLogList.cs` |
| `GetCollectible` | 2017 | 1 | `client/GatheringSystem.cs` |
| `PickMilestoneAgain` | 800014 | 1 | `client/PetManager.cs` |
| `GetWarehouse` | 3683 | 1 | `client/Durango.Logic.Item/Inventory.cs` |
| `MiniGameDanceScore` | 4625400 | 1 | `client/Durango.UI/MiniGameDanceGroup.cs` |
| `ReissueArchipelagoTodos` | 240005 | 1 | `client/Durango.Logic/ArchipelagoMissionSystem.cs` |
| `GetRoutes` | 2030 | 1 | `client/ExploreSystem.cs` |
| `S02DequeueEntree` | 222202 | 1 | `client/Durango.Logic/WarpRushSystem.cs` |
| `GetSailingBackCost` | 3110 | 1 | `client/ExploreSystem.cs` |
| `ConfirmResurrection` | 56238474 | 1 | `client/CPRSystem.cs` |

## รับได้แล้ว (33)

`ChangeMannequinDisplay`, `ChargeEffect`, `Cheat`, `CloseGate`, `DestructArtifact`, `DisappearEntityOnTile`, `Display`, `ExtendFloor`, `GetAddOns`, `GetArtifactBlueprints`, `GetClock`, `GetEstateLicenses`, `GetFavoriteProducts`, `GetGrazedPets`, `GetMusics`, `GetQuests`, `GetRecipes`, `OpenGate`, `PlaceAddOns`, `PlantSeed`, `PlayMusic`, `Ready`, `RemoveMusicFromSlot`, `RestOn`, `SaveMusicToSlot`, `Scribble`, `SetChunk`, `StopMusic`, `TakeOutItem`, `Touch`, `TurnOffMusic`, `TurnOnMusic`, `Wash`
