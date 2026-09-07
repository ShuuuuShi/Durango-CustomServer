using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;

namespace Durango.Online;

/// <summary>
/// สัตว์ป่าบนเกาะ — เกิด เก็บค่าสถานะ และส่งให้ผู้เล่นเห็น
///
/// ═══ ทำไมต้องเขียนขึ้นใหม่ทั้งก้อน ═══
/// เซิร์ฟในตัวเกมของ NEXON (ที่โปรเจกต์นี้ใช้เป็นฐาน) **ไม่เคยเกิดสัตว์เลยสักตัว** —
/// ตรวจแล้วว่าไม่มีจุดไหนในซอร์สเซิร์ฟสร้าง <c>AppearAnimal</c> เลย มีแต่ฝั่งเกมที่รอรับ
/// (<c>client/AnimalManager.cs:23</c>) ⇒ เกาะทุกลูกโล่งเปล่า ล่าสัตว์ไม่ได้ ทำเนื้อไม่ได้
/// ตรงนี้จึงเป็นของใหม่ ไม่มีต้นฉบับให้ลอก **แต่ข้อมูลมีครบ** จึงอิงข้อมูลจริงทุกค่า
///
/// ═══ ข้อมูลมาจากไหน ═══
/// ต้องประกบสองไฟล์ ไฟล์เดียวไม่พอ:
///   1. <c>herds.yml</c> ในไฟล์เกาะ → **"ฝูงเกิดตรงไหน"** (พิกัดแยกตามกลุ่ม land/beach/ocean/…)
///   2. <c>region_templates.json → herds</c> → **"ฝูงไหนเป็นสัตว์อะไร เลเวลเท่าไร"**
/// ชื่อกลุ่มของสองไฟล์ตรงกันเป๊ะ และจำนวนใน <c>spawns</c> เท่ากับ <c>total_count</c> ทั้ง 310 กลุ่ม
/// ⇒ จับคู่ตามลำดับ: ฝูงที่ i ในแม่แบบ เกิดที่จุดที่ i ในไฟล์เกาะ
///
/// ค่าสถานะทุกตัวคิดจากสูตรใน <c>entity_types/animal.json</c> ด้วย <see cref="StatFormula"/>
/// เช่น <c>life_max = (1.06 * ((combat_level + 24) ** 2)) * unstable_factor</c>
/// ⇒ ไม่มีเลขไหนที่เราตั้งเอง นอกจากที่กำกับไว้ชัด ๆ ด้านล่าง
///
/// ═══ ที่ยังไม่ได้ทำ (ตั้งใจ) ═══
/// • **การเดินเล่นเป็นของฝั่งเกม ไม่ใช่ของเรา** — <c>client/ClientAnimalActor.cs</c> เดินสุ่ม
///   รอบจุดเกิดเองทุกเฟรม (<c>_wanderRadius</c> 500 = 2.5 ช่อง) ⇒ **ห้ามส่ง Move ให้สัตว์**
///   จะไปสู้กับตัวมันเอง · ตำแหน่งที่เซิร์ฟเก็บจึงเป็น "จุดเกิด" ไม่ใช่ตำแหน่งจริงบนจอ
///   ซึ่งพอใช้ได้เพราะรัศมีเดินเล่นเล็กกว่าระยะไล่กัด (ดู Player.Hunting.AnimalTurn)
/// • **ยังไม่เกิดใหม่หลังตาย** — ตายแล้วซากอยู่จนกว่าจะปิดเซิร์ฟ (เปิดใหม่กลับมาครบ)
/// • ยังไม่เซฟ — สัตว์ที่ล้มไปแล้วฟื้นหมดตอนเปิดเซิร์ฟใหม่
/// </summary>
public class AnimalManager
{
    /// <summary>
    /// เพดานจำนวนสัตว์ต่อเกาะ — **ค่านี้เราตั้งเอง ไม่ใช่ของ NEXON**
    ///
    /// แม่แบบสั่งไว้ถึง 100 ฝูงต่อกลุ่ม รวมหลายกลุ่มได้ 150+ ตัว ซึ่งเกมจริงรับไหวเพราะเซิร์ฟจริง
    /// ส่งเฉพาะตัวที่อยู่ใกล้ แต่เรายังส่งทั้งเกาะ (ดูหัวข้อ "ที่ยังไม่ได้ทำ") ⇒ ต้องจำกัดไว้ก่อน
    /// พอทำ chunk culling แล้วให้ยกเพดานนี้ขึ้นเป็นค่าตามแม่แบบได้เลย
    /// </summary>
    public const int MaxAnimalsPerRegion = 60;

    /// <summary>
    /// ตัวคูณความไม่เสถียรในสูตรค่าสถานะ — **เราตั้งเป็น 1.0**
    ///
    /// สูตรใน animal.json แทบทุกตัวจบด้วย <c>* unstable_factor</c> แต่ไฟล์ข้อมูลไม่ได้บอกค่า
    /// ไว้ที่ไหนเลย (หาแล้วทั้ง constants.json และ region_templates.json) ⇒ 1.0 คือ "เกาะปกติ"
    /// ซึ่งเป็นค่าเดียวที่ทำให้สูตรอ่านออกมาตรงกับเลเวลสัตว์ตามที่ตั้งใจ
    /// เกาะไม่เสถียรค่อยยกค่านี้ขึ้นตอนทำระบบเกาะไม่เสถียร
    /// </summary>
    private const double StableFactor = 1.0;

