"""
ตารางธีมเกาะ — สกัดจากไฟล์เกาะจริงของเกม ห้ามแก้ค่าด้วยมือ

สร้างโดยอ่าน server/data/terrains/extracted/<เกาะ>/ ของจริงทั้ง 13 เกาะ แล้วนับสถิติออกมา
(biome id · ชนิดของธรรมชาติใน whole.garden · id หน้าผาใน whole.landmarks · ความหนาแน่น)
ค่าทุกตัวจึงเป็นค่าที่เกมใช้จริง ไม่ใช่ค่าที่แต่งขึ้น
"""

# Shared.Region.Biome — enum จริงจาก client/Shared.Region/Biome.cs
TEMPERATE_FOREST = 0
TROPICAL_FOREST = 1
DESERT = 2
TUNDRA = 3
SNOW_FIELD = 4
GRASSLAND = 5
SWAMP_MUD = 6
VOLCANIC = 7
PEBBLE_BEACH = 9
SAND_BEACH = 10
COLD_OCEAN = 11
WARM_OCEAN = 12
RIVER = 13
LAKE = 14
LAVA = 15

#: ธงในไบต์บนของ whole.biomes — 0xC0 = หน้าผา (ยืนยันจาก cliffs.dm ของเกาะจริง)
FLAG_CLIFF = 0xC0

#: ชื่อ + สีพรีวิวของแต่ละ biome (id ตรงกับ enum ของเกม)
BIOME_INFO = {
    0:  ("ป่าเขตอบอุ่น",  (45, 90, 39)),
    1:  ("ป่าเขตร้อน",   (34, 120, 34)),
    2:  ("ทะเลทราย",     (222, 194, 128)),
    3:  ("ทุนดรา",       (152, 164, 152)),
    4:  ("ทุ่งหิมะ",     (238, 240, 243)),
    5:  ("ทุ่งหญ้า",     (124, 176, 74)),
    6:  ("หนองน้ำ",      (74, 92, 58)),
    7:  ("ภูเขาไฟ",      (86, 54, 48)),
    9:  ("หาดกรวด",      (150, 146, 136)),
    10: ("หาดทราย",      (226, 208, 160)),
    11: ("ทะเลเย็น",     (48, 96, 132)),
    12: ("ทะเลอุ่น",     (40, 104, 148)),
    13: ("แม่น้ำ",       (74, 144, 184)),
    14: ("ทะเลสาบ",      (58, 122, 184)),
    15: ("ลาวา",         (196, 72, 32)),
}

WATER_BIOMES = frozenset((COLD_OCEAN, WARM_OCEAN, RIVER, LAKE, LAVA))
BEACH_BIOMES = frozenset((PEBBLE_BEACH, SAND_BEACH))

