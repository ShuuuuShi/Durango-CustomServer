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
    /// ความเร็วหมุนตัวของสัตว์ — ค่าเดียวกับที่ตัวเกมใช้กับสัตว์ที่มันขยับเอง
    /// (client/ClientAnimalActor.cs:31 <c>_rotateSpeed = 100f</c>)
    /// ส่ง 0 ไปฝั่งเกมจะ fallback เป็น 300 (AnimalBehavior.SetRotateSpeed) ซึ่งหมุนเร็วผิดปกติ
    /// </summary>
    private const float DefaultRotateSpeed = 100f;

    /// <summary>สัตว์ป่าหนึ่งตัวบนเกาะ</summary>
    public class Animal
    {
        public string EntityId;
        public ushort EntityType;
        public int CombatLevel;
        public Point2 Tile;
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

        /// <summary>จุดที่มันเกิด — เดินเล่นวนอยู่รอบ ๆ จุดนี้ ไม่หลุดไปไกล</summary>
        public Point2 HomeTile;

        /// <summary>ถึงเวลาออกเดินรอบถัดไปเมื่อไร</summary>
        public double NextWanderAt;

        /// <summary>ถึงเวลาหยุดเดินแล้วกลับไปยืน (0 = ไม่ได้เดินอยู่)</summary>
        public double StopWalkingAt;

        public WorldPosition Position => new(Tile.x * TileSize, Tile.y * TileSize);

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

        /// <summary>ข้อความบอกฝั่งเกมให้เปลี่ยนท่า — ใช้ตอนสถานะเปลี่ยน (ตาย/เข้าสู้)</summary>
        public Move ToMotionMessage() => new()
        {
            EntityId = EntityId,
            Movements = new[]
            {
                new Movement
                {
                    MotionName = CurrentMotion,
                    MotionOption = (byte)CurrentMotionOption,
                    PlaybackRate = 1f,
                    RotSpeed = DefaultRotateSpeed,
                    Path = new[] { new Location { Position = Position, Time = Gauge.CurrentTime } }
                }
            }
        };

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
                        Path = new[] { new Location { Position = Position, Time = Gauge.CurrentTime } }
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

            // ถึงเวลาหยุดเดินแล้ว — กลับไปท่ายืน
            if (animal.StopWalkingAt > 0.0 && now >= animal.StopWalkingAt)
            {
                animal.StopWalkingAt = 0.0;
                animal.NextWanderAt = now + WanderPauseMin + _rng.NextDouble() * (WanderPauseMax - WanderPauseMin);
                broadcast(animal.ToMotionMessage());
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

        WorldPosition from = animal.Position;
        float dx = dest.x - from.x, dy = dest.y - from.y;
        float distance = (float)Math.Sqrt(dx * dx + dy * dy);
        if (distance < 1f) return default;

        double travel = distance / MovingSpeed;
        float yaw = (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI);    // ฝั่งเกมนับ yaw จากแกน +Z

        // ขยับตำแหน่งฝั่งเซิร์ฟตามไปด้วย ไม่งั้นระยะไล่กัด/ระยะจับจะอ้างจุดเก่า
        animal.Tile = new Point2((int)Math.Round(dest.x / TileSize), (int)Math.Round(dest.y / TileSize));
        animal.StopWalkingAt = now + travel;

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
            Tile = tile,
            HomeTile = tile,
            LifeMax = lifeMax,
            Life = lifeMax,
            Attack = (float)Math.Max(0.0, StatFormula.EvalOr(info.Attack, vars, 0.0)),
            Defense = (float)Math.Max(0.0, StatFormula.EvalOr(info.Defense, vars, 0.0))
        };
    }
}