    /// <summary>1 ช่อง = 200 หน่วยพิกัดโลก (ค่าเดียวกับ Player.cs:534 และ Player.Gathering.cs:351)</summary>
    private const float TileSize = 200f;

    /// <summary>
    /// ความเร็วหมุนตัวของสัตว์ (องศา/วินาที) — ส่ง 0 ฝั่งเกม fallback เป็น 300
    /// (client/AnimalBehavior.cs:997 SetRotateSpeed)
    ///
    /// ⚠️ **ค่าของเรา** 540 — ไม่ใช่ 100 ของ client/ClientAnimalActor.cs:31
    /// ตัวนั้นเป็นสัตว์ที่ client เดินเองซึ่ง **แตกเส้นทางเป็นหลายจุดให้ค่อย ๆ เลี้ยว**
    /// (GeneratePath วน MoveTowardsAngle ทีละ 0.5 วิ) แต่เราส่ง path 2 จุด
    /// ⇒ ต้องหมุนให้ทันภายในช่วงเดียว 100 องศา/วิ = กลับหลังหันใช้ 1.8 วิ
    ///   ซึ่งนานกว่าเวลาเดินส่วนใหญ่ ⇒ เห็นเป็นเดินหันข้าง/หันผิดทางทั้งเส้น
    /// (โปรเจกต์ Opencode ที่ไม่มีอาการนี้ใช้ 540 — ServerAnimal.cs:270)
    /// </summary>
    private const float DefaultRotateSpeed = 540f;

    /// <summary>สัตว์ป่าหนึ่งตัวบนเกาะ</summary>
    public class Animal
    {
        public string EntityId;
        public ushort EntityType;
        public int CombatLevel;
        public float LifeMax;
        public float Life;
        public float Attack;
        public float Defense;
        public bool IsAlive = true;

        /// <summary>
        /// ซากนี้ถูกชำแหละไปแล้วหรือยัง
        ///
        /// ⚠️ ไม่มีตัวนี้ = ล้มสัตว์ตัวเดียวแล้วกดชำแหละซ้ำได้ไม่จำกัด ⇒ เนื้อ/หนัง/กระดูกไม่จำกัด
        /// **ผู้เล่นปกติกดรัวก็เจอเองในชั่วโมงแรก** ไม่ใช่การโกง ห้ามด้วยกฎไม่ได้
        /// (ทางของธรรมชาติมีตัวกันอยู่แล้ว — DestroyNatural ลบของออกจากโลกหลังเก็บ
        ///  แต่ทางซากข้ามขั้นนั้นไปเพราะซากไม่ได้อยู่ในตาราง natural)
        /// </summary>
        public bool Butchered;

        /// <summary>เวลาที่ตาย (Gauge.CurrentTime) — 0 คือยังไม่ตาย · ใช้ตอนทำระบบเกิดใหม่</summary>
        public double DiedAt;

        /// <summary>ผู้เล่นที่มันกำลังเล่นงานอยู่ — สัตว์กินพืชจะตั้งค่านี้ก็ต่อเมื่อถูกตีก่อน</summary>
        public string AggroTargetId;

        /// <summary>ตีได้อีกครั้งเมื่อไร (Gauge.CurrentTime) — คุมจังหวะด้วย attack_cooltime ของชนิดนั้น</summary>
        public double NextAttackAt;

        /// <summary>
        /// [7 ก.ย. 2026] เวลาที่ "เห็นเป้าอยู่ในระยะ" ครั้งล่าสุด — ใช้ตัดสินว่าควรเลิกโกรธหรือยัง
        ///
        /// ⚠️ ไม่มีตัวนี้ = <c>AggroTargetId</c> ถูกตั้งแล้วไม่มีจุดไหนเคลียร์เลยสักที่
        /// (grep ทั้งเซิร์ฟ) แล้ว AnimalManager.Process ข้ามการเดินเล่นตลอดถ้ายังมี aggro
        /// ⇒ สัตว์ค้างท่าเตรียมสู้ตลอดชีพ และพอผู้เล่นเดินหนีไปไกลมันก็ยังไม่กลับไปเดินเล่น
        /// </summary>
        public double AggroSeenAt;

        /// <summary>จุดที่มันเกิด — เดินเล่นวนอยู่รอบ ๆ จุดนี้ ไม่หลุดไปไกล</summary>
        public Point2 HomeTile;

        /// <summary>ถึงเวลาออกเดินรอบถัดไปเมื่อไร</summary>
        public double NextWanderAt;

        /// <summary>ถึงเวลาหยุดเดินแล้วกลับไปยืน (0 = ไม่ได้เดินอยู่)</summary>
        public double StopWalkingAt;

