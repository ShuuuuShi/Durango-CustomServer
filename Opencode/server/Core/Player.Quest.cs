using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  เควส — GetQuests + GetQuestState
//
//  ⚠️ กลไก "ทับ" handler เดิม: Player.cs:476 ลงทะเบียน GetQuests ไว้แล้วใน constructor
//  แต่ RegisterSystemHandlers() (Player.cs:503) ถูกเรียก "หลัง" บล็อกนั้น และ
//  Connection.RegisterMessageHandlerToRegistry (GameCode/Durango.Online/Connection.cs:198-202)
//  จะ Remove ของเกมแล้ว Add ตัวใหม่ทับ ⇒ **ตัวที่ลงทะเบียนทีหลังชนะ**
//  (คอมเมนต์เดียวกันอยู่ที่ Player.Systems.cs:26,34 — GetStatistics/GetSkills ก็ทับแบบนี้)
//
//  ═══ ทำไมต้องตอบทุกคำขอเสมอ (แม้จะไม่มีข้อมูล) ═══
//  ฝั่งเกมรอคำตอบแบบ "ไม่มี timeout":
//    - QuestSystem.GetQuests (client/Durango.Logic/QuestSystem.cs:191-203) ผูก .On<Quests>
//      กับ seq ของคำขอ · .Rest ไม่ใช่ตัวจับหมดเวลา แต่คือ "ก้อนอื่นที่ไม่ตรง .On"
//      (client/Durango.Network/Connection.cs:868-908 HandleMsg — ยิงเฉพาะเมื่อ "มี" reply
//      ก้อนหนึ่งมาถึงแล้วไม่ตรง .On) ⇒ ไม่ตอบ = callback ไม่มีวันยิง
//    - ผลที่เห็น: Category.GetQuestList (client/Durango.Logic.Quest/Category.cs:62-81) ตั้ง
//      _isLoadingQuests = true แล้วรอ · หน้าสมุดเรื่อง (client/Durango.UI/StoryGroup.cs:45)
//      และหน้าเควส (client/Durango.UI/QuestGroup.cs:163) รับ list ผ่าน callback ตัวนี้เท่านั้น
//      ⇒ เซิร์ฟเงียบ = "กำลังโหลด" ตลอดกาล และเปิดครั้งต่อไปก็ไม่ยิงคำขอซ้ำ (:67)
//  handler เดิม (Player.cs:476-494) มีจุดตายอยู่ 2 จุดที่แก้ในไฟล์นี้:
//    1. SingletonDict.Get(category) ไม่เจอ ⇒ "ไม่ตอบเลย" ⇒ ค้างตามข้างบน (ต้นฉบับเซิร์ฟแท้ก็เป็น
//       แบบนี้ client/Durango.Online/Player.cs:296-311)
//    2. ไม่สน Category ที่ขอมา — ตอบเควส "sunset" ทุกครั้ง ⇒ ถ้าวันหน้าเซิร์ฟโฆษณาหมวดอื่น
//       (QuestCategories) หน้าเควสของหมวดนั้นจะโชว์เควสเรื่องหลักทั้ง 101 ชิ้น (Finished หมด)
//
//  ═══ "สถานะจริง" ที่เซิร์ฟจะบอกได้ตอนนี้ ═══
//  เซิร์ฟยังไม่มีเอนจินเควส (ไม่มีใครเริ่ม/วัดความคืบหน้าเควส) ⇒ สถานะจริงของเควสเรื่องหลัก
//  คือสิ่งที่เซิร์ฟแท้ประกาศไว้เอง: **จบหมด** (Finished=true — client/Durango.Online/Player.cs:306)
//  ซึ่งตรงกับ UI ที่ผู้เล่นเห็นมาแล้วทั้งหมด (สมุดเรื่องโชว์ทุกตอน "สำเร็จ" = ไม่มีเควสให้ทำ)
//  เปลี่ยนเป็น "ยังไม่เริ่ม" ทั้ง 101 ชิ้น = พังหน้าจอเดิมโดยไม่มีเอนจินรองรับ ⇒ ห้าม
//  ส่วนเควสที่ "ไม่อยู่" ในข้อมูล (ถูกถามถึงแต่เซิร์ฟไม่มี) ตอบ NotActivated — "ยังไม่มีเควสนี้"
//  ถูกต้องตามโปรโตคอล (enum ที่ client แปลความหมายจริง — client/Yaml QuestState ผ่าน
//  client/Durango.Logic/QuestSystem.cs:212 msg.States.Get(questId, Invalid))
//
//  QuestStore ด้านล่างคือจุดเสียบ "เอนจินเควสจริง" ในอนาคต: ระบบที่เล่นเควสจริง (เช่น
//  Player.Tutorial.cs) เขียนสถานะทับได้ทันที โดย GetQuests/GetQuestState อ่านจากที่เดียวกัน
//  ⇒ สองเส้นไม่มีวันตอบขัดกันเอง
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterQuestHandlers()
    {
        // ── GetQuests (237918) — รายการเควสของหมวดหนึ่ง ─────────────────────────────
        // เกมเรียกตอนเปิดหน้าเควส/สมุดเรื่อง: Category.GetQuestList → QuestSystem.GetQuests
        // (client/Durango.Logic.Quest/Category.cs:70 → client/Durango.Logic/QuestSystem.cs:191)
        // ⚠️ ต้องตอบตรง header.Seq เพราะ client ผูก .On<Quests> กับ seq นั้น (QuestSystem.cs:196)
        _connection.Recv(delegate(GetQuests msg, PacketHeader header)
        {
            HandleGetQuestsMsg(msg, header.Seq);
        });

        // ── GetQuestState (398132) — สถานะของเควสเป็นชิ้น ๆ ─────────────────────────
        // ใช้โดยระบบไกด์ (PlayGuide): QuestReward รอ state == Finished ถึงจะปลดขั้น
        // (client/Durango.Logic.PlayGuide/QuestReward.cs:18-24) และ QuestRewardToDo ปิด todo
        // เมื่อ Finished หรือ NotActivated (client/Durango.Logic.PlayGuide/QuestRewardToDo.cs:18-24)
        // ⚠️ เดิมไม่มี handler เลย ⇒ callback ของ .On<QuestState> ไม่เคยยิง (ไม่มี .Rest รอง —
        //    QuestSystem.cs:205-215) ⇒ ไกด์ที่รอ "รับรางวัลเควส X" ค้างขั้นนั้นเงียบ ๆ
        // ⚠️ ต้องตอบตรง header.Seq — client ไม่ได้ลงทะเบียน On<QuestState> แบบ global
        //    (QuestSystem.Start ที่ :59-69 ลงเฉพาะ NotifyQuestProceed/QuestStarted/
        //    QuestCategories/QuestRewardResults) ⇒ ตอบ ReplyOf ผิด = ตกลงไม่มีใครรับ
        _connection.Recv(delegate(GetQuestState msg, PacketHeader header)
        {
            HandleGetQuestStateMsg(msg, header.Seq);
        });
    }

    private void HandleGetQuestsMsg(GetQuests msg, uint seq)
    {
        // หมวดที่ขอมาเป็นความจริงของคำขอ — เดิมทับด้วย "sunset" เสมอ (Player.cs:483)
        // ว่าง = โปรโตคอลไม่ได้กำหนดมา ⇒ ใช้หมวดเรื่องหลักเป็นค่าตั้งต้น
        string category = string.IsNullOrEmpty(msg.Category) ? EpicCategory : msg.Category;

        var todos = new List<QuestToDo>();
        // แหล่งข้อมูลเดียวกับ handler เดิม (data/assets/quests/epics_for_client.json → StoryYaml,
        // โหลดที่ Support/DataStore.cs:47-48) — ไม่เดารายชื่อเควสเอง
        Chapters chapters = SingletonDict<string, Chapters>.Get(category);
        if (chapters?.ChapterList != null)
        {
            foreach (Chapter chapter in chapters.ChapterList)
            {
                if (chapter?.Quests == null) continue;
                foreach (string questId in chapter.Quests)
                {
                    if (string.IsNullOrEmpty(questId)) continue;
                    todos.Add(QuestStore.ToQuestToDo(EntityId, questId));
                }
            }
        }
        else
        {
            // ⚠️ ไม่มีข้อมูลหมวดนี้ = ตอบ "ไม่มีเควส" ไม่ใช่ "เงียบ" — เดิมเงียบทำให้หน้าเควสค้างโหลด
            // (Category._isLoadingQuests ติด true ถาวร — client/Durango.Logic.Quest/Category.cs:67-71)
            Console.WriteLine($"[เควส] ไม่มีข้อมูลหมวด '{category}' — ตอบรายการว่างให้ {Short(EntityId)}");
        }

        Send(new Quests
        {
            Category = category,     // echo ตามที่ขอ (client ใช้ category ฝั่งของตัวเองต่อ — QuestSystem.cs:196-198)
            Todos = todos.ToArray()
        }, seq);
    }

    private void HandleGetQuestStateMsg(GetQuestState msg, uint seq)
    {
        var states = new Dictionary<string, Shared.Quest.QuestState>();
        // client ส่งมาครั้งละ 1 id (QuestSystem.cs:207-209) แต่โปรโตคอลรองรับหลาย id ⇒ ทำครบ
        // ⚠️ key ว่างห้ามใส่ — Dictionary<string,_> โยน ArgumentNullException กับ null key
        //    และ client ค้นด้วย id ตัวเดิมที่มันถาม (QuestSystem.cs:212) ใส่มาก็ไม่มีใครอ่าน
        if (msg.QuestIds != null)
        {
            foreach (string questId in msg.QuestIds)
            {
                if (string.IsNullOrEmpty(questId)) continue;
                states[questId] = QuestStore.StateOf(EntityId, questId);
            }
        }
        // ตัวที่ถามแต่ไม่อยู่ใน States จะได้ Invalid ฝั่ง client (QuestSystem.cs:212)
        // ⇒ ไม่ปลด QuestRewardToDo (:20 เช็คเฉพาะ Finished/NotActivated) ⇒ ใส่ครบทุก id ที่ถามดีกว่า
        Send(new QuestState
        {
            States = states
        }, seq);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ที่เก็บสถานะเควส (ใช้ร่วมกันทุกผู้เล่นในโปรเซสเดียว)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// สถานะเควสต่อผู้เล่น — จุดเดียวที่ GetQuests/GetQuestState ใช้ตัดสิน "ตอบอะไร"
    ///
    /// ⚠️ **อยู่ในหน่วยความจำเท่านั้น — รีสตาร์ตเซิร์ฟแล้วสถานะกลับค่าตั้งต้น** (เหตุผลและ
    /// แผน persist เดียวกับ PetStore ใน Core/Player.Animals.cs:1364-1375: ที่ถูกต้องคือ
    /// Core/PlayerContext.cs ซึ่งงานนี้ห้ามแก้) — ตอนนี้ store ว่างเสมอ ⇒ ทุกเควสตอบตาม
    /// ค่า default ข้างล่าง ซึ่งตรงกับพฤติกรรมเดิม 100%
    ///
    /// ไม่ต้องล็อกเธรด เพราะ handler ทั้งหมดถูกเรียกจาก main loop เส้นเดียว
    /// (Program.cs → host.Process() → … → Connection.Process — หลักการเดียวกับ PetStore)
    /// </summary>
    public static class QuestStore
    {
        /// <summary>สถานะเควสหนึ่งชิ้นที่เซิร์ฟ "รู้จริง" (จากระบบที่เล่นเควสจริง ไม่ใช่เดา)</summary>
        public sealed class Entry
        {
            public Shared.Quest.QuestState State;

            /// <summary>ความคืบหน้าปัจจุบัน — ส่งตรงเข้า QuestToDo.Progress</summary>
            public int Progress;

            /// <summary>
            /// เป้าหมาย — ⚠️ ห้ามเป็น 0 ถ้ายังไม่จบ: client ให้ป้ายแจ้งเตือน "มีรางวัลรอรับ"
            /// จากเงื่อนไข Progress >= GoalCount &amp;&amp; !Finished
            /// (client/Durango.Logic.Quest/Category.cs:233-241) ⇒ GoalCount=0 + ยังไม่จบ =
            /// ป้ายเตือนลวงโผล่ทันที ทั้งที่เควสยังไม่ทำอะไรเลย
            /// </summary>
            public int GoalCount = 1;
        }

        private static readonly Dictionary<string, Dictionary<string, Entry>> PerPlayer = new();

        /// <summary>เควสทั้งหมดของผู้เล่นคนนี้ที่เซิร์ฟจดไว้ (คืน dict จริง — แก้ได้เลย)</summary>
        public static Dictionary<string, Entry> Of(string ownerEntityId)
        {
            if (string.IsNullOrEmpty(ownerEntityId)) return new Dictionary<string, Entry>();
            if (PerPlayer.TryGetValue(ownerEntityId, out Dictionary<string, Entry> dict)) return dict;
            dict = new Dictionary<string, Entry>();
            PerPlayer[ownerEntityId] = dict;
            return dict;
        }

        public static Entry Find(string ownerEntityId, string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            return Of(ownerEntityId).GetValueOrDefault(questId);
        }

        /// <summary>
        /// จดสถานะจริงของเควสหนึ่งชิ้น — เรียกจากระบบที่ขับเคลื่อนเควสในอนาคต
        /// (เช่นบทเรียนเริ่มเกมเริ่มเควสแรก → Set(id, WorkInProgress, 0, 1))
        /// </summary>
        public static void Set(string ownerEntityId, string questId, Shared.Quest.QuestState state,
                               int progress = 0, int goalCount = 1)
        {
            if (string.IsNullOrEmpty(questId)) return;
            Of(ownerEntityId)[questId] = new Entry
            {
                State = state,
                Progress = progress,
                // กันป้ายรางวัลลวงตามคอมเมนต์ GoalCount — จบแล้วเท่านั้นที่ปล่อย 0 ได้
                GoalCount = state == Shared.Quest.QuestState.Finished ? Math.Max(goalCount, progress) : Math.Max(goalCount, 1)
            };
        }

        /// <summary>
        /// สถานะที่เซิร์ฟจะบอก client — ใช้ร่วมกันทั้ง GetQuests และ GetQuestState
        ///
        /// ลำดับการตัดสิน:
        ///   1. จดไว้ใน store = สถานะจริงจากระบบเควส
        ///   2. เป็นเควสเรื่องหลัก ("sunset" จาก epics_for_client) = Finished — สถานะที่เซิร์ฟแท้
        ///      ประกาศไว้เองและหน้าจอที่ผู้เล่นเห็นมาตลอด (client/Durango.Online/Player.cs:304-308)
        ///   3. ไม่รู้จัก = NotActivated — "ยังไม่มีเควสนี้" ตาม enum ของโปรโตคอล
        ///      (GameCode/Shared.Quest/QuestState.cs — client ใช้ค่านี้ปิด todo ของไกด์:
        ///      client/Durango.Logic.PlayGuide/QuestRewardToDo.cs:20)
        /// </summary>
        public static Shared.Quest.QuestState StateOf(string ownerEntityId, string questId)
        {
            Entry entry = Find(ownerEntityId, questId);
            if (entry != null) return entry.State;
            return IsStoryQuest(questId)
                ? Shared.Quest.QuestState.Finished
                : Shared.Quest.QuestState.NotActivated;
        }

        /// <summary>แปลงสถานะเป็น QuestToDo ตามโครงที่ handler เดิมส่ง (ค่าเริ่มต้นเหมือนเดิมทุกฟิลด์)</summary>
        public static QuestToDo ToQuestToDo(string ownerEntityId, string questId)
        {
            Entry entry = Find(ownerEntityId, questId);
            bool finished = StateOf(ownerEntityId, questId) == Shared.Quest.QuestState.Finished;
            return new QuestToDo
            {
                Id = questId,
                // ค่า Progress/GoalCount/EndAt/Reward เหมือน handler เดิมทุกอย่างเมื่อไม่มีข้อมูลจริง
                Progress = entry?.Progress ?? 0,
                GoalCount = entry?.GoalCount ?? 0,
                Finished = finished,
                EndAt = 0.0,
                Reward = null
            };
        }

        // ── ชุด id ของเควสเรื่องหลัก — cache ครั้งเดียวต่อโปรเซส ────────────────────────
        private static HashSet<string> _storyQuestIds;

        /// <summary>
        /// id นี้เป็นเควสเรื่องหลักหรือไม่ — ตัดสินจากข้อมูลจริง (epics_for_client ทุกหมวด)
        /// ไม่ใช่จากการ hard-code "sunset" เพื่อให้ยังทำงานถูกถ้าวันหน้ามีหมวดเรื่องหลักเพิ่ม
        /// </summary>
        public static bool IsStoryQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return false;
            if (_storyQuestIds == null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                Dictionary<string, Chapters> stories = SingletonDict<string, Chapters>.Instance;
                if (stories != null)
                {
                    foreach (Chapters chapters in stories.Values)
                    {
                        if (chapters?.ChapterList == null) continue;
                        foreach (Chapter chapter in chapters.ChapterList)
                        {
                            if (chapter?.Quests == null) continue;
                            foreach (string id in chapter.Quests)
                            {
                                if (!string.IsNullOrEmpty(id)) ids.Add(id);
                            }
                        }
                    }
                }
                _storyQuestIds = ids;
            }
            return _storyQuestIds.Contains(questId);
        }
    }
}