#: ธีมเกาะ — key คือชื่อธีม, ค่าที่เห็นทั้งหมดอ่านมาจากเกาะที่ระบุใน "sample"
THEMES = {
    'temperate': {
        "label": 'ป่าเขตอบอุ่น',
        "sample": 'ri35te',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 0, "beach": 10,
        "ocean_biome": 'warm_ocean', "lake_biome": 'temperate_forest', "river_biome": 'temperate_forest',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (2000, 'Models/Cliff/Common/cliff_common_a.prefab'),
            (2001, 'Models/Cliff/Common/cliff_common_b.prefab'),
            (2002, 'Models/Cliff/Common/cliff_common_c.prefab'),
        ],
        "landmark_count": 50,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0406,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (12121, 346, 'shore'),
            (11018, 277, 'land'),
            (11002, 247, 'land'),
            (11032, 236, 'land'),
            (14004, 226, 'land'),
            (12119, 219, 'sea'),
            (13000, 186, 'land'),
            (12003, 133, 'land'),
            (12120, 108, 'land'),
            (11009, 100, 'land'),
            (13047, 94, 'land'),
            (11013, 72, 'land'),
            (11031, 61, 'land'),
            (12000, 52, 'land'),
            (11021, 52, 'land'),
            (14005, 35, 'land'),
            (13044, 35, 'land'),
            (12118, 34, 'land'),
            (11004, 30, 'land'),
            (11026, 24, 'land'),
            (13014, 18, 'land'),
            (13045, 15, 'land'),
        ],
        "shares": {'12': 57.22, '0': 23.82, '10': 9.49, '13': 6.06, '14': 3.41},
    },
    'tropical': {
        "label": 'ป่าเขตร้อน',
        "sample": 'ri40tr',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 1, "beach": 10,
        "ocean_biome": 'warm_ocean', "lake_biome": 'tropical_forest', "river_biome": 'temperate_forest',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (2009, 'Models/Cliff/Tropical/cliff_tropical_a.prefab'),
            (2010, 'Models/Cliff/Tropical/cliff_tropical_b.prefab'),
            (2014, 'Models/Cliff/Tropical/cliff_tropical_c.prefab'),
        ],
        "landmark_count": 50,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0683,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (12174, 1919, 'sea'),
            (12121, 357, 'shore'),
            (12013, 229, 'land'),
            (11030, 198, 'land'),
            (11003, 172, 'land'),
            (11040, 155, 'land'),
            (11025, 152, 'land'),
            (12175, 125, 'land'),
            (11033, 112, 'land'),
            (13002, 109, 'land'),
            (14009, 102, 'land'),
            (11100, 99, 'land'),
            (11028, 97, 'land'),
            (14021, 83, 'land'),
            (14020, 78, 'land'),
            (14022, 68, 'land'),
            (12010, 62, 'land'),
            (14024, 60, 'land'),
            (11031, 46, 'land'),
            (14007, 42, 'land'),
            (13047, 37, 'land'),
            (14003, 30, 'land'),
        ],
        "shares": {'12': 53.43, '1': 29.18, '10': 11.42, '13': 3.26, '14': 2.72},
    },
    'grassland': {
        "label": 'ทุ่งหญ้า',
        "sample": 'pe10gr_1',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 5, "beach": 10,
        "ocean_biome": 'warm_ocean', "lake_biome": 'grassland', "river_biome": 'temperate_forest',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (2018, 'Models/Cliff/Grassland/cliff_grassland_a.prefab'),
            (2019, 'Models/Cliff/Grassland/cliff_grassland_b.prefab'),
            (2020, 'Models/Cliff/Grassland/cliff_grassland_c.prefab'),
        ],
        "landmark_count": 50,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0366,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (11018, 407, 'land'),
            (13000, 384, 'land'),
            (11032, 310, 'land'),
            (12121, 291, 'shore'),
            (12119, 199, 'sea'),
            (14012, 182, 'land'),
            (12003, 146, 'land'),
            (11002, 138, 'land'),
            (11031, 96, 'land'),
            (12120, 82, 'land'),
            (12000, 68, 'land'),
            (14010, 49, 'land'),
            (11023, 21, 'land'),
            (13047, 15, 'land'),
            (13044, 6, 'land'),
            (13045, 3, 'land'),
        ],
        "shares": {'12': 54.99, '5': 29.7, '10': 10.67, '14': 3.31, '13': 1.33},
    },
    'savanna': {
        "label": 'สะวันนา',
        "sample": 'ri45sa',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 5, "beach": 10,
        "ocean_biome": 'warm_ocean', "lake_biome": 'grassland', "river_biome": 'temperate_forest',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (2024, 'Models/Cliff/Savanna/cliff_savanna_a.prefab'),
            (2025, 'Models/Cliff/Savanna/cliff_savanna_b.prefab'),
            (2026, 'Models/Cliff/Savanna/cliff_savanna_c.prefab'),
        ],
        "landmark_count": 50,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0741,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (12174, 2028, 'sea'),
            (14057, 874, 'land'),
            (14056, 432, 'land'),
            (12121, 319, 'shore'),
            (14066, 218, 'land'),
            (13000, 213, 'land'),
            (14052, 204, 'land'),
            (14065, 133, 'land'),
            (14069, 91, 'land'),
            (12003, 86, 'land'),
            (12000, 43, 'land'),
            (12175, 41, 'land'),
            (14068, 36, 'land'),
            (14070, 34, 'land'),
            (14067, 30, 'land'),
            (13047, 18, 'land'),
            (13014, 15, 'land'),
            (14060, 10, 'land'),
            (12173, 9, 'land'),
            (14059, 9, 'land'),
            (15001, 5, 'land'),
            (13006, 4, 'land'),
        ],
        "shares": {'12': 61.9, '5': 22.21, '10': 13.61, '14': 1.19, '13': 1.09},
    },
    'desert': {
        "label": 'ทะเลทราย',
        "sample": 'ri35de',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 2, "beach": 10,
        "ocean_biome": 'warm_ocean', "lake_biome": 'desert', "river_biome": 'temperate_forest',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (2003, 'Models/Cliff/Desert/cliff_desert_a.prefab'),
            (2004, 'Models/Cliff/Desert/cliff_desert_b.prefab'),
            (2005, 'Models/Cliff/Desert/cliff_desert_c.prefab'),
        ],
        "landmark_count": 50,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0172,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (13040, 192, 'land'),
            (11010, 165, 'land'),
            (14037, 116, 'land'),
            (11012, 88, 'land'),
            (12106, 77, 'land'),
            (12020, 70, 'land'),
            (12017, 55, 'land'),
            (11080, 46, 'land'),
            (11082, 38, 'land'),
            (11084, 37, 'land'),
            (14035, 35, 'land'),
            (11081, 33, 'land'),
            (11033, 25, 'land'),
            (11024, 23, 'land'),
            (11083, 21, 'land'),
            (12181, 18, 'land'),
            (13014, 17, 'land'),
            (13048, 17, 'land'),
            (11048, 13, 'land'),
            (11017, 8, 'land'),
            (13074, 6, 'land'),
            (14044, 6, 'land'),
        ],
        "shares": {'12': 58.68, '2': 26.73, '10': 13.0, '14': 1.59},
    },
    'snow': {
        "label": 'ทุ่งหิมะ',
        "sample": 'ri50sn',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 4, "beach": 9,
        "ocean_biome": 'cold_ocean', "lake_biome": 'snow_field', "river_biome": 'temperate_forest',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (2006, 'Models/Cliff/Snow/cliff_snow_a.prefab'),
            (2007, 'Models/Cliff/Snow/cliff_snow_b.prefab'),
            (2008, 'Models/Cliff/Snow/cliff_snow_c.prefab'),
        ],
        "landmark_count": 50,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0286,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (14013, 504, 'land'),
            (11039, 428, 'land'),
            (11047, 363, 'land'),
            (13042, 144, 'land'),
            (12034, 109, 'land'),
            (11046, 109, 'land'),
            (14016, 64, 'land'),
            (12031, 62, 'land'),
            (13014, 26, 'land'),
            (14027, 20, 'land'),
            (12151, 12, 'land'),
            (13029, 10, 'land'),
            (15001, 5, 'land'),
            (13074, 5, 'land'),
            (12178, 5, 'land'),
            (12179, 5, 'land'),
            (12148, 3, 'land'),
            (15002, 2, 'land'),
        ],
        "shares": {'12': 56.12, '4': 28.14, '9': 10.22, '14': 2.84, '13': 2.68},
    },
    'tundra': {
        "label": 'ทุนดรา',
        "sample": 'ri55tu',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 3, "beach": 9,
        "ocean_biome": 'cold_ocean', "lake_biome": 'tundra', "river_biome": 'temperate_forest',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (2015, 'Models/Cliff/Tundra/cliff_tundra_a.prefab'),
            (2016, 'Models/Cliff/Tundra/cliff_tundra_b.prefab'),
            (2017, 'Models/Cliff/Tundra/cliff_tundra_c.prefab'),
        ],
        "landmark_count": 30,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0237,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (13004, 278, 'land'),
            (12119, 263, 'sea'),
            (11001, 196, 'land'),
            (11032, 166, 'land'),
            (14018, 115, 'land'),
            (11002, 87, 'land'),
            (11021, 81, 'land'),
            (14015, 77, 'land'),
            (12024, 68, 'land'),
            (11019, 60, 'land'),
            (12118, 46, 'land'),
            (12027, 34, 'land'),
            (11005, 28, 'land'),
            (14026, 10, 'land'),
            (13014, 10, 'land'),
            (12144, 9, 'land'),
            (13006, 7, 'land'),
            (11020, 7, 'land'),
            (15001, 5, 'land'),
            (12141, 3, 'land'),
        ],
        "shares": {'12': 59.74, '3': 24.4, '9': 11.11, '13': 2.77, '14': 1.98},
    },
    'swamp': {
        "label": 'หนองน้ำ',
        "sample": 'ra60sw',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 6, "beach": 10,
        "ocean_biome": 'swamp_ocean', "lake_biome": 'swamp_mud', "river_biome": 'swamp_mud',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
        ],
        "landmark_count": 0,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0125,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (11057, 413, 'land'),
            (13073, 240, 'land'),
            (11056, 66, 'land'),
            (14045, 52, 'land'),
            (11059, 24, 'land'),
            (11055, 9, 'land'),
            (11054, 4, 'land'),
            (13090, 4, 'land'),
            (11060, 3, 'land'),
            (15001, 2, 'shore'),
            (15002, 1, 'land'),
        ],
        "shares": {'12': 92.54, '6': 4.55, '10': 1.79, '14': 0.83, '13': 0.29},
    },
    'volcanic': {
        "label": 'ภูเขาไฟ',
        "sample": 'ua60vol',          # เกาะจริงที่เป็นต้นแบบของตัวเลขชุดนี้
        "land": 7, "beach": 9,
        "ocean_biome": 'volcanic_ocean', "lake_biome": 'volcanic', "river_biome": 'lava',
        "cliffs": [                 # id หน้าผา -> prefab (ลง whole.landmarks)
            (1015, 'Models/Landmark/Scoop/scoop_ground_05_grass_01.prefab'),
            (2024, 'Models/Cliff/Volcanic/cliff_volcanic_a.prefab'),
            (2025, 'Models/Cliff/Volcanic/cliff_volcanic_b.prefab'),
            (2026, 'Models/Cliff/Volcanic/cliff_volcanic_c.prefab'),
            (2500, 'Models/Cliff/Volcanic/cliff_volcanic_lava_a.prefab'),
            (2501, 'Models/Cliff/Volcanic/cliff_volcanic_lava_b.prefab'),
        ],
        "landmark_count": 25,       # จำนวนหน้าผาบนเกาะต้นแบบ
        "garden_density": 0.0297,     # ของธรรมชาติต่อ tile บนเกาะต้นแบบ
        "garden": [                 # (entityType, น้ำหนัก, ถิ่นที่อยู่)
            (16028, 307, 'land'),
            (16020, 249, 'land'),
            (16004, 121, 'land'),
            (16003, 109, 'land'),
            (16011, 109, 'land'),
            (16045, 104, 'land'),
            (16042, 92, 'land'),
            (16002, 90, 'land'),
            (16043, 85, 'land'),
            (16007, 74, 'land'),
            (16022, 57, 'land'),
            (16021, 49, 'land'),
            (16001, 41, 'land'),
            (16014, 39, 'land'),
            (16005, 37, 'land'),
            (16023, 32, 'land'),
            (16000, 31, 'land'),
            (16016, 29, 'land'),
            (16029, 29, 'land'),
            (16010, 26, 'land'),
            (16038, 24, 'land'),
            (16012, 24, 'land'),
        ],
        "shares": {'12': 38.91, '7': 37.94, '9': 15.77, '15': 6.36, '14': 1.02},
    },
}

DEFAULT_THEME = "temperate"


def theme_names():
    """ชื่อธีมเรียงตามลำดับที่อยากให้โผล่ใน UI"""
    return list(THEMES)