        /// <summary>
        /// [7 ก.ย. 2026] ถึงเวลาสั่งกลับไปท่ายืน (0 = ไม่ต้องสั่ง)
        ///
        /// ⚠️ ไม่มีตัวนี้ = คลิปโจมตีเล่นจบแล้วไม่มีอะไรพากลับท่ายืน
        /// คลิปโจมตีของเกมนี้ขยับ root bone ไปข้างหน้า ⇒ ตัวค้างอยู่หน้าตำแหน่งจริง
        /// แล้ว packet ถัดไปกระชากกลับ = ผู้เล่นเห็นเป็นวาร์ป
        /// (เทียบจากโปรเจกต์ Opencode ที่ไม่มีอาการนี้ — AnimalSpawner.StandAt)
        /// </summary>
        public double StandAt;

        /// <summary>
        /// [7 ก.ย. 2026] มุมหันหน้าปัจจุบัน (องศา 0-360)
        ///
        /// ⚠️ ไม่ส่งมุมไปกับ Movement = ตัวค้างหันไปทางที่เดินมาล่าสุด ⇒ กัดลม
        /// </summary>
        public float Yaw;

        /// <summary>จุดเริ่มเดิน/เวลาที่จะถึงปลายทาง — ใช้คำนวณตำแหน่งจริงระหว่างเดิน</summary>
        public WorldPosition WalkFrom;
        public WorldPosition WalkTo;
        public double WalkStartAt;
        public double WalkEndAt;

        /// <summary>
        /// [7 ก.ย. 2026] ตำแหน่งจริงเป็นพิกัดโลกต่อเนื่อง ไม่ใช่ช่อง
        ///
        /// ⚠️ เดิมเก็บเป็น <c>Point2 Tile</c> แล้วคูณ 200 กลับเป็นพิกัด ⇒ ทุกคำสั่งถูกปัด
        /// เข้ากลางช่อง คลาดได้ถึง ~141 หน่วย ซึ่ง **ใหญ่พอ ๆ กับระยะกัด (200)**
        /// ⇒ มุมที่คำนวณจากจุดที่ปัดแล้วเพี้ยนได้เป็นสิบ ๆ องศา = อาการหันหน้าไม่ถูก
        /// (โปรเจกต์ Opencode ที่ไม่มีอาการนี้เก็บเป็น WorldPosition ตลอด — ServerAnimal.cs:32)
        /// </summary>
        public WorldPosition Position;

        /// <summary>ช่องที่มันอยู่ — คิดจาก <see cref="Position"/> ใช้เฉพาะตอนวัดระยะ/วาง Collectible</summary>
        public Point2 Tile => new((int)Math.Round(Position.x / TileSize),
                                  (int)Math.Round(Position.y / TileSize));

        /// <summary>
        /// [7 ก.ย. 2026] ตำแหน่ง "จริง" ณ วินาทีนี้ — ถ้ากำลังเดินอยู่จะคิดจากเส้นทาง
        ///
        /// ⚠️ ใช้ <see cref="Position"/> เป็นจุดเริ่ม path ไม่ได้ เพราะมันคือ "ปลายทาง
        /// ของคำสั่งเดินก่อนหน้า" ⇒ ฝั่งเกมได้ packet แล้วกระโดดไปข้างหน้าทันที
        /// </summary>
        public WorldPosition PositionAt(double now)
        {
            if (WalkEndAt <= WalkStartAt || now >= WalkEndAt) return Position;
            if (now <= WalkStartAt) return WalkFrom;
            float t = (float)((now - WalkStartAt) / (WalkEndAt - WalkStartAt));
            return new WorldPosition(WalkFrom.x + (WalkTo.x - WalkFrom.x) * t,
                                     WalkFrom.y + (WalkTo.y - WalkFrom.y) * t);
        }

        /// <summary>มุมหันจากจุดหนึ่งไปอีกจุด (องศา 0-360 — สูตรเดียวกับฝั่งเกม atan2(dx, dz))</summary>
        public static float YawTo(WorldPosition from, WorldPosition to)
        {
            float dx = to.x - from.x;
            float dy = to.y - from.y;          // world y = client z
            float yaw = (float)(Math.Atan2(dx, dy) * (180.0 / Math.PI));
            return yaw < 0f ? yaw + 360f : yaw;
        }

        /// <summary>
        /// ท่าที่ควรเล่นตามสถานะตอนนี้ — ชื่อ clip จริงจาก asset (ดู Support/AnimalMotions.cs)
        ///
        /// ตายแล้วต้องส่งท่าตายมาเอง: <c>AnimalBehavior.OnDie</c> (client บรรทัด 830-846)
        /// **ไม่ได้เล่นท่าตายให้** มันแค่เปลี่ยน layer กับไล่สีจาง ⇒ ถ้าไม่ส่ง สัตว์ตายแล้วยังยืนท่าเดิม
        /// (ตัวที่เล่นท่าตายคือ SetAsDead ซึ่งใช้กับซากที่ terrain วางไว้เท่านั้น — AnimalNaturalObject.cs:9)
        ///
        /// กำลังโกรธใครอยู่ ⇒ ท่ายืนแบบเตรียมสู้ ให้เห็นว่ามันไม่ได้ยืนเฉย ๆ แล้ว
        /// </summary>
        public string CurrentMotion
        {
            get
            {
                AnimalMotions.Motions m = AnimalMotions.Of(EntityType);
                if (m == null) return null;
                if (!IsAlive) return m.Dead ?? m.Stand;
                if (!string.IsNullOrEmpty(AggroTargetId)) return m.BattleStand ?? m.Stand;
                return m.Stand ?? m.Idle;
            }
        }

