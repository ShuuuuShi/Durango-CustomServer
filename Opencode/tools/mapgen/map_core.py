"""
แกนสร้างแมพของ Durango — ไม่มี tkinter อยู่ในไฟล์นี้ เรียกใช้/เทสแบบ headless ได้

รูปแบบไฟล์ทุกอย่างในนี้ยืนยันมาจากเกาะจริงของเกม (server/data/terrains/extracted/):

    whole.biomes      w*h ไบต์   · [ธง 2 บิต][biome 6 บิต]  ธง 0xC0 = หน้าผา
    oceans.dm         w*h ไบต์   · signed -32..+32 · บวก = แผ่นดิน (ยิ่งมากยิ่งลึกเข้าไป)
    whole.elevations  w*h ไบต์   · ความสูงพื้น 0..~230
    whole.garden      6 ไบต์/ชิ้น · <HHH  x, y, entityType
    whole.landmarks   16 ไบต์/ชิ้น · <HHHB hhh BBB  x, y, id, rotate, off xyz, scale xyz (=51)
    info.yml          JSON       · tile_count, entry_points, landmarks (คลัง id -> prefab)

กับดักที่เคยหลงมาแล้ว อย่าพลาดซ้ำ:
  * oceans.dm ยาว w*h ไม่ใช่ (w+1)*(h+1) — ที่ยาว (w+1)*(h+1) คือ whole.ocean คนละไฟล์กัน
  * whole.landmarks ยาว 16 ไบต์/ชิ้น และ scale ต้องเป็น 51 ไม่ใช่ 0 (0 = ของหายไปเลย)
  * biome id ต้องตรง Shared.Region.Biome เท่านั้น เกาะจริงใช้ biome แค่ 4-5 ชนิดต่อเกาะ
  * จุดเข้าเกมของเกาะจริงอยู่ใน "น้ำตื้นหน้าหาด" (oceans.dm = -2..-5) ไม่ใช่บนบก
"""

import json
import math
import os
import random
import struct
from collections import deque
from datetime import datetime

import terrain_themes as T

#: ค่าที่เกมใช้: 1 tile = 16 px และ 1 chunk = 16x16 tile
TILE_SIZE = 16
CHUNK = 16

#: ขอบเขตของ oceans.dm (signed distance ถึงชายฝั่ง)
LAND_DIST_MAX = 32

#: scale ที่เกาะจริงใส่มาทุกชิ้นใน whole.landmarks
LANDMARK_SCALE = 51

#: ขนาดแมพที่ client ยอมรับ — ด่านนี้อยู่ใน client เอง (TerrainMeta.Load:
#: ถ้า tile_count[0] < 64 หรือ > 2048 จะเด้ง "TerrainMeta load failed")
MIN_TILES = 64
MAX_TILES = 2048

#: ขนาดอ้างอิงที่ใช้คิดความหนาแน่น — เกาะจริงของเกมทุกใบเป็น 256x256
REFERENCE_TILES = 256

#: จำนวนลอนของ noise ที่ใช้บิดรูปเกาะ (คิดทั้งแมพ ไม่ใช่ต่อ tile)
#: ยิ่งมากยิ่งได้อ่าวเล็ก ๆ ถี่ ๆ · 5 ลอนให้รูปเกาะที่มีคาบสมุทรใหญ่ ๆ แบบเกาะจริง
WARP_LOBES = 5.0


def clamp(v, lo, hi):
    return lo if v < lo else hi if v > hi else v


class PerlinNoise:
    """Perlin noise แบบ 2 มิติ — ถือ Random ของตัวเอง ไม่ยุ่งกับ random ตัวกลาง"""

    def __init__(self, seed):
        self.seed = seed
        rng = random.Random(seed)
        p = list(range(256))
        rng.shuffle(p)
        self.perm = p * 2

    def _fade(self, t):
        return t * t * t * (t * (t * 6 - 15) + 10)

    def _grad(self, h, x, y):
        h &= 3
        if h == 0:
            return x + y
        if h == 1:
            return -x + y
        if h == 2:
            return x - y
        return -x - y

    def noise2d(self, x, y):
        xf = math.floor(x)
        yf = math.floor(y)
        X = int(xf) & 255
        Y = int(yf) & 255
        x -= xf
        y -= yf
        u = self._fade(x)
        v = self._fade(y)
        perm = self.perm
        A = perm[X] + Y
        B = perm[X + 1] + Y
        n00 = self._grad(perm[A], x, y)
        n10 = self._grad(perm[B], x - 1, y)
        n01 = self._grad(perm[A + 1], x, y - 1)
        n11 = self._grad(perm[B + 1], x - 1, y - 1)
        a = n00 + u * (n10 - n00)
        b = n01 + u * (n11 - n01)
        return a + v * (b - a)

    def octave(self, x, y, octaves=6, persistence=0.5, lacunarity=2.0):
        total = 0.0
        freq = 1.0
        amp = 1.0
        max_val = 0.0
        for _ in range(octaves):
            total += self.noise2d(x * freq, y * freq) * amp
            max_val += amp
            amp *= persistence
            freq *= lacunarity
        return total / max_val if max_val else 0.0