        /// <summary>ท่าตายเล่นครั้งเดียวแล้วค้างท่าสุดท้าย ท่าอื่นวนซ้ำ</summary>
        public MotionOption CurrentMotionOption =>
            IsAlive ? MotionOption.LOOPING : MotionOption.NORMAL;

        /// <summary>
        /// [7 ก.ย. 2026] packet "ยืนอยู่กับที่แล้วเล่นคลิปหนึ่ง" พร้อมหันหน้าไปทางที่กำหนด
        ///
        /// ฝั่งเกมเล่นอนิเมชั่นสัตว์จาก <c>Movement.MotionName</c> ของ packet Move เท่านั้น
        /// (client/AnimalBehavior.cs HandleMoveMsg → PlayAnimationMovement)
        /// ⇒ path 2 จุดที่ตำแหน่งเดียวกัน = "อยู่กับที่" แต่ยังสั่งท่ากับมุมหันได้
        ///
        /// MotionOption เป็น flag (Durango.Network/MotionOption):
        ///   1 LOOPING · 4 SNAP_ANGLE_BEGIN (หันทันทีตอนเริ่ม) · 8 IN_PLACE_MOTION (กัน root motion ลากตัว)
        /// ⚠️ ท่าโจมตีต้องมี 8 ไม่งั้นคลิปลากตัวไปข้างหน้าแล้วค้างผิดตำแหน่ง
        /// ⚠️ ต้องมี 4 ทั้งคู่ ไม่งั้นตัวค่อย ๆ หมุนตามทีหลัง = เห็นเป็นกัดลม
        /// </summary>
        public Move MakeMotion(string motionName, float yaw, double now,
                               double seconds = 0.6, bool loop = false)
        {
            // ท่าอยู่กับที่ = หยุดตรงจุดที่อยู่จริงตอนนี้ ไม่ใช่ปลายทางของคำสั่งเดินก่อนหน้า
            WorldPosition here = PositionAt(now);
            Position = here;
            WalkStartAt = WalkEndAt = 0;         // ไม่ได้เดินแล้ว
            Yaw = yaw;

            byte option = (byte)(loop ? 1 | 4 : 8 | 4);
            return new Move
            {
                EntityId = EntityId,
                Movements = new[]
                {
                    new Movement
                    {
                        MotionName = motionName,
                        MotionOption = option,
                        PlaybackRate = 1f,
                        RotSpeed = DefaultRotateSpeed,
                        Path = new[]
                        {
                            new Location { Position = here, Yaw = yaw, Time = now },
                            new Location { Position = here, Yaw = yaw, Time = now + seconds }
                        }
                    }
                }
            };
        }

        /// <summary>ข้อความบอกฝั่งเกมให้เปลี่ยนท่า — ใช้ตอนสถานะเปลี่ยน (ตาย/เข้าสู้)</summary>
        public Move ToMotionMessage()
        {
            double now = Gauge.CurrentTime;
            // ท่าตายเล่นรอบเดียว ท่ายืนวนลูป
            return MakeMotion(CurrentMotion, Yaw, now, IsAlive ? 2.0 : 30.0, loop: IsAlive);
        }

        /// <summary>
        /// ท่าโจมตี — สุ่มจากท่าที่ชนิดนี้มี แล้วหันหน้าเข้าหาเป้า
        ///
        /// ⚠️ ต้องหันหน้าหาเป้า ไม่งั้นตัวค้างหันไปทางที่เดินมาล่าสุด ⇒ ดูเหมือนกัดลม
        /// คืนค่าเปล่าถ้าชนิดนี้ไม่มีท่าโจมตี ⇒ ผู้เรียกเช็ค Movements ก่อนส่ง
        /// </summary>
        public Move ToAttackMotionMessage(WorldPosition targetPos, double now, Random rng)
        {
            AnimalMotions.Motions m = AnimalMotions.Of(EntityType);
            string clip = m?.PickAttack(rng);
            if (string.IsNullOrEmpty(clip)) return default;
            float yaw = YawTo(PositionAt(now), targetPos);
            return MakeMotion(clip, yaw, now, AttackClipSeconds);
        }

        /// <summary>
        /// **ค่าของเรา** — ความยาวโดยประมาณของคลิปโจมตี ครบแล้วสั่งกลับท่ายืน
        ///
        /// ⚠️ ต้องสั้นกว่า <c>attack_cooltime</c> ที่สั้นที่สุดในข้อมูล (1.3 วิ ของแรปเตอร์)
        /// ไม่งั้นสัตว์สั่งตีรอบใหม่ก่อนที่รอบเก่าจะได้กลับท่ายืน ⇒ ค้างท่าตีค้างตลอด
        /// </summary>
        public const double AttackClipSeconds = 0.8;

        /// <summary>
        /// แปลงเป็นข้อความที่เกมรอรับ
        ///
        /// รูปร่างนี้ลอกจากตัวเกมเองที่ <c>client/AnimalManager.cs:93-129</c> — เป็นเมธอดดีบัก
        /// <c>MakeAnimal</c> ที่ NEXON เขียนไว้สร้างสัตว์ปลอมในเครื่อง ⇒ **บอกตรง ๆ ว่าฟิลด์
        /// ขั้นต่ำที่เกมต้องมีคืออะไร** (EntityId · EntityType · IsAlive · Move 1 ท่อน ·
        /// Survival ที่มีหลอด Life · Display ที่มี BaseScale) ไม่ต้องเดา
        /// </summary>
        public AppearAnimal ToMessage() => new()
        {
            EntityId = EntityId,
            EntityType = EntityType,
            IsAlive = IsAlive,
            Level = CombatLevel,
            Move = new Move
            {
                EntityId = EntityId,
                Movements = new[]
                {
                    new Movement
                    {
                        // ⚠️ ต้องมี MotionName เป็น **ชื่อ AnimationClip จริง** ไม่งั้นสัตว์นิ่งสนิท
                        // client/AnimalBehavior.cs:1007-1011 PlayAnimationMovement return ทันที
                        // ถ้าชื่อว่าง และ AnimalBehavior.Update() ไม่มีตรรกะเล่นท่ายืนเองเลย
                        // ชื่อ clip ถอดจาก asset ของเกมเอง — ดู Support/AnimalMotions.cs
                        MotionName = CurrentMotion,
                        MotionOption = (byte)CurrentMotionOption,   // ท่ายืนต้องวนซ้ำ ไม่งั้นเล่นจบแล้วค้าง
                        PlaybackRate = 1f,                          // 0 = หยุดนิ่ง (ค่าปริยายของ struct)
                        RotSpeed = DefaultRotateSpeed,
                        // ⚠️ ต้องใส่ Yaw ไม่งั้นสัตว์ที่โผล่มาหันไปทางเหนือหมดทุกตัว
                        // (client/PathMovable.cs:154 TurnToYaw(value.Yaw, bSnap: true) — 0 = หันเหนือ)
                        Path = new[] { new Location { Position = Position, Yaw = Yaw, Time = Gauge.CurrentTime } }
                    }
                }
            },
            Survival = new Survival
            {
                EntityId = EntityId,
                Life = new Gauge(LifeMax, 0f, new[] { new GaugeNode(Gauge.CurrentTime, Life) }),
                Gauges = new Dictionary<string, Gauge>()
            },
            Display = new AnimalDisplay
            {
                EntityId = EntityId,
                // ⚠️ ห้ามส่ง 1.0 ตายตัว — ฝั่งเกมเอาไปตั้ง transform.localScale ตรง ๆ
                // (client/AnimalManager.cs:153) มีแค่ 29 จาก 214 ชนิดที่ขนาด 1.0 จริง
                BaseScale = AnimalTypes.Get(EntityType)?.BaseScale ?? 1f
            }
        };
    }

    /// <summary>
    /// ค่าเดินเล่น — **เอาจาก client ต้นฉบับตรง ๆ** ไม่ได้ตั้งเอง
    /// client/ClientAnimalActor.cs:23 <c>_wanderRadius = 500f</c> (2.5 ช่อง)
    /// client/ClientAnimalActor.cs:28 <c>_movingSpeed = 100f</c> (หน่วยต่อวินาที)
    /// ข้อมูล entity_types/animal.json ไม่มีความเร็วเดินของสัตว์เลย (มีแต่ *_velocity ของหลอด)
    /// </summary>
    private const float WanderRadius = 500f;
    private const float MovingSpeed = 100f;

    /// <summary>**ค่าของเรา** — หยุดพักกี่วินาทีระหว่างเดินแต่ละรอบ (สุ่มในช่วงนี้)</summary>
    private const double WanderPauseMin = 4.0;
    private const double WanderPauseMax = 12.0;

    private readonly Random _rng = new();

    private readonly List<Animal> _animals = new();
    private readonly Dictionary<string, Animal> _byId = new(StringComparer.Ordinal);

    public IReadOnlyList<Animal> All => _animals;

    public int Count => _animals.Count;

    public AnimalManager(TerrainData terrain, RegionCatalog.TemplateInfo template)
    {
        Spawn(terrain, template);
    }