class DropletErosion:
    """
    Hydraulic erosion แบบหยดน้ำ (droplet) ของจริง

    ตัวเก่าเขียนผิดตรงที่ตอนไหลลงเนินใช้ min(h_diff, sediment) ทั้งที่ sediment เริ่มที่ 0
    ทำให้กัดเซาะไม่เคยเกิดขึ้นเลย เหลือแค่ noise — รอบนี้ใช้สูตร capacity ตามมาตรฐาน:

        capacity = max(-dh * speed * water * capacity_factor, min_capacity)

    ถ้ามีตะกอนเกิน capacity (หรือกำลังไหลขึ้นเนิน) ให้ทับถม · ไม่งั้นกัดเซาะ
    """

    def __init__(self, seed, inertia=0.05, capacity_factor=4.0, min_capacity=0.01,
                 deposit_rate=0.3, erode_rate=0.3, evaporate=0.02, gravity=4.0,
                 max_lifetime=30, start_water=1.0, start_speed=1.0):
        self.rng = random.Random(seed)
        self.inertia = inertia
        self.capacity_factor = capacity_factor
        self.min_capacity = min_capacity
        self.deposit_rate = deposit_rate
        self.erode_rate = erode_rate
        self.evaporate = evaporate
        self.gravity = gravity
        self.max_lifetime = max_lifetime
        self.start_water = start_water
        self.start_speed = start_speed

    def _height_and_gradient(self, hm, w, x, y):
        """ความสูง + ความชันแบบ bilinear ที่พิกัดทศนิยม (คิดจาก 4 มุมของช่อง)"""
        xi = int(x)
        yi = int(y)
        fx = x - xi
        fy = y - yi
        i = yi * w + xi
        nw = hm[i]
        ne = hm[i + 1]
        sw = hm[i + w]
        se = hm[i + w + 1]
        gx = (ne - nw) * (1 - fy) + (se - sw) * fy
        gy = (sw - nw) * (1 - fx) + (se - ne) * fx
        height = (nw * (1 - fx) + ne * fx) * (1 - fy) + (sw * (1 - fx) + se * fx) * fy
        return height, gx, gy

    def erode(self, hm, w, h, droplets, progress=None):
        rng = self.rng
        report_every = max(1, droplets // 20)
        for n in range(droplets):
            if progress is not None and n % report_every == 0:
                progress(n / droplets)
            x = rng.uniform(1.0, w - 2.0)
            y = rng.uniform(1.0, h - 2.0)
            dx = dy = 0.0
            speed = self.start_speed
            water = self.start_water
            sediment = 0.0

            for _ in range(self.max_lifetime):
                xi = int(x)
                yi = int(y)
                cell = yi * w + xi
                fx = x - xi
                fy = y - yi

                height, gx, gy = self._height_and_gradient(hm, w, x, y)
                dx = dx * self.inertia - gx * (1 - self.inertia)
                dy = dy * self.inertia - gy * (1 - self.inertia)
                length = math.hypot(dx, dy)
                if length < 1e-9:
                    # ที่ราบสนิท — สุ่มทิศไปต่อ ไม่งั้นหยดจะค้างอยู่กับที่
                    ang = rng.uniform(0, math.tau)
                    dx, dy = math.cos(ang), math.sin(ang)
                else:
                    dx /= length
                    dy /= length

                x += dx
                y += dy
                if not (1.0 <= x < w - 2.0 and 1.0 <= y < h - 2.0):
                    break

                new_height = self._height_and_gradient(hm, w, x, y)[0]
                dh = new_height - height

                capacity = max(-dh * speed * water * self.capacity_factor, self.min_capacity)

                if sediment > capacity or dh > 0:
                    # ทับถม: ถ้าไหลขึ้นเนินให้ถมหลุมที่เพิ่งผ่านมาแค่พอเต็ม
                    amount = min(dh, sediment) if dh > 0 else (sediment - capacity) * self.deposit_rate
                    sediment -= amount
                    hm[cell] += amount * (1 - fx) * (1 - fy)
                    hm[cell + 1] += amount * fx * (1 - fy)
                    hm[cell + w] += amount * (1 - fx) * fy
                    hm[cell + w + 1] += amount * fx * fy
                else:
                    # กัดเซาะ: ห้ามขุดลึกกว่าความต่างระดับ ไม่งั้นจะเกิดหลุมเข็ม
                    amount = min((capacity - sediment) * self.erode_rate, -dh)
                    sediment += amount
                    hm[cell] -= amount * (1 - fx) * (1 - fy)
                    hm[cell + 1] -= amount * fx * (1 - fy)
                    hm[cell + w] -= amount * (1 - fx) * fy
                    hm[cell + w + 1] -= amount * fx * fy

                speed = math.sqrt(max(0.0, speed * speed + dh * self.gravity))
                water *= (1 - self.evaporate)
                if water < 0.01:
                    break
        if progress is not None:
            progress(1.0)
        return hm


DEFAULTS = {
    "width": 256,
    "height": 256,
    "theme": T.DEFAULT_THEME,
    # "map"  = ขยายภูมิประเทศตามแมพ — เกาะ 1024 คือเกาะ 256 ที่ใหญ่ขึ้น 4 เท่า
    #          ภูเขา/อ่าว/หาด ใหญ่ตามไปด้วย สัดส่วนทุกอย่างเท่าเกาะจริง (ค่าปริยาย)
    # "tile" = ขนาดภูมิประเทศคงที่ — แมพใหญ่ = ภูเขาลูกเล็กเยอะขึ้น
    #          ผลคือที่ราบยักษ์ หาดเหลือเป็นเส้นบาง (เทส 2048 ได้หาด 2.4% จาก 11%)
    "feature_scale": "map",
    "scale": 0.022,
    "octaves": 5,
    "island": 1.15,
    "warp": 0.35,
    "ocean_share": 0.56,
    "beach_share": 0.11,
    "beach_max_dist": 13,
    "cliff_share": 0.05,
    "erode": 20000,
    "smooth": 2,
    "rivers": 6,
    "river_width": 1.6,
    "lakes": 3,
    "lake_min": 12,
    "garden_scale": 1.0,
    "landmarks": 50,
}


class MapGenerator:
    """ปั่นเกาะหนึ่งใบให้ออกมาหน้าตาเหมือนเกาะจริงของเกม"""

    def __init__(self, params=None):
        p = dict(DEFAULTS)
        p.update(params or {})
        if p.get("seed") is None:
            p["seed"] = random.Random().randint(0, 999999)
        self.p = p
        self.seed = int(p["seed"])
        self.w = int(p["width"])
        self.h = int(p["height"])
        if self.w != self.h:
            # client ใช้ tile_count[0] ตัวเดียวเป็นทั้งกว้างและสูง (TerrainMeta.Load)
            # แมพไม่จัตุรัสจะเพี้ยนทั้งการวาดและพิกัด
            raise ValueError("แมพต้องเป็นจัตุรัส (กว้าง = สูง) เพราะ client ใช้ tile_count[0] ตัวเดียว")
        if not MIN_TILES <= self.w <= MAX_TILES:
            raise ValueError("ขนาดต้องอยู่ระหว่าง %d ถึง %d tile (ด่านของ client เอง)"
                             % (MIN_TILES, MAX_TILES))
        if self.w % CHUNK:
            raise ValueError("ขนาดต้องหารด้วย %d ลงตัว (client คิด ChunkCount = TileCount / %d)"
                             % (CHUNK, CHUNK))
        self.theme_name = p["theme"] if p["theme"] in T.THEMES else T.DEFAULT_THEME
        self.theme = T.THEMES[self.theme_name]
        self.rng = random.Random(self.seed)

        # ค่าอย่างจำนวนแม่น้ำ/ทะเลสาบ/หน้าผา/หยดกัดเซาะ ตั้งไว้ต่อพื้นที่ 256x256
        # ถ้าไม่คูณตามพื้นที่ เกาะ 1024 จะได้แม่น้ำ 6 สายเท่าเกาะ 256 คือโล่งจนดูไม่ออกว่ามี
        self.area_scale = (self.w * self.h) / float(REFERENCE_TILES * REFERENCE_TILES)

        n = self.w * self.h
        self.height = [0.0] * n
        self.biomes = [self.theme["land"]] * n
        self.flags = [0] * n
        self.land_dist = [0] * n
        self.elevations = [0] * n
        self.garden = []
        self.landmarks = []
        self.rivers = []
        self.entry = (self.w // 2, self.h // 2)
        self.sea_level = 0.0

    def _scaled(self, key):
        """
        คูณตามพื้นที่ — ใช้กับของที่เป็น "จุด" (แลนด์มาร์ก, หยดกัดเซาะ)

        ของพวกนี้กินพื้นที่คงที่ต่อชิ้น จำนวนจึงต้องโตตามพื้นที่ถึงจะได้ความหนาแน่นเท่าเดิม
        """
        base = float(self.p[key])
        if base <= 0:
            return 0
        return max(1, int(round(base * self.area_scale)))

    def _scaled_linear(self, key):
        """
        คูณตามด้าน — ใช้กับแม่น้ำ

        แม่น้ำหนึ่งสายบนแมพใหญ่ *ยาวขึ้นเอง* ตามด้านของแมพอยู่แล้ว ถ้าคูณจำนวนตามพื้นที่อีก
        พื้นที่แม่น้ำรวมจะโตเป็น area^1.5 (เทสจริงที่ 512 ได้แม่น้ำ 5.4% จาก 1.0%)
        คูณตามด้านแทน พื้นที่รวมถึงจะโตพอดีกับแมพ สัดส่วนคงเดิม
        """
        base = float(self.p[key])
        if base <= 0:
            return 0
        return max(1, int(round(base * math.sqrt(self.area_scale))))

    def _scaled_feature(self, key):
        """
        จำนวนของที่เป็น "ภูมิประเทศ" (แม่น้ำ, ทะเลสาบ)

        โหมด map = เกาะทั้งใบถูกขยาย แม่น้ำ/ทะเลสาบก็ใหญ่ตาม จำนวนจึงเท่าเดิม
        โหมด tile = ภูมิประเทศขนาดคงที่ แมพใหญ่ต้องมีจำนวนมากขึ้นถึงจะหนาแน่นเท่าเดิม
        """
        if self.p.get("feature_scale", "map") == "map":
            return max(0, int(self.p[key]))
        return self._scaled(key)

    def _feature_span(self):
        """ความยาวอ้างอิงของภูมิประเทศ 1 หน่วย (ใช้คิดรัศมีทะเลสาบ)"""
        if self.p.get("feature_scale", "map") == "map":
            return min(self.w, self.h)
        return REFERENCE_TILES

    def _length_scale(self):
        """
        ตัวคูณของ "ความยาวเป็น tile" ที่ตั้งค่าไว้อ้างอิงเกาะ 256

        ค่าอย่างความกว้างหาด (13 tile) กับความกว้างแม่น้ำ (1.6 tile) ถ้าไม่ขยายตามแมพ
        เกาะ 1024 จะได้หาดเป็นเส้นบาง 4.2% แทนที่จะเป็น 11% เพราะแถบริมฝั่งกว้างเท่าเดิม
        แต่พื้นที่เกาะโตขึ้น 16 เท่า (เทสจริงแล้ว — นี่คือสาเหตุที่หาดหายไป)
        """
        if self.p.get("feature_scale", "map") == "map":
            return self.w / float(REFERENCE_TILES)
        return 1.0

    # ------------------------------------------------------------------ pipeline

    def generate(self, progress=None):
        def step(name, frac):
            if progress:
                progress(name, frac)

        step("ภูมิประเทศ", 0.0)
        self._build_height()
        self._carve_lake_basins()
        step("กัดเซาะ", 0.15)
        self._erode(lambda f: step("กัดเซาะ", 0.15 + 0.35 * f))
        step("ระดับน้ำทะเล", 0.50)
        self._pick_sea_level()
        step("ทะเล/ทะเลสาบ", 0.56)
        water_kind = self._flood_water()
        step("ระยะชายฝั่ง", 0.64)
        self._coast_distance(water_kind)
        step("แม่น้ำ", 0.72)
        self._carve_rivers(water_kind)
        step("ไบโอม", 0.80)
        self._assign_biomes(water_kind)
        step("หน้าผา", 0.86)
        self._mark_cliffs()
        step("ของธรรมชาติ", 0.90)
        self._place_garden()
        step("หน้าผา/แลนด์มาร์ก", 0.95)
        self._place_landmarks()
        step("จุดเข้าเกม", 0.98)
        self._pick_entry_point(water_kind)
        self._build_elevations()
        step("เสร็จ", 1.0)
        return self

    def _build_height(self):
        w, h, p = self.w, self.h, self.p
        # โหมด "map": หารความถี่ด้วยอัตราส่วนขนาดแมพ ภูเขาจึงใหญ่ขึ้นตามเกาะ
        # ไม่งั้นเกาะ 2048 จะเป็นที่ราบแบนยักษ์ที่มีเนินจิ๋ว ๆ โรยอยู่
        sc = p["scale"]
        if p.get("feature_scale", "map") == "map":
            sc *= REFERENCE_TILES / float(w)
        octs = int(p["octaves"])
        isl = p["island"]
        warp_amt = p["warp"]

        base = PerlinNoise(self.seed)
        warp = PerlinNoise(self.seed + 7717)
        hm = self.height
        inv_w = 1.0 / w
        inv_h = 1.0 / h

        for y in range(h):
            cy = (y * inv_h - 0.5) * 2
            row = y * w
            for x in range(w):
                cx = (x * inv_w - 0.5) * 2
                e = (base.octave(x * sc, y * sc, octs) + 1) * 0.5
                # บิดหน้ากากเกาะด้วย noise อีกชั้น ไม่งั้นชายฝั่งจะกลมเป็นวงเวียน
                #
                # ความถี่ตรงนี้ต้องคิดเป็นสัดส่วนของแมพ (x/w) ไม่ใช่ต่อ tile (x*sc)
                # เพราะ d เป็นระยะที่ normalize แล้ว 0..1  ถ้าใช้ความถี่ต่อ tile
                # แมพ 512 จะได้ลอนถี่ขึ้นเท่าตัว = ขอบหยักละเอียดแต่รูปเกาะกลมเป๊ะ
                # ใช้จำนวนลอนคงที่ทั้งแมพแทน รูปเกาะถึงจะมีอ่าว/คาบสมุทรเหมือนกันทุกขนาด
                d = math.hypot(cx, cy)
                if warp_amt:
                    d += warp.octave(x * inv_w * WARP_LOBES, y * inv_h * WARP_LOBES, 3) * warp_amt
                mask = clamp(1.0 - d * isl, 0.0, 1.0)
                mask = mask * mask * (3 - 2 * mask)  # smoothstep ให้ชายฝั่งไม่หักมุม
                hm[row + x] = e * mask

    def _carve_lake_basins(self):
        """
        ขุดแอ่งไว้ก่อนกัดเซาะ เพื่อให้เกิดทะเลสาบกลางเกาะ

        ถ้าไม่ขุดไว้ แอ่งปิดจะเกิดจากการกัดเซาะล้วน ๆ ซึ่งได้แค่ 0.1% ของแมพ
        ขณะที่เกาะจริงมีทะเลสาบ 1.2-3.4%
        """
        count = self._scaled_feature("lakes")
        if count <= 0:
            return
        w, h = self.w, self.h
        hm = self.height
        # ต้องรู้ระดับน้ำคร่าว ๆ ก่อน ไม่งั้นจะไปขุดแอ่งกลางทะเล (ซึ่งไม่เกิดอะไรขึ้น)
        ordered = sorted(hm)
        sea = ordered[clamp(int(len(ordered) * self.p["ocean_share"]), 0, len(ordered) - 1)]
        margin = int(min(w, h) * 0.18)
        inland = [
            i for i in range(w * h)
            if hm[i] > sea + 0.04
            and margin <= i % w < w - margin and margin <= i // w < h - margin
        ]
        if not inland:
            return
        # ขอบทะเลสาบต้องหยัก ไม่งั้นได้บ่อกลมเป๊ะเหมือนหลุมอุกกาบาต
        # (เห็นชัดมากตอนแมพใหญ่ เพราะรัศมีโตตามแมพจนวงกลมกินพื้นที่หลายสิบ tile)
        shore = PerlinNoise(self.seed + 3300)

        for _ in range(count):
            center = self.rng.choice(inland)
            cx = float(center % w)
            cy = float(center // w)
            # รัศมีเป็นหน่วย tile จริง ไม่ผูกกับขนาดแมพ
            # เพราะความถี่ของ noise (scale) เป็นค่าสัมบูรณ์ต่อ tile อยู่แล้ว — แมพใหญ่ขึ้น
            # ไม่ได้แปลว่าภูเขาลูกใหญ่ขึ้น แต่แปลว่ามีภูเขา *จำนวนมากขึ้น*
            # ถ้าให้รัศมีโตตามแมพด้วย ทะเลสาบบนแมพ 1024 จะกลายเป็นทะเลในซึ่งผิดสเกล
            span = self._feature_span()
            radius = self.rng.uniform(span * 0.035, span * 0.075)
            # ก้นแอ่งต้องแบนและอยู่ใต้ระดับน้ำทั้งผืน — ถ้าขุดเป็นหลุมรูปกรวย
            # จะมีแค่จุดกลางที่จมน้ำ ได้ทะเลสาบเล็กกว่าที่ตั้งใจ 3-4 เท่า
            floor = sea - self.rng.uniform(0.02, 0.06)
            inner = radius * 0.7
            lobe = 4.0 / max(1.0, radius)      # ความถี่ noise ให้ได้ ~4 ลอนรอบบ่อทุกขนาด
            x0 = clamp(int(cx - radius) - 1, 0, w - 1)
            x1 = clamp(int(cx + radius) + 2, 0, w)
            y0 = clamp(int(cy - radius) - 1, 0, h - 1)
            y1 = clamp(int(cy + radius) + 2, 0, h)
            for y in range(y0, y1):
                row = y * w
                dy = y - cy
                for x in range(x0, x1):
                    dx = x - cx
                    # บิดระยะด้วย noise ~4 ลอนรอบบ่อ ทำให้ชายฝั่งทะเลสาบเว้าแหว่งเป็นธรรมชาติ
                    d = math.hypot(dx, dy) + shore.octave(
                        x * lobe, y * lobe, 3) * radius * 0.32
                    if d >= radius:
                        continue
                    if d <= inner:
                        hm[row + x] = min(hm[row + x], floor)
                    else:
                        # ไล่ระดับขึ้นฝั่งให้ชายทะเลสาบไม่เป็นกำแพงตั้งฉาก
                        t = (d - inner) / (radius - inner)
                        t = t * t * (3 - 2 * t)
                        hm[row + x] = min(hm[row + x], floor + (hm[row + x] - floor) * t)

    def _erode(self, progress=None):
        n = self._scaled("erode")
        if n <= 0:
            return
        DropletErosion(self.seed + 5000).erode(self.height, self.w, self.h, n, progress)
        self._smooth(int(self.p["smooth"]))
        # การกัดเซาะทำให้ค่าหลุดช่วง 0..1 ได้ ต้องดึงกลับก่อนใช้เป็นระดับความสูง
        lo = min(self.height)
        hi = max(self.height)
        span = hi - lo
        if span < 1e-9:
            return
        inv = 1.0 / span
        self.height = [(v - lo) * inv for v in self.height]

    def _smooth(self, passes):
        """
        เกลี่ยพื้นเบา ๆ หลังกัดเซาะ

        หยดน้ำกัด/ถมทีละช่อง ผลลัพธ์ดิบ ๆ จึงเป็นหนามแหลมเต็มเขาและชายฝั่งหยักเป็นฟันเลื่อย
        เห็นชัดมากในมุม 3 มิติ — เบลอ 3x3 สองรอบก็หายโดยที่ร่องน้ำที่กัดไว้ยังอยู่ครบ
        """
        if passes <= 0:
            return
        w, h = self.w, self.h
        for _ in range(passes):
            src = self.height
            dst = list(src)
            for y in range(1, h - 1):
                row = y * w
                for x in range(1, w - 1):
                    i = row + x
                    dst[i] = (
                        src[i] * 4
                        + src[i - 1] + src[i + 1] + src[i - w] + src[i + w]
                        + (src[i - w - 1] + src[i - w + 1] + src[i + w - 1] + src[i + w + 1]) * 0.5
                    ) / 10.0
            self.height = dst

    def _pick_sea_level(self):
        """
        เลือกระดับน้ำจาก "สัดส่วนทะเลที่อยากได้" แทนการตั้งค่าคงที่

        เกาะจริงทุกใบมีทะเล 53-62% การตั้ง water level เป็นตัวเลขตายตัวแบบเดิม
        ทำให้บาง seed ได้เกาะจิ๋วกลางมหาสมุทร บาง seed ได้แผ่นดินเต็มจอ
        """
        share = clamp(self.p["ocean_share"], 0.05, 0.95)
        ordered = sorted(self.height)
        idx = int(len(ordered) * share)
        self.sea_level = ordered[clamp(idx, 0, len(ordered) - 1)]

    def _flood_water(self):
        """
        แยกน้ำออกเป็น "ทะเล" กับ "ทะเลสาบ"

        น้ำที่ต่อถึงขอบแมพ = ทะเล · แอ่งน้ำที่ไม่ต่อถึงขอบ = ทะเลสาบ
        คืนลิสต์ 0 = บก, 1 = ทะเล, 2 = ทะเลสาบ
        """
        w, h = self.w, self.h
        sea = self.sea_level
        kind = [1 if v < sea else 0 for v in self.height]
        seen = bytearray(w * h)
        q = deque()
        for x in range(w):
            for i in (x, (h - 1) * w + x):
                if kind[i] == 1 and not seen[i]:
                    seen[i] = 1
                    q.append(i)
        for y in range(h):
            for i in (y * w, y * w + w - 1):
                if kind[i] == 1 and not seen[i]:
                    seen[i] = 1
                    q.append(i)
        while q:
            i = q.popleft()
            x = i % w
            y = i // w
            if x > 0 and kind[i - 1] == 1 and not seen[i - 1]:
                seen[i - 1] = 1
                q.append(i - 1)
            if x < w - 1 and kind[i + 1] == 1 and not seen[i + 1]:
                seen[i + 1] = 1
                q.append(i + 1)
            if y > 0 and kind[i - w] == 1 and not seen[i - w]:
                seen[i - w] = 1
                q.append(i - w)
            if y < h - 1 and kind[i + w] == 1 and not seen[i + w]:
                seen[i + w] = 1
                q.append(i + w)

        # น้ำที่ไม่โดน flood = ทะเลสาบ · แอ่งเล็กเกินไปกลบทิ้งเป็นพื้นดิน
        lake_min = int(self.p["lake_min"])
        pools = {}
        pool_id = [0] * (w * h)
        next_id = 0
        for start in range(w * h):
            if kind[start] != 1 or seen[start] or pool_id[start]:
                continue
            next_id += 1
            cells = []
            stack = [start]
            pool_id[start] = next_id
            while stack:
                i = stack.pop()
                cells.append(i)
                x = i % w
                y = i // w
                for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1), (i - w, y > 0), (i + w, y < h - 1)):
                    if ok and kind[j] == 1 and not seen[j] and not pool_id[j]:
                        pool_id[j] = next_id
                        stack.append(j)
            pools[next_id] = cells

        for cells in pools.values():
            fill = 2 if len(cells) >= lake_min else 0
            for i in cells:
                kind[i] = fill
                if fill == 0:
                    # ถมแอ่งจิ๋วให้พ้นระดับน้ำ ไม่งั้นจะเป็นรูโหว่ในแผ่นดิน
                    self.height[i] = sea + 1e-4
        return kind

    def _coast_distance(self, kind):
        """
        BFS หลายต้นทางหาระยะถึงชายฝั่ง แล้วเก็บลง oceans.dm แบบ signed -32..+32

        นิยามเดียวกับเกาะจริง: บก = บวก (ยิ่งเข้ากลางเกาะยิ่งมาก) · ทะเล = ลบ
        tile ที่ติดชายฝั่งพอดีได้ +1 / -1
        """
        w, h = self.w, self.h
        INF = 1 << 30
        dist = [INF] * (w * h)
        q = deque()
        is_land = [k == 0 or k == 2 for k in kind]
        for y in range(h):
            row = y * w
            for x in range(w):
                i = row + x
                land = is_land[i]
                border = False
                if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                    border = not land
                if not border:
                    for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1), (i - w, y > 0), (i + w, y < h - 1)):
                        if ok and is_land[j] != land:
                            border = True
                            break
                if border:
                    dist[i] = 1
                    q.append(i)
        while q:
            i = q.popleft()
            d = dist[i] + 1
            if d > LAND_DIST_MAX:
                continue
            x = i % w
            y = i // w
            for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1), (i - w, y > 0), (i + w, y < h - 1)):
                if ok and is_land[j] == is_land[i] and dist[j] > d:
                    dist[j] = d
                    q.append(j)
        self.land_dist = [
            clamp(dist[i] if dist[i] < INF else LAND_DIST_MAX, 1, LAND_DIST_MAX) * (1 if is_land[i] else -1)
            for i in range(w * h)
        ]

    def _carve_rivers(self, kind):
        """
        แม่น้ำไหล *ลง* จากที่สูงไปออกทะเล

        ตัวเก่าเลือกเพื่อนบ้านที่สูงที่สุด = ปีนขึ้นเขา แม่น้ำเลยชนเพดาน 500 ก้าวทุกสาย
        """
        count = self._scaled_feature("rivers")
        if count <= 0:
            return
        w, h = self.w, self.h
        hm = self.height
        # เริ่มจากยอดที่สูงจริง ๆ สุ่มมาจาก 5% บนของแผ่นดิน
        land_cells = [i for i in range(w * h) if kind[i] == 0]
        if not land_cells:
            return
        land_cells.sort(key=lambda i: -hm[i])
        pool = land_cells[:max(1, len(land_cells) // 20)]
        max_len = (w + h) * 2

        for _ in range(count):
            start = self.rng.choice(pool)
            cur = start
            path = []
            visited = set()
            # การกัดเซาะทิ้งแอ่งปิดไว้เต็มแมพ ถ้าหยุดทันทีที่ไม่มีเพื่อนบ้านต่ำกว่า
            # แม่น้ำจะยาวแค่ 10-20 tile แล้วตายกลางเขา — ให้ปีนข้ามสันแอ่งได้บ้าง
            # (เลียนแบบทะเลสาบที่เอ่อล้นออกทางต่ำสุดของขอบแอ่ง)
            overflow_budget = 24
            for _ in range(max_len):
                if cur in visited:
                    break
                visited.add(cur)
                path.append(cur)
                if kind[cur] != 0:
                    break  # ถึงน้ำแล้ว จบสาย
                x = cur % w
                y = cur // w
                downhill = None
                downhill_h = hm[cur]
                lowest = None
                lowest_h = None
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        if dx == 0 and dy == 0:
                            continue
                        nx = x + dx
                        ny = y + dy
                        if 0 <= nx < w and 0 <= ny < h:
                            j = ny * w + nx
                            if j in visited:
                                continue
                            if hm[j] < downhill_h:
                                downhill_h = hm[j]
                                downhill = j
                            if lowest_h is None or hm[j] < lowest_h:
                                lowest_h = hm[j]
                                lowest = j
                if downhill is not None:
                    cur = downhill
                elif lowest is not None and overflow_budget > 0:
                    overflow_budget -= 1
                    cur = lowest
                else:
                    break  # ตันจริง ๆ
            if len(path) > 8:
                self.rivers.append(path)
                for i in path:
                    hm[i] = max(0.0, hm[i] - 0.004)

    def _assign_biomes(self, kind):
        """
        เกาะจริงใช้ biome แค่ 4-5 ชนิดต่อใบ (ทะเล + พื้นดินหลัก + หาด + แม่น้ำ/ทะเลสาบ)
        ไม่ใช่ Whittaker diagram เต็มรูปแบบ — ตรงนี้จึงยึดธีมเป็นหลัก
        """
        th = self.theme
        land_b = th["land"]
        beach_b = th["beach"]
        ocean_b = T.COLD_OCEAN if th["ocean_biome"] == "cold_ocean" else T.WARM_OCEAN
        n = self.w * self.h

        # หาดของเกาะจริงไม่ใช่ "ขอบหนา N tile" — ค่ามัธยฐานอยู่ที่ 3-4 tile แต่ยาวได้ถึง 13
        # คือมันคือ *ที่ราบต่ำริมฝั่ง* ไม่ใช่ระยะทางล้วน ๆ  เลยคัดจากความสูงในแถบริมฝั่ง
        # แล้วเอาเท่าที่ได้สัดส่วนตามเกาะต้นแบบ (9.5-15.8%)
        max_dist = max(1, int(round(self.p["beach_max_dist"] * self._length_scale())))
        band = [i for i in range(n) if kind[i] == 0 and self.land_dist[i] <= max_dist]
        band.sort(key=lambda i: self.height[i])
        beach = set(band[:int(n * clamp(self.p["beach_share"], 0.0, 0.5))])

        biomes = self.biomes
        for i in range(n):
            k = kind[i]
            if k == 1:
                biomes[i] = ocean_b
            elif k == 2:
                biomes[i] = T.LAKE
            elif i in beach:
                biomes[i] = beach_b
            else:
                biomes[i] = land_b
        # แม่น้ำของเกาะจริงกว้าง 2-3 tile (คิดเป็น 1-6% ของแมพ) ไม่ใช่เส้นหนา 1 tile
        w = self.w
        width = max(0.0, float(self.p["river_width"]) * self._length_scale())
        reach = int(math.ceil(width))
        w2 = width * width
        for path in self.rivers:
            for i in path:
                cx = i % w
                cy = i // w
                for dy in range(-reach, reach + 1):
                    ny = cy + dy
                    if not 0 <= ny < self.h:
                        continue
                    for dx in range(-reach, reach + 1):
                        nx = cx + dx
                        if not 0 <= nx < w or dx * dx + dy * dy > w2:
                            continue
                        j = ny * w + nx
                        if kind[j] == 0:
                            biomes[j] = T.RIVER

    def _mark_cliffs(self):
        """
        ธง 0xC0 ในไบต์ของ whole.biomes = หน้าผา (ยืนยันจาก cliffs.dm ของเกาะจริง)

        ใช้ "สัดส่วนที่อยากได้" แทนค่าความชันตายตัว เพราะความชันสัมบูรณ์ขึ้นกับ
        scale/erode ที่ผู้ใช้ปรับ — ตั้งเป็นเลขคงที่แล้วบาง seed ได้หน้าผา 12% บ้าง 0% บ้าง
        """
        w, h = self.w, self.h
        hm = self.height
        flags = self.flags
        slopes = []
        for y in range(1, h - 1):
            row = y * w
            for x in range(1, w - 1):
                i = row + x
                if self.land_dist[i] < 1:
                    continue
                gx = hm[i + 1] - hm[i - 1]
                gy = hm[i + w] - hm[i - w]
                slopes.append((math.hypot(gx, gy), i))
        if not slopes:
            return
        want = int(w * h * clamp(self.p["cliff_share"], 0.0, 0.5))
        if want <= 0:
            return
        slopes.sort(reverse=True)
        for _, i in slopes[:want]:
            flags[i] = T.FLAG_CLIFF

    def _place_garden(self):
        """
        วางของธรรมชาติตามชนิด/ความหนาแน่นของเกาะต้นแบบ

        แต่ละชนิดมี "ถิ่นที่อยู่" ที่อ่านมาจากเกาะจริง (บก / ริมหาด / ใต้ทะเล)
        เช่นสาหร่ายอยู่ในทะเลลึก ต้นไม้อยู่ลึกเข้าไปในเกาะ
        """
        th = self.theme
        entries = th["garden"]
        if not entries:
            return
        density = th["garden_density"] * self.p["garden_scale"]
        total = int(self.w * self.h * density)
        if total <= 0:
            return

        buckets = {"land": [], "shore": [], "sea": []}
        for ty, weight, hab in entries:
            buckets.setdefault(hab, []).append((ty, weight))

        cells = {"land": [], "shore": [], "sea": []}
        for i in range(self.w * self.h):
            b = self.biomes[i]
            d = self.land_dist[i]
            if b in T.WATER_BIOMES:
                if d <= -6:
                    cells["sea"].append(i)
            elif b in T.BEACH_BIOMES:
                cells["shore"].append(i)
            elif d >= 5:
                cells["land"].append(i)

        weight_sum = sum(w for _, w, _ in entries)
        used = set()
        for hab, opts in buckets.items():
            if not opts or not cells.get(hab):
                continue
            share = sum(w for _, w in opts) / weight_sum
            want = int(total * share)
            types = [t for t, _ in opts]
            weights = [w for _, w in opts]
            spots = cells[hab]
            for _ in range(want):
                i = self.rng.choice(spots)
                if i in used:
                    continue
                used.add(i)
                ty = self.rng.choices(types, weights)[0]
                self.garden.append((i % self.w, i // self.w, ty))

    def _place_landmarks(self):
        """หน้าผา (whole.landmarks) — เกาะจริงมี 25-50 ชิ้น วางบนพื้นชันกลางเกาะ"""
        cliffs = self.theme["cliffs"]
        if not cliffs:
            return
        want = self._scaled("landmarks")
        if want <= 0:
            return
        spots = [
            i for i in range(self.w * self.h)
            if self.flags[i] == T.FLAG_CLIFF and self.land_dist[i] >= 4
        ]
        if not spots:
            spots = [i for i in range(self.w * self.h) if self.land_dist[i] >= 8]
        if not spots:
            return
        self.rng.shuffle(spots)
        ids = [c[0] for c in cliffs]
        taken = []
        for i in spots:
            if len(taken) >= want:
                break
            x = i % self.w
            y = i // self.w
            # เว้นระยะกัน ไม่ให้หน้าผาซ้อนกันเป็นกอง
            if any(abs(x - px) < 6 and abs(y - py) < 6 for px, py in taken):
                continue
            taken.append((x, y))
            self.landmarks.append({"x": x, "y": y, "id": self.rng.choice(ids), "rotate": 0})

    def _pick_entry_point(self, kind):
        """
        จุดเข้าเกม = น้ำตื้นหน้าหาด (เกาะจริงทุกใบมีค่า oceans.dm อยู่ที่ -2..-5)

        ของเดิมตรึงไว้กลางแมพเฉย ๆ ซึ่งตกลงกลางทะเลหรือกลางภูเขาก็ได้
        """
        w, h = self.w, self.h
        # เกาะจริงไม่เคยวางจุดเข้าเกมชิดขอบแมพ เว้นไว้หน่อยกันเรือมาจอดนอกโลก
        margin = max(4, int(min(w, h) * 0.05))

        # [4 ก.ย. 2026] จุดเข้าเกมต้องอยู่ "หน้าหาดของแผ่นดินหลัก" ไม่ใช่เกาะเล็กแยกกลางทะเล
        # (เจอจริง: ผู้เล่นโผล่ริมน้ำข้างเกาะจิ๋วที่ไม่เชื่อมแผ่นดินใหญ่ เดินไปไหนไม่ได้)
        # หา component แผ่นดินที่ใหญ่สุดก่อน แล้วเลือกน้ำตื้นที่ติดแผ่นดินนั้น
        land = [self.land_dist[i] >= 0 for i in range(w * h)]
        comp = [-1] * (w * h)
        best_comp, best_size = -1, 0
        cid = 0
        for start in range(w * h):
            if not land[start] or comp[start] != -1:
                continue
            q = deque([start]); comp[start] = cid; size = 0
            while q:
                j = q.popleft(); size += 1
                jx, jy = j % w, j // w
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = jx + dx, jy + dy
                    if 0 <= nx < w and 0 <= ny < h:
                        n = ny * w + nx
                        if land[n] and comp[n] == -1:
                            comp[n] = cid; q.append(n)
            if size > best_size:
                best_size, best_comp = size, cid
            cid += 1

        def near_main_land(x, y, reach=3):
            for dx in range(-reach, reach + 1):
                for dy in range(-reach, reach + 1):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and comp[ny * w + nx] == best_comp:
                        return True
            return False

        candidates = []
        for i in range(w * h):
            d = self.land_dist[i]
            if -5 <= d <= -2 and self.biomes[i] in (T.WARM_OCEAN, T.COLD_OCEAN):
                x = i % w
                y = i // w
                if margin <= x < w - margin and margin <= y < h - margin and near_main_land(x, y):
                    candidates.append(i)
        if not candidates:
            # ไม่มีน้ำตื้นติดแผ่นดินหลัก — เอาน้ำตื้นทั่วไป แล้วค่อยน้ำลึก
            for i in range(w * h):
                d = self.land_dist[i]
                if -5 <= d <= -2 and self.biomes[i] in (T.WARM_OCEAN, T.COLD_OCEAN):
                    x, y = i % w, i // w
                    if margin <= x < w - margin and margin <= y < h - margin:
                        candidates.append(i)
        if not candidates:
            candidates = [i for i in range(w * h) if self.land_dist[i] < 0]
        if not candidates:
            self.entry = (w // 2, self.h // 2)
            return
        i = self.rng.choice(candidates)
        self.entry = (i % w, i // w)

    def _build_elevations(self):
        """whole.elevations — เกาะจริงอยู่ในช่วง 0..~230 และใต้น้ำเป็น 0"""
        sea = self.sea_level
        span = max(1e-9, max(self.height) - sea)
        out = self.elevations
        for i, v in enumerate(self.height):
            out[i] = 0 if v <= sea else clamp(int((v - sea) / span * 230), 0, 255)

    # -------------------------------------------------------------------- export

    def biome_bytes(self):
        return bytes((self.biomes[i] & 0x3F) | self.flags[i] for i in range(self.w * self.h))

    def ocean_bytes(self):
        return bytes(d & 0xFF for d in self.land_dist)

    def elevation_bytes(self):
        return bytes(self.elevations)

    def cliff_bytes(self):
        """
        cliffs.dm — ระยะห่างจากหน้าผาแบบมีเครื่องหมาย w*h ไบต์ (เข้ารหัสเหมือน oceans.dm)

        ลบ = อยู่ในเนื้อหิน · บวก = ห่างออกมา (สูงสุด 32)

        เซิร์ฟใช้ไฟล์นี้กันไม่ให้สัตว์เกิดทับก้อนหิน — เกาะจริงมีหิน 5.8-7.8% ของพื้นที่
        ค่าติดลบตรงกับธง 0xC0 ใน whole.biomes 100% (ตรวจกับเกาะจริงแล้ว) ที่นี่ก็สร้างคู่กัน
        """
        w, h = self.w, self.h
        n = w * h
        rock = [self.flags[i] == T.FLAG_CLIFF for i in range(n)]
        INF = 1 << 30
        dist = [INF] * n
        q = deque()
        for y in range(h):
            row = y * w
            for x in range(w):
                i = row + x
                edge = False
                for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1),
                              (i - w, y > 0), (i + w, y < h - 1)):
                    if ok and rock[j] != rock[i]:
                        edge = True
                        break
                if edge:
                    dist[i] = 1
                    q.append(i)
        while q:
            i = q.popleft()
            d = dist[i] + 1
            if d > LAND_DIST_MAX:
                continue
            x = i % w
            y = i // w
            for j, ok in ((i - 1, x > 0), (i + 1, x < w - 1),
                          (i - w, y > 0), (i + w, y < h - 1)):
                if ok and rock[j] == rock[i] and dist[j] > d:
                    dist[j] = d
                    q.append(j)
        out = bytearray(n)
        for i in range(n):
            d = clamp(dist[i] if dist[i] < INF else LAND_DIST_MAX, 1, LAND_DIST_MAX)
            out[i] = (-d if rock[i] else d) & 0xFF
        return bytes(out)

    def ocean_bytes_vertex(self):
        """
        whole.ocean — กริดจุดยอด (w+1)*(h+1) ไบต์ละ 1 จุด = "ความลึกน้ำ"

        **นี่คือไฟล์ที่ client ใช้วาดผิวน้ำ** (เซิร์ฟส่งให้เป็นก้อนละ 17x17 ผ่าน
        TerrainStore.GetChunkOcean) ไม่ใช่ oceans.dm ที่เป็นระยะห่างชายฝั่ง
        ถ้าไม่มีไฟล์นี้ เซิร์ฟจะส่งศูนย์ล้วนไปให้ client แล้วทะเลจะไม่ถูกวาดเลย

        ค่าที่วัดจาก ri40tr ของจริง:
            บก        0
            ทะเล      ไล่จาก ~62 ที่ริมฝั่ง ขึ้นไปเต็มที่ 127 เมื่อห่างฝั่งราว 10 tile
            ทะเลสาบ   186-255 (แยกย่านออกจากทะเลชัดเจน เดาว่าเป็นธง "น้ำจืด")
        """
        w, h = self.w, self.h

        def tile_value(i):
            b = self.biomes[i]
            if b == T.LAKE:
                return 200
            if b in (T.WARM_OCEAN, T.COLD_OCEAN):
                depth = -self.land_dist[i]
                return clamp(int(62 + (depth - 1) * 6.4), 0, 127)
            return 0

        cache = [tile_value(i) for i in range(w * h)]
        out = bytearray((w + 1) * (h + 1))
        for vy in range(h + 1):
            row = vy * (w + 1)
            for vx in range(w + 1):
                # จุดยอดหนึ่งจุดติดกับ tile ได้ถึง 4 ช่อง — เอาค่าน้ำที่ลึกที่สุด
                # ไม่งั้นขอบน้ำจะขาดเป็นรอยฟันหนูตรงรอยต่อบก/น้ำ
                best = 0
                for ty in (vy - 1, vy):
                    if not 0 <= ty < h:
                        continue
                    base = ty * w
                    for tx in (vx - 1, vx):
                        if 0 <= tx < w:
                            v = cache[base + tx]
                            if v > best:
                                best = v
                out[row + vx] = best
        return bytes(out)

    def river_bytes(self):
        """
        whole.rivers — 3 ไบต์ต่อจุดยอด (w+1)*(h+1)*3

        ถอดจากของจริง: จุดที่ไม่มีแม่น้ำเป็น (127, 127, 0) เป๊ะทั้งไฟล์
        จุดที่เป็นแม่น้ำมีค่าอย่าง (64, 236, 255) ⇒ สองไบต์แรกคือทิศการไหล
        เก็บแบบ 127 = ศูนย์ · ไบต์ที่สามคือความแรง (255 = เต็ม)
        """
        w, h = self.w, self.h
        flow = {}
        for path in self.rivers:
            for n, i in enumerate(path):
                nxt = path[n + 1] if n + 1 < len(path) else i
                dx = (nxt % w) - (i % w)
                dy = (nxt // w) - (i // w)
                length = math.hypot(dx, dy)
                if length < 1e-9:
                    fx = fy = 0.0
                else:
                    fx, fy = dx / length, dy / length
                flow[i] = (fx, fy)

        out = bytearray()
        for vy in range(h + 1):
            for vx in range(w + 1):
                found = None
                for ty in (vy - 1, vy):
                    if not 0 <= ty < h:
                        continue
                    base = ty * w
                    for tx in (vx - 1, vx):
                        if 0 <= tx < w and self.biomes[base + tx] == T.RIVER:
                            found = flow.get(base + tx, (0.0, 0.0))
                            break
                    if found is not None:
                        break
                if found is None:
                    out += b"\x7f\x7f\x00"
                else:
                    out.append(clamp(int(127 + found[0] * 127), 0, 255))
                    out.append(clamp(int(127 + found[1] * 127), 0, 255))
                    out.append(255)
        return bytes(out)

    def garden_bytes(self):
        return b"".join(struct.pack("<HHH", x, y, ty) for x, y, ty in self.garden)

    def landmark_bytes(self):
        return b"".join(
            struct.pack("<HHHBhhhBBB", lm["x"], lm["y"], lm["id"], lm["rotate"],
                        0, 0, 0, LANDMARK_SCALE, LANDMARK_SCALE, LANDMARK_SCALE)
            for lm in self.landmarks
        )

    def info_json(self):
        th = self.theme
        return {
            "tile_count": [self.w, self.h],
            "lake_biome": th["lake_biome"],
            "ocean_biome": th["ocean_biome"],
            "river_biome": th["river_biome"],
            "color_set": "",
            "region_template": "gen%06d" % self.seed,
            "tile_set": "",
            "entry_points": [[self.entry[0], self.entry[1]]],
            # info.yml["landmarks"] คือ *คลัง* id -> prefab ไม่ใช่ตำแหน่งที่วาง
            # (ตำแหน่งอยู่ใน whole.landmarks) — ของเดิมเขียนสลับกัน
            "landmarks": [{"id": cid, "prefab": prefab} for cid, prefab in th["cliffs"]],
            "indicators": [],
        }

    def config_json(self):
        return {
            "generated": datetime.now().isoformat(timespec="seconds"),
            "generator": "map_generator v5",
            "seed": self.seed,
            "theme": self.theme_name,
            "params": {k: v for k, v in self.p.items() if k != "seed"},
        }

    FILES = ("whole.biomes", "oceans.dm", "cliffs.dm", "whole.ocean", "whole.rivers",
             "whole.elevations", "whole.garden", "whole.landmarks",
             "info.yml", "config.yml")

    def export(self, directory):
        """เขียนไฟล์ทั้งชุดลงโฟลเดอร์เดียว คืนรายชื่อไฟล์ + ขนาด"""
        os.makedirs(directory, exist_ok=True)
        blobs = {
            "whole.biomes": self.biome_bytes(),
            "oceans.dm": self.ocean_bytes(),
            # เซิร์ฟใช้ cliffs.dm กันสัตว์เกิดทับก้อนหิน (TerrainStore.CliffDistance)
            "cliffs.dm": self.cliff_bytes(),
            # whole.ocean กับ whole.rivers คือสองไฟล์ที่เซิร์ฟสตรีมให้ client ไปวาดน้ำ
            # ไม่เขียนสองตัวนี้ = เกาะจะไม่มีน้ำให้เห็นในเกม ทั้งที่ข้อมูลฝั่งเซิร์ฟถูกหมด
            "whole.ocean": self.ocean_bytes_vertex(),
            "whole.rivers": self.river_bytes(),
            "whole.elevations": self.elevation_bytes(),
            "whole.garden": self.garden_bytes(),
            "whole.landmarks": self.landmark_bytes(),
        }
        written = []
        for name, data in blobs.items():
            path = os.path.join(directory, name)
            with open(path, "wb") as fh:
                fh.write(data)
            written.append((name, len(data)))
        for name, obj in (("info.yml", self.info_json()), ("config.yml", self.config_json())):
            path = os.path.join(directory, name)
            with open(path, "w", encoding="utf-8") as fh:
                json.dump(obj, fh, indent=2, ensure_ascii=False)
            written.append((name, os.path.getsize(path)))
        return written

    # --------------------------------------------------------------------- stats

    def stats(self):
        n = self.w * self.h
        counts = {}
        for b in self.biomes:
            counts[b] = counts.get(b, 0) + 1
        cliff = sum(1 for f in self.flags if f)
        land = sum(1 for d in self.land_dist if d > 0)
        return {
            "size": (self.w, self.h),
            "seed": self.seed,
            "theme": self.theme_name,
            "chunks": (self.w // CHUNK, self.h // CHUNK),
            "sea_level": self.sea_level,
            "biomes": sorted(((b, c, 100.0 * c / n) for b, c in counts.items()), key=lambda r: -r[1]),
            "cliff_pct": 100.0 * cliff / n,
            "land_pct": 100.0 * land / n,
            "rivers": len(self.rivers),
            "garden": len(self.garden),
            "landmarks": len(self.landmarks),
            "entry": self.entry,
            "entry_dist": self.land_dist[self.entry[1] * self.w + self.entry[0]],
            "max_land_dist": max(self.land_dist),
            "file_sizes": [
                ("whole.biomes", n),
                ("oceans.dm", n),
                ("cliffs.dm", n),
                ("whole.ocean", (self.w + 1) * (self.h + 1)),
                ("whole.rivers", (self.w + 1) * (self.h + 1) * 3),
                ("whole.elevations", n),
                ("whole.garden", len(self.garden) * 6),
                ("whole.landmarks", len(self.landmarks) * 16),
            ],
        }


class LoadedTerrain:
    """
    เกาะที่อ่านมาจากไฟล์จริง (ของเกมหรือที่เราเพิ่ง export)

    มีหน้าตาเหมือน MapGenerator พอให้ Scene 3 มิติกับหน้าสถิติใช้ได้เลย
    ประโยชน์: เอาเกาะของเกมมากางดูข้าง ๆ เกาะที่เพิ่งปั่น จะได้รู้ว่าหน้าตาใกล้กันแค่ไหน
    """

    def __init__(self, directory):
        self.directory = directory
        self.name = os.path.basename(os.path.normpath(directory))
        with open(os.path.join(directory, "info.yml"), encoding="utf-8") as fh:
            info = json.load(fh)
        self.w, self.h = info.get("tile_count", [256, 256])[:2]
        n = self.w * self.h

        def read(name, want):
            path = os.path.join(directory, name)
            if not os.path.exists(path):
                return None
            with open(path, "rb") as fh:
                data = fh.read()
            return data if len(data) >= want else None

        raw_b = read("whole.biomes", n)
        raw_o = read("oceans.dm", n)
        raw_e = read("whole.elevations", n)

        self.biomes = [(raw_b[i] & 0x3F) if raw_b else T.WARM_OCEAN for i in range(n)]
        self.flags = [(raw_b[i] & 0xC0) if raw_b else 0 for i in range(n)]
        self.land_dist = [
            (raw_o[i] - 256 if raw_o[i] > 127 else raw_o[i]) if raw_o else 0 for i in range(n)
        ]
        self.elevations = list(raw_e[:n]) if raw_e else [0] * n

        # แปลง elevation กลับเป็น height 0..1 ให้ renderer ใช้ได้
        # ใต้น้ำเกมเก็บเป็น 0 หมด จึงเดาความลึกจาก oceans.dm แทนเพื่อไม่ให้ทะเลแบนสนิท
        peak = max(self.elevations) or 1
        self.sea_level = 0.25
        self.height = [0.0] * n
        for i in range(n):
            if self.land_dist[i] > 0:
                self.height[i] = self.sea_level + self.elevations[i] / peak * (1 - self.sea_level)
            else:
                self.height[i] = self.sea_level * (1 - min(1.0, -self.land_dist[i] / 32.0) * 0.9)

        self.seed = 0
        self.theme_name = info.get("region_template") or self.name
        self.theme = T.THEMES[T.DEFAULT_THEME]
        entry = (info.get("entry_points") or [[self.w // 2, self.h // 2]])[0]
        self.entry = (entry[0], entry[1])
        self.rivers = []
        self.landmarks = []
        self.garden = []
        lm = read("whole.landmarks", 0) or b""
        if len(lm) % 16 == 0:
            for i in range(0, len(lm), 16):
                x, y, lid, rot = struct.unpack_from("<HHHB", lm, i)
                self.landmarks.append({"x": x, "y": y, "id": lid, "rotate": rot})
        gd = read("whole.garden", 0) or b""
        for i in range(0, len(gd) - 5, 6):
            self.garden.append(struct.unpack_from("<HHH", gd, i))

    def stats(self):
        n = self.w * self.h
        counts = {}
        for b in self.biomes:
            counts[b] = counts.get(b, 0) + 1
        cliff = sum(1 for f in self.flags if f)
        land = sum(1 for d in self.land_dist if d > 0)
        return {
            "size": (self.w, self.h),
            "seed": 0,
            "theme": self.theme_name,
            "chunks": (self.w // CHUNK, self.h // CHUNK),
            "sea_level": self.sea_level,
            "biomes": sorted(((b, c, 100.0 * c / n) for b, c in counts.items()), key=lambda r: -r[1]),
            "cliff_pct": 100.0 * cliff / n,
            "land_pct": 100.0 * land / n,
            "rivers": 0,
            "garden": len(self.garden),
            "landmarks": len(self.landmarks),
            "entry": self.entry,
            "entry_dist": self.land_dist[self.entry[1] * self.w + self.entry[0]],
            "max_land_dist": max(self.land_dist),
            "file_sizes": [],
        }


REAL_ISLAND_TARGETS = {
    "ocean": (53.4, 61.9),
    "land": (21.3, 37.9),
    "beach": (9.5, 15.8),
    "cliff": (3.2, 8.2),
}