    /// <summary>
    /// รอบเดินเล่นของสัตว์ทั้งเกาะ — เรียกจาก World.Process ทุกเฟรม (ทำงานจริงเป็นช่วง ๆ)
    ///
    /// ทำไมเซิร์ฟต้องเป็นคนเดินให้: <c>AnimalBehavior.Update()</c> ฝั่งเกมไม่มีตรรกะขยับเองเลย
    /// (ตัวที่เดินเองคือ ClientAnimalActor ซึ่งใช้กับสัตว์ประดับที่ terrain วาง ไม่ใช่สัตว์จากเซิร์ฟ)
    /// ⇒ ไม่ส่ง Move มา สัตว์จะยืนแช่อยู่จุดเดิมตลอดกาล
    ///
    /// วิธี: เดินไปจุดสุ่มในรัศมีรอบจุดเกิด → พอถึงเวลาก็กลับไปยืน → พักแล้วเดินใหม่
    /// สัตว์ที่กำลังโกรธใครอยู่ไม่เดินเล่น (มันควรจ้องเป้าหมาย)
    /// </summary>
    public void Process(double now, Action<Move> broadcast)
    {
        if (broadcast == null) return;
        foreach (Animal animal in _animals)
        {
            if (!animal.IsAlive) continue;

            // [7 ก.ย. 2026] ตีจบแล้ว — ดึงกลับท่ายืนที่ตำแหน่งจริง
            //
            // ⚠️ ไม่มีตรงนี้ = คลิปโจมตีเล่นจบแล้วไม่มีอะไรพากลับ ตัวค้างท่าตีค้างผิดตำแหน่ง
            // (คลิปโจมตีลาก root bone ไปข้างหน้า) แล้ว packet ถัดไปกระชากกลับ = เห็นเป็นวาร์ป
            if (animal.StandAt > 0.0 && now >= animal.StandAt)
            {
                animal.StandAt = 0.0;
                broadcast(animal.ToMotionMessage());
                continue;
            }
            if (animal.StandAt > 0.0) continue;      // ท่าโจมตียังเล่นไม่จบ อย่าสั่งอะไรทับ

            // ถึงเวลาหยุดเดินแล้ว — กลับไปท่ายืน
            if (animal.StopWalkingAt > 0.0 && now >= animal.StopWalkingAt)
            {
                animal.StopWalkingAt = 0.0;
                animal.NextWanderAt = now + WanderPauseMin + _rng.NextDouble() * (WanderPauseMax - WanderPauseMin);
                // [7 ก.ย. 2026] กำลังไล่ล่าอยู่ = อย่าส่งท่ายืนทับ
                // ⚠️ ตัวนี้เคยทำให้การวิ่งเข้าหาเหยื่อพังทั้งระบบ: Player.Hunting สั่งวิ่ง
                // แล้วรอบถัดมาที่นี่ส่ง "ท่ายืนที่ตำแหน่งใหม่" ทับทันที ⇒ บนจอเห็นสัตว์
                // วาร์ปกลับไปยืนนิ่ง แล้วตีข้ามระยะเหมือนเดิม
                // ปล่อยให้ AnimalTurn เป็นคนตัดสินใจรอบถัดไป (เดินต่อ หรือกัด)
                if (string.IsNullOrEmpty(animal.AggroTargetId))
                {
                    broadcast(animal.ToMotionMessage());
                }
                continue;
            }

            if (animal.StopWalkingAt > 0.0) continue;                 // เดินอยู่ ปล่อยให้เดินต่อ
            if (!string.IsNullOrEmpty(animal.AggroTargetId)) continue; // โกรธอยู่ ไม่เดินเล่น
            if (now < animal.NextWanderAt) continue;

            Move msg = BuildWander(animal, now);
            if (msg.Movements == null) { animal.NextWanderAt = now + WanderPauseMin; continue; }
            broadcast(msg);
        }
    }

    /// <summary>
    /// สร้างเส้นทางเดินไปจุดสุ่มรอบบ้าน — รูปแบบเดียวกับที่ตัวเกมสร้างเองใน
    /// client/ClientAnimalActor.cs:172-230 (จุดเริ่ม + จุดจบ พร้อมเวลาที่คำนวณจากระยะ/ความเร็ว)
    /// </summary>
    private Move BuildWander(Animal animal, double now)
    {
        AnimalMotions.Motions motions = AnimalMotions.Of(animal.EntityType);
        string walk = motions?.Move;
        if (string.IsNullOrEmpty(walk)) return default;               // ไม่มีท่าเดิน = อย่าให้ขยับแบบไถลไป

        double angle = _rng.NextDouble() * Math.PI * 2.0;
        float radius = (float)(_rng.NextDouble() * WanderRadius);
        var home = new WorldPosition(animal.HomeTile.x * TileSize, animal.HomeTile.y * TileSize);
        var dest = new WorldPosition(home.x + (float)(Math.Cos(angle) * radius),
                                     home.y + (float)(Math.Sin(angle) * radius));

        // จุดเริ่มต้องเป็น "ที่ที่มันอยู่จริงตอนนี้" ไม่ใช่ปลายทางของคำสั่งก่อนหน้า
        // ไม่งั้นฝั่งเกมกระโดดไปข้างหน้าทันทีที่ได้ packet
        WorldPosition from = animal.PositionAt(now);
        float dx = dest.x - from.x, dy = dest.y - from.y;
        float distance = (float)Math.Sqrt(dx * dx + dy * dy);
        if (distance < 1f) return default;

        double travel = distance / MovingSpeed;
        float yaw = Animal.YawTo(from, dest);   // ต้องเป็น 0-360 ไม่งั้นตัวหันผิดด้าน

        // ขยับตำแหน่งฝั่งเซิร์ฟตามไปด้วย ไม่งั้นระยะไล่กัด/ระยะจับจะอ้างจุดเก่า
        animal.Position = dest;
        animal.StopWalkingAt = now + travel;
        animal.Yaw = yaw;
        animal.WalkFrom = from;
        animal.WalkTo = dest;
        animal.WalkStartAt = now;
        animal.WalkEndAt = now + travel;

        return new Move
        {
            EntityId = animal.EntityId,
            Movements = new[]
            {
                new Movement
                {
                    MotionName = walk,
                    MotionOption = (byte)(MotionOption.LOOPING | MotionOption.ALIGN_TO_PATH),
                    PlaybackRate = 1f,
                    RotSpeed = DefaultRotateSpeed,
                    Path = new[]
                    {
                        // ⚠️ จุดแรกต้องใช้ "ทิศปลายทาง" ด้วย — ฝั่งเกม lerp มุมจาก Path[0].Yaw
                        // ไป Path[1].Yaw ตลอดช่วงเดิน ถ้าใส่ทิศเดิมไว้จุดแรกจะเดินหันข้างทั้งเส้น
                        new Location { Position = from, Yaw = yaw, Time = now },
                        new Location { Position = dest, Yaw = yaw, Time = now + travel }
                    }
                }
            }
        };
    }

    /// <summary>
    /// [7 ก.ย. 2026] เดินเข้าหาเหยื่อ — เดิมสัตว์ยืนอยู่กับที่แล้วตีข้ามระยะ 4 ช่อง
    ///
    /// ใช้เส้นทางรูปแบบเดียวกับ <see cref="BuildWander"/> ต่างแค่ปลายทางเป็นตำแหน่งเหยื่อ
    /// หยุดห่างจากเหยื่อ <paramref name="stopAtDistance"/> หน่วย เพื่อไม่ให้เดินทับตัวผู้เล่น
    ///
    /// คืนค่าเปล่าถ้าไม่มีท่าเดิน หรืออยู่ใกล้พอแล้ว ⇒ ผู้เรียกเช็ค Movements ก่อนส่ง
    /// </summary>
    public Move BuildChase(Animal animal, WorldPosition target, float stopAtDistance, double now)
    {
        AnimalMotions.Motions motions = AnimalMotions.Of(animal.EntityType);
        string walk = motions?.Move;
        if (string.IsNullOrEmpty(walk)) return default;

        // จุดเริ่มต้องเป็นตำแหน่งจริงตอนนี้ ไม่ใช่ปลายทางของคำสั่งเดินก่อนหน้า
        WorldPosition from = animal.PositionAt(now);
        float dx = target.x - from.x, dy = target.y - from.y;
        float distance = (float)Math.Sqrt(dx * dx + dy * dy);
        if (distance <= stopAtDistance) return default;      // ประชิดแล้ว ไม่ต้องเดิน

        // เดินไปหยุดที่ขอบระยะประชิด ไม่ใช่ทับตัวผู้เล่น
        float ratio = (distance - stopAtDistance) / distance;
        var dest = new WorldPosition(from.x + dx * ratio, from.y + dy * ratio);

        double travel = (distance - stopAtDistance) / MovingSpeed;
        if (travel <= 0.01) return default;
        float yaw = Animal.YawTo(from, dest);

        animal.Position = dest;
        animal.StopWalkingAt = now + travel;
        animal.Yaw = yaw;
        animal.WalkFrom = from;
        animal.WalkTo = dest;
        animal.WalkStartAt = now;
        animal.WalkEndAt = now + travel;

        return new Move
        {
            EntityId = animal.EntityId,
            Movements = new[]
            {
                new Movement
                {
                    MotionName = walk,
                    MotionOption = (byte)(MotionOption.LOOPING | MotionOption.ALIGN_TO_PATH),
                    PlaybackRate = 1f,
                    RotSpeed = DefaultRotateSpeed,
                    Path = new[]
                    {
                        new Location { Position = from, Yaw = yaw, Time = now },
                        new Location { Position = dest, Yaw = yaw, Time = now + travel }
                    }
                }
            }
        };
    }

    public Animal Get(string entityId) =>
        entityId != null && _byId.TryGetValue(entityId, out Animal animal) ? animal : null;

    /// <summary>
    /// [7 ก.ย. 2026] เสกสัตว์หนึ่งตัวลงตรงจุดที่สั่ง — ใช้กับคำสั่ง cheat "animal" เท่านั้น
    ///
    /// มีไว้เพราะสัตว์ตามฝูงเกิดกระจายทั่วเกาะ กว่าจะเดินไปเจอตัวหนึ่งใช้เวลานาน
    /// ทำให้ทดสอบเรื่องอนิเมชั่น/การไล่กัด/การตายซ้ำ ๆ ไม่ไหว
    ///
    /// id ใส่เลขไล่ไว้กันชนกับฝูงของเกาะ (ซึ่งใช้รูปแบบ herd_&lt;กลุ่ม&gt;_&lt;ลำดับ&gt;)
    /// คืน null ถ้าชนิดนั้นไม่มีในข้อมูล ⇒ ผู้เรียกต้องเช็คก่อนส่งเข้าเกม
    /// </summary>
    public Animal SpawnAt(ushort entityType, int combatLevel, Point2 tile)
    {
        var spawn = new RegionCatalog.HerdSpawn(entityType, combatLevel);
        Animal animal = Create($"cheat_{entityType}_{++_cheatSpawnCount}", spawn, tile);
        if (animal == null) return null;
        _animals.Add(animal);
        _byId[animal.EntityId] = animal;
        return animal;
    }

    private int _cheatSpawnCount;

    /// <summary>
    /// สร้างสัตว์ตามฝูงที่แม่แบบสั่ง
    ///
    /// id ของสัตว์ผูกกับ (กลุ่ม, ลำดับ) เช่น <c>herd_land_7</c> ให้คงที่ทุกครั้งที่เปิดเซิร์ฟ
    /// เหตุผลเดียวกับ POI: ถ้าไล่เลขใหม่ทุกรอบ ระบบที่อ้างถึงสัตว์ตัวเดิมจะหลงทันที
    /// </summary>
    private void Spawn(TerrainData terrain, RegionCatalog.TemplateInfo template)
    {
        if (terrain?.Herds == null || template == null || template.Herds.Count == 0)
        {
            return;
        }

        // กระจายโควตาให้ทุกกลุ่มตามสัดส่วนที่แม่แบบสั่ง แทนที่จะเติมกลุ่มแรกจนเต็มแล้วกลุ่มหลังไม่ได้เลย
        int wanted = 0;
        foreach (KeyValuePair<string, List<RegionCatalog.HerdSpawn>> group in template.Herds)
        {
            wanted += Math.Min(group.Value.Count, terrain.Herds.Of(group.Key).Count);
        }
        if (wanted == 0) return;

        foreach (KeyValuePair<string, List<RegionCatalog.HerdSpawn>> group in template.Herds)
        {
            IReadOnlyList<Point2> points = terrain.Herds.Of(group.Key);
            int available = Math.Min(group.Value.Count, points.Count);
            if (available == 0) continue;

            int quota = Math.Max(1, (int)Math.Round(MaxAnimalsPerRegion * (double)available / wanted));
            quota = Math.Min(quota, available);

            // เลือกแบบเว้นระยะเท่า ๆ กันทั้งลิสต์ ไม่ใช่ตัดเอาแค่ต้นลิสต์
            // ⇒ สัตว์กระจายทั่วเกาะ ไม่กระจุกอยู่มุมเดียว · และผลเหมือนเดิมทุกครั้ง (ไม่ใช้สุ่ม)
            double stride = (double)available / quota;
            for (int n = 0; n < quota; n++)
            {
                int i = Math.Min(available - 1, (int)(n * stride));
                Animal animal = Create($"herd_{group.Key}_{i}", group.Value[i], points[i]);
                if (animal == null) continue;
                _animals.Add(animal);
                _byId[animal.EntityId] = animal;
            }
        }

        if (_animals.Count > 0)
        {
            Console.WriteLine($"[สัตว์] เกาะ {terrain.Info?.region_template ?? "?"} เกิดสัตว์ {_animals.Count} ตัว " +
                              $"(แม่แบบสั่งไว้ {wanted} ฝูง · เพดานตอนนี้ {MaxAnimalsPerRegion})");
        }
    }

    /// <summary>สุ่มทิศตอนเกิด — แยกจาก _rng เพราะ Create เป็น static</summary>
    private static readonly Random SpawnYawRng = new();

    private static Animal Create(string entityId, RegionCatalog.HerdSpawn spawn, Point2 tile)
    {
        AnimalTypes.Info info = AnimalTypes.Get(spawn.EntityType);
        if (info == null)
        {
            return null;    // ชนิดที่ไม่มีในข้อมูล — ส่งไปเกมก็โหลดโมเดลไม่ได้ (AnimalLoadFailed)
        }

        // ~7% ของเลขในไฟล์ถอดแล้วเลเวลหลุดช่วงของสัตว์ตัวนั้น ⇒ หนีบเข้าช่วง ไม่ทิ้งทั้งฝูง
        int level = Math.Clamp(spawn.CombatLevel, info.MinCombatLevel, info.MaxCombatLevel);

        var vars = new Dictionary<string, double>
        {
            ["combat_level"] = level,
            ["unstable_factor"] = StableFactor
        };

        // ถ้าสูตรอ่านไม่ออกจริง ๆ ให้ใช้ค่าสำรองที่ "ไม่ทำให้เกมพัง" แล้วบ่นออกล็อก
        // (life 1 = ตีทีเดียวตาย ดีกว่าสัตว์อมตะที่ไม่มีใครรู้ว่าทำไม)
        float lifeMax = (float)Math.Max(1.0, StatFormula.EvalOr(info.LifeMax, vars, 1.0));

        return new Animal
        {
            EntityId = entityId,
            EntityType = spawn.EntityType,
            CombatLevel = level,
            Position = new WorldPosition(tile.x * TileSize, tile.y * TileSize),
            HomeTile = tile,
            // **ค่าของเรา** — สุ่มทิศตอนเกิด ไม่งั้นทั้งฝูงหันหน้าไปทางเหนือเรียงกันหมด
            Yaw = (float)(SpawnYawRng.NextDouble() * 360.0),
            LifeMax = lifeMax,
            Life = lifeMax,
            Attack = (float)Math.Max(0.0, StatFormula.EvalOr(info.Attack, vars, 0.0)),
            Defense = (float)Math.Max(0.0, StatFormula.EvalOr(info.Defense, vars, 0.0))
        };
    }
}
