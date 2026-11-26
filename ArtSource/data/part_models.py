# IMPOSSIBLE LAB — 部品の品番（同じ種類の中の仕様違い）のデータを作る。
# 出力：Assets/_ImpossibleLab/Data/Catalog/Resources/ImpossibleLabCatalog/part_models.json（アプリとプロキシの唯一の出典）
# 実行：python ArtSource/data/part_models.py
#
# 値は市販の汎用部品のよくある値に近い「目安」。メーカー名・型番は使わない。
# size は DNA の scale と同じ決まり（m）：箱 [幅, 高さ, 奥行き]、円柱・カプセル [直径, 高さ, 直径]、球 [直径×3]、
#   円すい [下の直径, 高さ, 下の直径]、輪 [外の直径, 太さ, 外の直径]、ドーム [直径, 盛り上がり, 直径]。
# variant は 3D の見た目（部品キット KIT_<type>_<shape>-<variant>）。無ければその種類の標準の見た目。
# specs のキー：capacityWh・voltageV・powerW・lumens・rangeM・fovDeg・db・flowLpm・airflowM3h・noiseDb・torqueNm・rpm・
#   forceN・loadKg・strokeM・volumeL・inch・thrustN。ip は防水の等級（IPX の数字。不明は -1）。
import json, os

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_ImpossibleLab", "Data", "Catalog", "Resources",
                   "ImpossibleLabCatalog", "part_models.json")
M = []


def add(id, type, label, shape, size, kg, yen, ip=-1, variant=None, tags=(), **specs):
    M.append({"id": id, "type": type, "labelJa": label, "shape": shape, "size": [round(v, 4) for v in size],
              "massKg": round(kg, 4), "priceJpy": int(yen), "ip": ip, "variant": variant or "", "tags": list(tags),
              "specs": {k: round(float(v), 4) for k, v in specs.items()}})


# ---------------- 電池 ----------------
add("bat-cr2032", "battery", "コイン電池（3V）", "cylinder", (0.02, 0.0032, 0.02), 0.003, 100, variant="coin", tags=("小さい", "腕", "軽い"), capacityWh=0.66, voltageV=3)
add("bat-9v", "battery", "9V 角形電池", "box", (0.026, 0.048, 0.017), 0.045, 400, variant="cell9v", capacityWh=4.5, voltageV=9)
for n, (w, yen) in {2: (0.03, 300), 4: (0.06, 500), 6: (0.09, 700), 8: (0.06, 900)}.items():
    d = 0.031 if n != 8 else 0.062
    add(f"bat-aa{n}", "battery", f"単3 充電池 ×{n} の電池ケース", "box", (w, 0.016 if n != 8 else 0.032, 0.058), 0.028 * n + 0.01, yen,
        variant="aa", tags=("単3", "乾電池", "交換"), capacityWh=2.4 * n, voltageV=1.2 * n)
add("bat-aaa2", "battery", "単4 充電池 ×2 の電池ケース", "box", (0.024, 0.012, 0.048), 0.03, 300, variant="aa", tags=("単4", "小さい"), capacityWh=1.9, voltageV=2.4)
add("bat-aaa4", "battery", "単4 充電池 ×4 の電池ケース", "box", (0.045, 0.012, 0.048), 0.055, 450, variant="aa", tags=("単4",), capacityWh=3.8, voltageV=4.8)
for n, yen in ((1, 600), (2, 1300), (4, 2600), (8, 5200)):
    w = 0.019 * (n if n <= 2 else 2)
    dpt = 0.019 * (1 if n <= 2 else n // 2)
    add(f"bat-18650x{n}", "battery", f"18650 リチウムイオンセル ×{n}", "cylinder" if n == 1 else "box",
        (0.0185, 0.065, 0.0185) if n == 1 else (w, 0.066, dpt), 0.047 * n + (0 if n == 1 else 0.015), yen,
        variant="cell", tags=("リチウム", "大容量"), capacityWh=10.8 * n, voltageV=3.6 * (1 if n == 1 else 2 if n >= 2 else 1))
for n, yen in ((1, 900), (4, 4200)):
    add(f"bat-21700x{n}", "battery", f"21700 リチウムイオンセル ×{n}", "cylinder" if n == 1 else "box",
        (0.021, 0.070, 0.021) if n == 1 else (0.042, 0.071, 0.042), 0.07 * n + (0 if n == 1 else 0.02), yen,
        variant="cell", tags=("リチウム", "大容量"), capacityWh=18 * n, voltageV=3.6 if n == 1 else 7.2)
for mah, (w, h, d, kg, yen) in {300: (0.02, 0.005, 0.03, 0.008, 500), 500: (0.03, 0.005, 0.035, 0.011, 600), 1000: (0.034, 0.008, 0.05, 0.021, 800),
                                2000: (0.04, 0.008, 0.06, 0.038, 1200), 3000: (0.05, 0.009, 0.065, 0.055, 1500), 5000: (0.06, 0.01, 0.09, 0.09, 2200)}.items():
    add(f"bat-lipo{mah}", "battery", f"薄型リチウムポリマー電池 {mah}mAh", "box", (w, h, d), kg, yen, variant="pouch", tags=("薄い", "服", "身につける"),
        capacityWh=3.7 * mah / 1000, voltageV=3.7)
for cells, mah, kg, yen in ((3, 2200, 0.18, 2800), (4, 5000, 0.55, 7000), (6, 10000, 1.3, 18000), (6, 22000, 2.6, 38000)):
    add(f"bat-drone{cells}s{mah}", "battery", f"ドローン用リチウムポリマー {cells}セル {mah}mAh", "box",
        (0.035 + 0.005 * cells, 0.025 + 0.002 * cells, 0.1 + mah / 400000), kg, yen, variant="pack", tags=("飛ぶ", "ドローン", "高出力"),
        capacityWh=3.7 * cells * mah / 1000, voltageV=3.7 * cells)
for mah, (w, h, d, kg, yen) in {5000: (0.065, 0.014, 0.1, 0.12, 2000), 10000: (0.07, 0.016, 0.14, 0.2, 3000), 20000: (0.075, 0.026, 0.15, 0.36, 5000)}.items():
    add(f"bat-bank{mah}", "battery", f"モバイルバッテリー {mah}mAh（USB）", "box", (w, h, d), kg, yen, variant="power_bank", tags=("USB", "充電", "スマホ"),
        capacityWh=3.7 * mah / 1000, voltageV=5)
for ah, (w, h, d, kg, yen) in {6: (0.15, 0.095, 0.065, 0.8, 6000), 12: (0.15, 0.095, 0.1, 1.5, 9000), 20: (0.18, 0.17, 0.08, 2.6, 15000), 50: (0.23, 0.21, 0.14, 6.0, 35000)}.items():
    add(f"bat-lfp12v{ah}", "battery", f"リン酸鉄リチウム電池 12V {ah}Ah", "box", (w, h, d), kg, yen, variant="pack", tags=("ロボット", "長時間", "安全"),
        capacityWh=12.8 * ah, voltageV=12.8)
add("bat-ebike36v10", "battery", "電動アシスト用電池 36V 10Ah", "box", (0.09, 0.09, 0.37), 2.8, 30000, ip=4, variant="pack", tags=("自転車", "移動", "大容量"), capacityWh=360, voltageV=36)
add("bat-ebike48v15", "battery", "電動モビリティ用電池 48V 15Ah", "box", (0.1, 0.1, 0.42), 4.2, 48000, ip=4, variant="pack", tags=("乗り物", "移動", "大容量"), capacityWh=720, voltageV=48)

# ---------------- モーター ----------------
for name, (d, h, kg, w, rpm, yen) in {"130": (0.02, 0.025, 0.018, 1, 9000, 150), "280": (0.024, 0.03, 0.03, 5, 8000, 300),
                                       "380": (0.028, 0.038, 0.05, 15, 15000, 500), "540": (0.036, 0.05, 0.16, 40, 20000, 1000),
                                       "775": (0.042, 0.066, 0.35, 150, 12000, 1800)}.items():
    add(f"mot-dc{name}", "motor", f"小型 DC モーター（{name} サイズ）", "cylinder", (d, h, d), kg, yen, variant="dc", tags=("回す", "安い"),
        powerW=w, rpm=rpm, torqueNm=w / (rpm * 6.283 / 60) * 2, noiseDb=55 + w ** 0.3 * 5)
for name, (d, h, kg, w, rpm, tq, yen) in {"n20": (0.012, 0.03, 0.01, 1, 100, 0.05, 400), "tt": (0.022, 0.065, 0.03, 2, 200, 0.08, 300),
                                           "25mm": (0.025, 0.06, 0.09, 6, 150, 0.3, 1500), "37mm": (0.037, 0.08, 0.25, 15, 100, 1.2, 2500),
                                           "worm": (0.04, 0.09, 0.45, 30, 50, 4.0, 4000)}.items():
    add(f"mot-gear-{name}", "motor", f"ギア付きモーター（{name}）", "cylinder", (d, h, d), kg, yen, variant="gear", tags=("ゆっくり", "力が強い", "車輪"),
        powerW=w, rpm=rpm, torqueNm=tq, noiseDb=50)
for name, (w_, h, d, kg, tq, yen, ip) in {"micro": (0.023, 0.029, 0.012, 0.009, 0.18, 400, -1), "standard": (0.04, 0.042, 0.02, 0.055, 1.0, 1500, -1),
                                           "high": (0.04, 0.042, 0.02, 0.065, 2.0, 2500, -1), "waterproof": (0.04, 0.042, 0.02, 0.07, 2.5, 4000, 6),
                                           "large": (0.065, 0.06, 0.03, 0.17, 6.0, 8000, -1)}.items():
    add(f"mot-servo-{name}", "motor", f"サーボモーター（{ {'micro': '小型', 'standard': '標準', 'high': '高トルク', 'waterproof': '防水', 'large': '大型'}[name] }）",
        "box", (w_, h, d), kg, yen, ip=ip, variant="servo", tags=("角度", "動かす", "関節", "開け閉め"), powerW=5 * tq + 1, torqueNm=tq, rpm=60)
for nema, (s, h, kg, tq, yen) in {8: (0.02, 0.03, 0.06, 0.02, 2500), 14: (0.035, 0.034, 0.13, 0.1, 2000), 17: (0.042, 0.048, 0.35, 0.45, 2000), 23: (0.057, 0.076, 1.0, 1.9, 5000)}.items():
    add(f"mot-step{nema}", "motor", f"ステッピングモーター（{s * 1000:.0f} mm 角）", "box", (s, s, h), kg, yen, variant="stepper", tags=("正確", "位置", "ゆっくり"),
        powerW=3 + tq * 15, torqueNm=tq, rpm=300)
for stroke, (force, kg, yen) in {0.05: (200, 0.5, 4000), 0.1: (500, 0.9, 6000), 0.3: (900, 1.6, 9000), 0.5: (1500, 2.4, 14000)}.items():
    add(f"mot-linear{int(stroke * 1000)}", "motor", f"電動アクチュエーター（伸び {stroke * 100:.0f} cm）", "cylinder", (0.04, 0.12 + stroke, 0.04), kg, yen,
        variant="linear", tags=("押す", "引く", "持ち上げ", "伸び縮み"), powerW=20 + force / 30, forceN=force, strokeM=stroke)
for inch, (w, kg, yen) in {6.5: (250, 2.6, 15000), 8: (350, 3.6, 20000), 10: (500, 5.0, 28000)}.items():
    add(f"mot-hub{str(inch).replace('.', '_')}", "motor", f"車輪一体のハブモーター（{inch} インチ）", "cylinder", (inch * 0.0254, 0.06, inch * 0.0254), kg, yen,
        ip=4, variant="hub", tags=("乗り物", "走る", "車輪"), powerW=w, rpm=600)
for name, (d, h, kg, w, yen) in {"2204": (0.028, 0.02, 0.025, 150, 2000), "2306": (0.03, 0.024, 0.033, 400, 2500),
                                  "5010": (0.057, 0.025, 0.1, 600, 6000), "8318": (0.09, 0.035, 0.27, 2000, 20000)}.items():
    add(f"mot-bl{name}", "motor", f"ブラシレスモーター（{name}）", "cylinder", (d, h, d), kg, yen, variant="brushless", tags=("飛ぶ", "ドローン", "高回転"),
        powerW=w, rpm=10000, noiseDb=70)

# ---------------- 発光部 ----------------
for w, (lm, d, kg, yen) in {0.5: (50, 0.01, 0.003, 100), 1: (100, 0.02, 0.006, 200), 3: (300, 0.025, 0.012, 400), 5: (500, 0.03, 0.02, 600),
                            10: (1000, 0.045, 0.06, 1200)}.items():
    add(f"led-spot{str(w).replace('.', '_')}w", "light", f"LED スポット {w}W", "cylinder", (d, d * 0.8, d), kg, yen, variant="", tags=("照らす", "足元", "明るい"),
        powerW=w, lumens=lm)
add("led-cob20", "light", "COB LED 投光 20W", "box", (0.08, 0.03, 0.06), 0.18, 2500, ip=5, variant="panel", tags=("広く", "明るい", "屋外"), powerW=20, lumens=2000)
add("led-cob30", "light", "COB LED 投光 30W", "box", (0.1, 0.035, 0.08), 0.28, 3500, ip=5, variant="panel", tags=("広く", "明るい", "屋外"), powerW=30, lumens=3000)
for cm, (lm, w, yen) in {10: (30, 0.5, 300), 30: (100, 1.5, 600), 100: (300, 5, 1500)}.items():
    add(f"led-tape{cm}", "light", f"LED テープ {cm} cm", "box", (cm / 100, 0.003, 0.01), 0.005 + cm * 0.0004, yen, ip=4, variant="led_tape",
        tags=("線", "縁", "光る", "服", "見える"), powerW=w, lumens=lm)
for cm, (lm, w, yen) in {10: (200, 2, 1200), 20: (800, 8, 3000)}.items():
    add(f"led-panel{cm}", "light", f"LED パネル {cm} cm 角", "box", (cm / 100, 0.008, cm / 100), 0.05 * cm / 10, yen, variant="panel", tags=("天井", "面", "部屋"),
        powerW=w, lumens=lm)
add("led-marker", "light", "小型マーカーライト（点滅）", "cylinder", (0.012, 0.006, 0.012), 0.002, 150, ip=6, variant="", tags=("目印", "点滅", "見える"), powerW=0.2, lumens=10)
add("led-headlamp", "light", "ヘッドランプのライト部", "cylinder", (0.035, 0.03, 0.035), 0.04, 1500, ip=4, variant="", tags=("頭", "前", "夜"), powerW=3, lumens=250)
add("led-uv", "light", "UV LED（365nm）", "cylinder", (0.015, 0.012, 0.015), 0.004, 600, variant="", tags=("除菌",), powerW=1, lumens=5)

# ---------------- センサー ----------------
S = []
def sensor(id, label, variant, shape, size, kg, yen, ip=-1, tags=(), **specs):
    add(id, "sensor", label, shape, size, kg, yen, ip=ip, variant=variant, tags=tags, **specs)
sensor("sen-cam", "小型カメラ", "camera", "box", (0.025, 0.025, 0.02), 0.01, 2000, tags=("見る", "映像", "見分ける", "顔"), rangeM=10, fovDeg=70, powerW=1)
sensor("sen-cam-wide", "広角カメラ", "camera", "box", (0.03, 0.03, 0.025), 0.02, 3500, tags=("見る", "広く", "見分ける"), rangeM=8, fovDeg=120, powerW=1.2)
sensor("sen-cam-depth", "距離が分かるステレオカメラ", "stereo", "box", (0.09, 0.025, 0.025), 0.07, 30000, tags=("見る", "立体", "距離", "見分ける"), rangeM=6, fovDeg=90, powerW=3.5)
sensor("sen-cam-thermal", "熱を見るカメラ（サーモ）", "camera", "box", (0.03, 0.03, 0.03), 0.02, 15000, tags=("温度", "人", "暗い", "見分ける"), rangeM=15, fovDeg=55, powerW=0.8)
sensor("sen-cam-ai", "物を見分けるカメラ（AI 処理つき）", "camera", "box", (0.04, 0.04, 0.03), 0.04, 12000, tags=("見分ける", "人", "自転車", "動物"), rangeM=12, fovDeg=80, powerW=2.5)
sensor("sen-ultra", "超音波距離センサー", "ultrasonic", "box", (0.045, 0.02, 0.015), 0.009, 300, tags=("距離", "障害物", "ぶつからない"), rangeM=4, fovDeg=30, powerW=0.08)
sensor("sen-ultra-wp", "防水 超音波距離センサー", "ultrasonic", "cylinder", (0.025, 0.02, 0.025), 0.02, 1500, ip=7, tags=("距離", "屋外", "雨", "車"), rangeM=5, fovDeg=70, powerW=0.15)
sensor("sen-tof", "ToF 距離センサー（短い距離）", "tof", "box", (0.013, 0.004, 0.018), 0.002, 800, tags=("距離", "近い", "正確"), rangeM=2, fovDeg=25, powerW=0.02)
sensor("sen-tof-long", "ToF 距離センサー（4 m）", "tof", "box", (0.02, 0.006, 0.025), 0.004, 2000, tags=("距離", "正確"), rangeM=4, fovDeg=27, powerW=0.1)
sensor("sen-lidar2d", "回転式レーザー距離計（LiDAR）", "lidar", "cylinder", (0.07, 0.04, 0.07), 0.19, 15000, tags=("周り", "地図", "ロボット", "障害物"), rangeM=12, fovDeg=360, powerW=2.5)
sensor("sen-pir", "人感センサー（赤外線）", "pir", "sphere", (0.024, 0.024, 0.024), 0.006, 300, tags=("人", "近づく", "自動", "見守り"), rangeM=5, fovDeg=110, powerW=0.01)
sensor("sen-pir-ceiling", "天井用の人感センサー（全方向）", "pir", "sphere", (0.05, 0.05, 0.05), 0.03, 2500, tags=("人", "部屋", "天井", "見守り"), rangeM=6, fovDeg=360, powerW=0.05)
sensor("sen-radar", "電波式の人感センサー（ミリ波）", "tof", "box", (0.03, 0.006, 0.03), 0.005, 2500, tags=("人", "息", "寝る", "見守り"), rangeM=6, fovDeg=120, powerW=0.5)
sensor("sen-climate", "温度・湿度センサー", "climate", "box", (0.02, 0.008, 0.025), 0.004, 600, tags=("温度", "湿度", "暑い", "寒い", "蒸れ"), rangeM=0, powerW=0.01)
sensor("sen-temp-probe", "防水 温度センサー（棒）", "climate", "cylinder", (0.006, 0.05, 0.006), 0.01, 400, ip=7, tags=("温度", "水", "お風呂"), rangeM=0, powerW=0.01)
sensor("sen-rain", "雨滴センサー（板）", "rain", "box", (0.05, 0.002, 0.04), 0.008, 300, tags=("雨", "濡れる", "自動"), rangeM=0, powerW=0.02)
sensor("sen-water-level", "水位センサー", "rain", "box", (0.02, 0.06, 0.003), 0.005, 400, tags=("水", "タンク", "量"), rangeM=0, powerW=0.02)
sensor("sen-load5", "重さセンサー（5 kg まで）", "load_cell", "box", (0.08, 0.013, 0.013), 0.03, 800, tags=("重さ", "量る", "荷物"), rangeM=0, loadKg=5, powerW=0.01)
sensor("sen-load50", "重さセンサー（50 kg まで）", "load_cell", "box", (0.13, 0.022, 0.03), 0.18, 2500, tags=("重さ", "量る", "人"), rangeM=0, loadKg=50, powerW=0.01)
sensor("sen-imu6", "傾き・動きのセンサー（6 軸）", "imu", "box", (0.016, 0.003, 0.02), 0.002, 600, tags=("傾き", "倒れる", "歩く", "転ぶ"), rangeM=0, powerW=0.01)
sensor("sen-imu9", "傾き・向きのセンサー（9 軸）", "imu", "box", (0.02, 0.003, 0.025), 0.003, 2000, tags=("傾き", "向き", "姿勢"), rangeM=0, powerW=0.02)
sensor("sen-gps", "GPS 受信機", "gps", "box", (0.035, 0.009, 0.035), 0.02, 3000, tags=("位置", "地図", "迷子", "場所"), rangeM=0, powerW=0.15)
sensor("sen-mic", "小型マイク", "microphone", "cylinder", (0.01, 0.006, 0.01), 0.002, 300, tags=("音", "声", "聞く"), rangeM=5, fovDeg=360, powerW=0.01)
sensor("sen-mic-array", "向きが分かるマイク（4 個組）", "microphone", "box", (0.065, 0.01, 0.065), 0.02, 5000, tags=("音", "声", "向き"), rangeM=8, fovDeg=360, powerW=0.3)
sensor("sen-lux", "明るさセンサー", "climate", "box", (0.015, 0.004, 0.015), 0.002, 300, tags=("暗い", "明るい", "自動", "夜"), rangeM=0, powerW=0.005)
sensor("sen-co2", "二酸化炭素センサー", "climate", "box", (0.035, 0.012, 0.025), 0.01, 4000, tags=("空気", "換気", "部屋"), rangeM=0, powerW=0.2)
sensor("sen-heart", "心拍センサー（腕・指）", "imu", "box", (0.015, 0.004, 0.015), 0.002, 1500, tags=("体", "健康", "運動"), rangeM=0, powerW=0.02)

# ---------------- 音を出す部品 ----------------
add("spk-buzzer", "speaker", "圧電ブザー", "cylinder", (0.012, 0.009, 0.012), 0.002, 100, variant="buzzer", tags=("鳴る", "知らせる", "小さい"), db=85, powerW=0.05)
add("spk-buzzer-loud", "speaker", "大音量ブザー", "cylinder", (0.03, 0.015, 0.03), 0.015, 600, ip=4, variant="buzzer", tags=("鳴る", "防犯", "大きい音"), db=95, powerW=0.3)
add("spk-siren", "speaker", "防犯サイレン", "cylinder", (0.05, 0.04, 0.05), 0.06, 1500, ip=5, variant="buzzer", tags=("防犯", "大きい音", "助け"), db=105, powerW=2)
for d, (db, w, kg, yen) in {0.028: (85, 1, 0.01, 300), 0.04: (90, 3, 0.03, 500), 0.06: (95, 10, 0.12, 1200)}.items():
    add(f"spk-{int(d * 1000)}mm", "speaker", f"スピーカー（直径 {d * 1000:.0f} mm）", "cylinder", (d, d * 0.45, d), kg, yen, variant="", tags=("声", "音楽", "知らせる"), db=db, powerW=w)
add("spk-horn", "speaker", "防水ホーンスピーカー", "cylinder", (0.1, 0.12, 0.1), 0.6, 4000, ip=6, variant="", tags=("屋外", "大きい音", "遠く"), db=110, powerW=15)

# ---------------- 太陽電池 ----------------
for w, (x, z, kg, yen, flex) in {0.5: (0.06, 0.06, 0.02, 400, False), 1: (0.1, 0.07, 0.03, 600, False), 3: (0.15, 0.13, 0.12, 1500, False),
                                 6: (0.22, 0.18, 0.25, 2500, False), 10: (0.35, 0.25, 0.5, 4000, False), 20: (0.55, 0.32, 0.6, 7000, True), 50: (0.65, 0.54, 3.0, 12000, False)}.items():
    add(f"sol-{str(w).replace('.', '_')}w", "solar", f"{'曲げられる' if flex else ''}太陽電池パネル {w}W", "box", (x, 0.003 if flex else 0.006, z), kg, yen, ip=4,
        variant="", tags=("太陽", "外", "充電"), powerW=w)

# ---------------- 加熱部・冷却部 ----------------
for w, (x, z, yen) in {5: (0.05, 0.08, 800), 10: (0.08, 0.1, 1200), 20: (0.1, 0.15, 1800)}.items():
    add(f"heat-film{w}", "heater", f"フィルムヒーター {w}W", "box", (x, 0.001, z), 0.005 + w * 0.0015, yen, variant="film", tags=("温める", "服", "薄い"), powerW=w)
add("heat-pad-usb", "heater", "USB 温熱パッド", "box", (0.15, 0.006, 0.12), 0.06, 1500, variant="film", tags=("温める", "背中", "手"), powerW=10)
add("heat-ptc30", "heater", "PTC ヒーター 30W", "box", (0.04, 0.025, 0.06), 0.05, 1200, variant="ptc", tags=("温める", "温風"), powerW=30)
add("heat-ptc100", "heater", "PTC ヒーター 100W", "box", (0.06, 0.03, 0.08), 0.12, 2500, variant="ptc", tags=("温める", "温風", "部屋"), powerW=100)
for w, (s, yen) in {12: (0.02, 600), 36: (0.04, 900), 60: (0.04, 1500)}.items():
    add(f"cool-peltier{w}", "cooler", f"ペルチェ素子 {w}W（冷やす）", "box", (s, 0.004, s), 0.02 + w * 0.0003, yen, variant="peltier", tags=("冷やす", "首", "暑い"), powerW=w)
add("cool-neck", "cooler", "首用の冷却プレート（ペルチェと放熱つき）", "box", (0.05, 0.02, 0.04), 0.09, 4000, variant="", tags=("冷やす", "首", "夏"), powerW=8)

# ---------------- 車輪・推進ユニット ----------------
for mm, (w, kg, yen) in {50: (0.02, 0.02, 300), 100: (0.03, 0.08, 600), 150: (0.04, 0.2, 1200), 200: (0.05, 0.45, 2500)}.items():
    add(f"whl-{mm}", "wheel", f"ゴム車輪（直径 {mm / 10:.0f} cm）", "cylinder", (mm / 1000, w, mm / 1000), kg, yen, variant="", tags=("走る", "床"), loadKg=mm / 5)
add("whl-air8", "wheel", "空気入りタイヤ（8 インチ）", "cylinder", (0.2, 0.05, 0.2), 0.9, 3000, variant="", tags=("でこぼこ", "外", "乗り物"), loadKg=100)
add("whl-omni", "wheel", "オムニホイール（横にも転がる）", "cylinder", (0.1, 0.03, 0.1), 0.15, 2500, variant="", tags=("横", "ロボット", "その場で回る"), loadKg=15)
for inch, (thrust, w, kg, yen) in {5: (9, 150, 0.005, 300), 7: (15, 300, 0.01, 500), 10: (30, 500, 0.02, 900), 15: (60, 900, 0.04, 2000)}.items():
    add(f"thr-prop{inch}", "thruster", f"プロペラ推進ユニット（{inch} インチ）", "cylinder", (inch * 0.0254 * 1.1, 0.03, inch * 0.0254 * 1.1), kg + 0.05 + w / 20000, yen + 2500,
        variant="", tags=("飛ぶ", "ドローン"), thrustN=thrust, powerW=w)
for mm, (thrust, w, yen) in {50: (5, 300, 4000), 70: (12, 800, 7000), 90: (25, 1800, 12000)}.items():
    add(f"thr-edf{mm}", "thruster", f"ダクトファン（{mm} mm）", "cylinder", (mm / 1000 + 0.01, mm / 1000, mm / 1000 + 0.01), 0.05 + mm / 1000, yen,
        variant="", tags=("飛ぶ", "風", "高出力"), thrustN=thrust, powerW=w)

# ---------------- 新しい種類：動き ----------------
add("hng-25", "hinge", "小型ちょうつがい（25 mm）", "box", (0.025, 0.003, 0.025), 0.01, 150, variant="", tags=("開け閉め", "ふた", "折る"), loadKg=2)
add("hng-50", "hinge", "ちょうつがい（50 mm）", "box", (0.05, 0.004, 0.04), 0.035, 300, variant="", tags=("開け閉め", "扉", "折る"), loadKg=8)
add("hng-piano", "hinge", "長いちょうつがい（30 cm）", "box", (0.3, 0.003, 0.03), 0.12, 900, variant="", tags=("ふた", "長い"), loadKg=15)
add("hng-torque", "hinge", "止まるちょうつがい（好きな角度で止まる）", "box", (0.04, 0.008, 0.035), 0.05, 1500, variant="", tags=("角度", "ふた", "画面"), loadKg=3, torqueNm=0.5)
add("hng-wp", "hinge", "ステンレス防水ちょうつがい", "box", (0.05, 0.004, 0.04), 0.05, 700, ip=7, variant="", tags=("屋外", "水", "錆びない"), loadKg=10)
for mm, (load, kg, yen) in {150: (15, 0.15, 800), 300: (25, 0.3, 1200), 500: (35, 0.55, 2000)}.items():
    add(f"rail-{mm}", "slide_rail", f"スライドレール（{mm / 10:.0f} cm）", "box", (0.012, 0.035, mm / 1000), kg, yen, variant="", tags=("引き出し", "伸ばす", "滑らせる"), loadKg=load, strokeM=mm / 1000 * 0.9)
add("rail-linear", "slide_rail", "直動ガイド（ミニ 10 cm）", "box", (0.012, 0.01, 0.1), 0.06, 2500, variant="", tags=("正確", "まっすぐ", "動かす"), loadKg=5, strokeM=0.07)
add("pole-1m", "telescopic", "伸縮ポール（30〜100 cm）", "cylinder", (0.025, 0.3, 0.025), 0.15, 1200, variant="", tags=("伸ばす", "高く", "棒"), strokeM=0.7, loadKg=3)
add("pole-2m", "telescopic", "伸縮ポール（50〜200 cm）", "cylinder", (0.03, 0.5, 0.03), 0.35, 2500, variant="", tags=("伸ばす", "高く", "棒"), strokeM=1.5, loadKg=3)
add("pole-carbon", "telescopic", "カーボン伸縮ポール（軽い）", "cylinder", (0.025, 0.4, 0.025), 0.12, 5000, variant="", tags=("軽い", "伸ばす", "傘"), strokeM=1.0, loadKg=2)
add("spr-small", "spring", "小さな押しばね", "cylinder", (0.01, 0.03, 0.01), 0.002, 50, variant="", tags=("戻る", "押す", "ボタン"), forceN=10)
add("spr-large", "spring", "大きな押しばね", "cylinder", (0.04, 0.1, 0.04), 0.05, 300, variant="", tags=("戻る", "衝撃", "やわらげる"), forceN=200)
add("spr-gas100", "spring", "ガススプリング（100 N）", "cylinder", (0.018, 0.25, 0.018), 0.15, 1000, variant="gas", tags=("ゆっくり開く", "ふた", "支える"), forceN=100, strokeM=0.08)
add("spr-gas300", "spring", "ガススプリング（300 N）", "cylinder", (0.022, 0.35, 0.022), 0.3, 1800, variant="gas", tags=("ゆっくり開く", "ふた", "支える", "重い"), forceN=300, strokeM=0.12)
add("spr-torsion", "spring", "ねじりばね", "cylinder", (0.015, 0.01, 0.015), 0.003, 100, variant="", tags=("戻る", "ちょうつがい", "クリップ"), torqueNm=0.1)
add("gear-spur", "gear", "歯車の組（平歯車）", "cylinder", (0.04, 0.008, 0.04), 0.02, 500, variant="", tags=("回す", "速さを変える"), torqueNm=2)
add("gear-belt", "gear", "タイミングベルトとプーリー", "box", (0.04, 0.012, 0.2), 0.06, 1500, variant="belt", tags=("回す", "離れた所", "ベルト"), torqueNm=1.5)
add("gear-worm", "gear", "ウォームギア（逆に回らない）", "box", (0.04, 0.03, 0.04), 0.08, 1200, variant="", tags=("止める", "持ち上げ", "回す"), torqueNm=5)
add("gear-pulley", "gear", "滑車", "cylinder", (0.05, 0.015, 0.05), 0.04, 400, variant="", tags=("持ち上げ", "ひも", "ロープ"), loadKg=20)
add("lock-magnet", "latch", "マグネットキャッチ", "box", (0.03, 0.012, 0.012), 0.015, 200, variant="", tags=("閉める", "ふた", "扉"), forceN=30)
add("lock-solenoid", "latch", "電気で開く錠（ソレノイド）", "box", (0.05, 0.02, 0.03), 0.12, 1500, variant="", tags=("鍵", "自動", "開ける"), forceN=300, powerW=6)
add("lock-push", "latch", "押して開くプッシュラッチ", "box", (0.03, 0.015, 0.02), 0.01, 300, variant="", tags=("閉める", "引き出し"), forceN=20)
add("lock-hasp", "latch", "掛け金", "box", (0.06, 0.008, 0.03), 0.05, 400, variant="", tags=("鍵", "閉める", "箱"), forceN=500)
for cm, (load, kg, yen) in {30: (20, 0.8, 8000), 60: (60, 2.5, 18000), 100: (150, 6.0, 40000)}.items():
    add(f"crawl-{cm}", "crawler", f"クローラー（長さ {cm} cm）", "box", (0.05 + cm / 2000, cm / 600 + 0.04, cm / 100), kg, yen, variant="", tags=("階段", "段差", "でこぼこ", "雪"), loadKg=load)
add("cast-ball25", "caster", "ボールキャスター（25 mm）", "sphere", (0.025, 0.025, 0.025), 0.03, 300, variant="ball", tags=("転がる", "支える", "ロボット"), loadKg=10)
add("cast-40", "caster", "自在キャスター（40 mm）", "cylinder", (0.04, 0.055, 0.04), 0.05, 300, variant="", tags=("転がる", "台車", "向きが変わる"), loadKg=20)
add("cast-75b", "caster", "ストッパー付き自在キャスター（75 mm）", "cylinder", (0.075, 0.1, 0.075), 0.2, 800, variant="", tags=("転がる", "止める", "台車"), loadKg=60)
add("cast-100", "caster", "自在キャスター（100 mm）", "cylinder", (0.1, 0.13, 0.1), 0.35, 1200, variant="", tags=("台車", "重い荷物"), loadKg=100)
add("leg-adj", "leg", "高さを変えられる脚", "cylinder", (0.03, 0.08, 0.03), 0.05, 300, variant="", tags=("立てる", "床", "水平"), loadKg=50)
add("leg-fold", "leg", "折りたたみ脚", "cylinder", (0.025, 0.6, 0.025), 0.4, 1500, variant="", tags=("立てる", "たたむ", "台"), loadKg=30)
add("leg-robot", "leg", "歩くロボットの脚（関節つき）", "cylinder", (0.05, 0.3, 0.05), 0.5, 12000, variant="", tags=("歩く", "階段", "ロボット"), loadKg=5, powerW=20)

# ---------------- 新しい種類：流体 ----------------
add("pump-mini", "pump", "小型水ポンプ（3V）", "cylinder", (0.025, 0.045, 0.025), 0.03, 400, ip=7, variant="", tags=("水", "吸う", "流す"), flowLpm=1.5, powerW=1)
add("pump-dia", "pump", "ダイヤフラムポンプ（12V）", "box", (0.04, 0.035, 0.09), 0.2, 2500, variant="", tags=("水", "霧", "高圧"), flowLpm=3, powerW=24)
add("pump-peri", "pump", "チューブポンプ（少しずつ送る）", "box", (0.04, 0.04, 0.05), 0.1, 2000, variant="", tags=("正確", "薬", "えさ"), flowLpm=0.1, powerW=4)
add("pump-air", "pump", "エアーポンプ（空気を送る）", "box", (0.03, 0.025, 0.05), 0.05, 800, variant="", tags=("空気", "膨らむ", "クッション"), flowLpm=2, powerW=2)
add("pump-sub", "pump", "水中ポンプ（12V）", "cylinder", (0.05, 0.09, 0.05), 0.25, 2500, ip=8, variant="", tags=("水", "くみ上げ"), flowLpm=8, powerW=20)
for l, (d, h, kg, yen) in {0.1: (0.045, 0.08, 0.02, 300), 0.5: (0.075, 0.14, 0.08, 600), 2: (0.12, 0.22, 0.25, 1200), 5: (0.17, 0.27, 0.5, 2000), 10: (0.22, 0.32, 0.9, 3000)}.items():
    add(f"tank-{str(l).replace('.', '_')}l", "tank", f"タンク（{l} L）", "cylinder", (d, h, d), kg, yen, variant="", tags=("水", "ためる", "入れる"), volumeL=l)
add("noz-mist", "nozzle", "霧のノズル", "cone", (0.012, 0.015, 0.012), 0.005, 300, variant="", tags=("霧", "冷やす", "まく"), flowLpm=0.1)
add("noz-spray", "nozzle", "スプレーノズル", "cone", (0.02, 0.025, 0.02), 0.01, 400, variant="", tags=("まく", "水", "洗う"), flowLpm=1)
add("noz-jet", "nozzle", "噴射ノズル", "cone", (0.03, 0.05, 0.03), 0.03, 800, variant="", tags=("噴く", "勢い", "空気"), flowLpm=5)
add("valve-sol", "valve", "電磁弁（水・空気）", "box", (0.04, 0.05, 0.03), 0.12, 1500, variant="", tags=("止める", "流す", "自動"), powerW=5)
add("valve-check", "valve", "逆止弁（逆に流れない）", "cylinder", (0.015, 0.03, 0.015), 0.01, 300, variant="", tags=("止める", "空気", "水"))
add("valve-ball", "valve", "ボールバルブ（手で開け閉め）", "box", (0.04, 0.04, 0.05), 0.15, 1200, variant="", tags=("止める", "手で"))
for mm, (yen,) in {4: (300,), 8: (500,), 12: (800,)}.items():
    add(f"hose-{mm}", "hose", f"シリコンチューブ（太さ {mm} mm、1 m）", "cylinder", (mm / 1000, 1.0, mm / 1000), 0.004 * mm, yen, variant="", tags=("流す", "つなぐ", "水", "空気"))
for mm, (air, w, db, yen, ip) in {30: (5, 0.5, 25, 300, -1), 40: (9, 0.8, 26, 400, -1), 60: (30, 1.5, 28, 600, -1), 80: (55, 2, 30, 800, -1),
                                   120: (110, 3, 32, 1200, -1)}.items():
    add(f"fan-{mm}", "fan", f"ファン（{mm} mm）", "box", (mm / 1000, mm / 4000 + 0.01, mm / 1000), 0.01 + mm * 0.0012, yen, ip=ip, variant="", tags=("風", "冷やす", "換気"),
        airflowM3h=air, powerW=w, noiseDb=db)
add("fan-blower", "fan", "ブロワー（50 mm、強い風）", "box", (0.05, 0.015, 0.05), 0.03, 800, variant="blower", tags=("風", "送る", "強い"), airflowM3h=10, powerW=2, noiseDb=40)
add("fan-wp", "fan", "防水ファン（80 mm）", "box", (0.08, 0.025, 0.08), 0.1, 2500, ip=5, variant="", tags=("風", "屋外", "雨"), airflowM3h=50, powerW=2.5, noiseDb=32)
add("fan-neck", "fan", "首かけ扇風機用の小型ファン", "cylinder", (0.04, 0.02, 0.04), 0.02, 600, variant="", tags=("風", "首", "夏"), airflowM3h=6, powerW=1.5, noiseDb=35)

# ---------------- 新しい種類：知らせる ----------------
for inch, (x, z, kg, w, yen, var) in {0.96: (0.027, 0.027, 0.004, 0.08, 600, ""), 1.3: (0.035, 0.035, 0.006, 0.1, 900, ""),
                                       2.4: (0.06, 0.045, 0.02, 0.4, 1500, ""), 3.5: (0.085, 0.06, 0.04, 0.8, 2500, ""),
                                       7: (0.17, 0.11, 0.25, 3, 7000, "")}.items():
    add(f"disp-{str(inch).replace('.', '_')}", "display", f"画面（{inch} インチ）", "box", (x, 0.006, z), kg, yen, variant=var, tags=("表示", "数字", "見せる"), inch=inch, powerW=w)
add("disp-epaper", "display", "電子ペーパー（2.9 インチ・省電力）", "box", (0.08, 0.004, 0.04), 0.015, 2500, variant="", tags=("表示", "長持ち", "札"), inch=2.9, powerW=0.01)
add("disp-matrix", "display", "LED ドット表示（8×8）", "box", (0.032, 0.008, 0.032), 0.01, 500, variant="matrix", tags=("表示", "顔", "光る", "かわいい"), inch=1.3, powerW=0.5, lumens=20)
add("vib-coin", "vibrator", "コイン形振動モーター", "cylinder", (0.01, 0.003, 0.01), 0.001, 100, variant="", tags=("震える", "知らせる", "腕"), powerW=0.2)
add("vib-erm", "vibrator", "振動モーター（10 mm）", "cylinder", (0.01, 0.02, 0.01), 0.004, 150, variant="", tags=("震える", "知らせる", "手"), powerW=0.3)
add("vib-lra", "vibrator", "リニア振動子（くっきりした振動）", "box", (0.01, 0.004, 0.01), 0.002, 400, variant="", tags=("震える", "合図", "腕"), powerW=0.2)
add("ref-tape", "reflector", "反射テープ（2 cm × 30 cm）", "box", (0.3, 0.001, 0.02), 0.005, 300, ip=7, variant="", tags=("夜", "見える", "車", "反射"))
add("ref-disk", "reflector", "丸い反射板（直径 7 cm）", "cylinder", (0.07, 0.005, 0.07), 0.015, 200, ip=7, variant="", tags=("夜", "見える", "自転車"))
add("ref-panel", "reflector", "反射パネル（A5）", "box", (0.21, 0.002, 0.15), 0.04, 600, ip=7, variant="", tags=("夜", "見える", "背中", "ベスト"))

# ---------------- 新しい種類：電気 ----------------
add("sw-tact", "switch", "押しボタン（小）", "box", (0.012, 0.008, 0.012), 0.002, 30, variant="", tags=("押す", "操作"))
add("sw-big", "switch", "大きな押しボタン", "cylinder", (0.03, 0.02, 0.03), 0.02, 300, variant="button", tags=("押す", "すぐ", "非常"))
add("sw-toggle", "switch", "トグルスイッチ", "box", (0.015, 0.03, 0.015), 0.01, 200, variant="", tags=("入れる", "切る", "電源"))
add("sw-wp", "switch", "防水押しボタン", "cylinder", (0.022, 0.03, 0.022), 0.015, 600, ip=7, variant="button", tags=("押す", "屋外", "雨"))
add("sw-dial", "switch", "回すつまみ（ダイヤル）", "cylinder", (0.025, 0.02, 0.025), 0.015, 300, variant="dial", tags=("強さ", "調整", "回す"))
add("sw-slide", "switch", "スライドスイッチ", "box", (0.02, 0.008, 0.01), 0.003, 50, variant="", tags=("入れる", "切る"))
add("rf-ble", "radio", "Bluetooth モジュール", "box", (0.018, 0.003, 0.025), 0.002, 800, variant="", tags=("スマホ", "つなぐ", "近く"), rangeM=10, powerW=0.05)
add("rf-wifi", "radio", "Wi-Fi モジュール", "box", (0.025, 0.004, 0.05), 0.005, 1200, variant="", tags=("インターネット", "家", "つなぐ"), rangeM=30, powerW=0.5)
add("rf-lora", "radio", "LoRa 無線（遠くまで届く）", "box", (0.03, 0.006, 0.04), 0.008, 2500, variant="", tags=("遠く", "山", "見守り"), rangeM=2000, powerW=0.4)
add("rf-lte", "radio", "携帯回線モジュール（LTE）", "box", (0.03, 0.005, 0.04), 0.01, 6000, variant="", tags=("どこでも", "位置", "知らせる"), rangeM=10000, powerW=1.5)
add("rf-ir", "radio", "赤外線リモコン受信部", "box", (0.01, 0.007, 0.006), 0.001, 100, variant="", tags=("リモコン", "家電"), rangeM=8, powerW=0.005)
add("chg-qi5", "charger", "ワイヤレス充電コイル（5W）", "cylinder", (0.04, 0.003, 0.04), 0.01, 800, variant="", tags=("充電", "置くだけ"), powerW=5)
add("chg-qi15", "charger", "ワイヤレス充電コイル（15W）", "cylinder", (0.05, 0.004, 0.05), 0.02, 1500, variant="", tags=("充電", "置くだけ", "速い"), powerW=15)
add("chg-mag", "charger", "マグネット充電端子", "box", (0.015, 0.006, 0.008), 0.003, 500, variant="", tags=("充電", "防水", "くっつく"), powerW=10)
for f, (d, h, kg, yen) in {10: (0.01, 0.03, 0.006, 300), 100: (0.022, 0.045, 0.02, 800), 500: (0.035, 0.06, 0.1, 2000)}.items():
    add(f"cap-{f}f", "capacitor", f"電気二重層キャパシタ（{f}F）", "cylinder", (d, h, d), kg, yen, variant="", tags=("すぐ充電", "瞬間", "長持ち"),
        capacityWh=0.5 * f * 2.7 * 2.7 / 3600, voltageV=2.7)

# ---------------- 新しい種類：熱 ----------------
for mm, (kg, yen) in {20: (0.01, 100), 40: (0.03, 300), 100: (0.2, 1200)}.items():
    add(f"hs-{mm}", "heat_sink", f"放熱板（{mm} mm 角）", "box", (mm / 1000, max(0.008, mm / 5000), mm / 1000), kg, yen, variant="", tags=("冷やす", "熱を逃がす"))
add("hs-pipe", "heat_sink", "ヒートパイプつき放熱器", "box", (0.05, 0.06, 0.03), 0.15, 2500, variant="", tags=("冷やす", "強力"))
add("ins-foam5", "insulation", "断熱シート（発泡、5 mm）", "box", (0.3, 0.005, 0.3), 0.01, 300, variant="", tags=("保温", "保冷", "冷めない"))
add("ins-foam10", "insulation", "断熱ボード（発泡、10 mm）", "box", (0.3, 0.01, 0.3), 0.03, 500, variant="", tags=("保温", "保冷", "箱"))
add("ins-aerogel", "insulation", "エアロゲル断熱シート（3 mm）", "box", (0.3, 0.003, 0.3), 0.015, 3000, variant="", tags=("保温", "薄い", "服"))
add("ins-vacuum", "insulation", "真空断熱パネル（10 mm）", "box", (0.3, 0.01, 0.3), 0.18, 4000, variant="", tags=("保冷", "強力", "箱"))

# ---------------- 新しい種類：取り付け ----------------
add("clip-binder", "clip", "ダブルクリップ", "box", (0.032, 0.02, 0.012), 0.01, 50, variant="", tags=("はさむ", "留める"), forceN=15)
add("clip-belt", "clip", "ベルトクリップ", "box", (0.03, 0.06, 0.012), 0.015, 300, variant="", tags=("腰", "ベルト", "付ける"), forceN=20)
add("clip-cable", "clip", "コードクリップ", "box", (0.015, 0.012, 0.015), 0.002, 30, variant="", tags=("まとめる", "留める"), forceN=5)
for mm, (force, kg, yen) in {30: (20, 0.005, 100), 60: (80, 0.02, 300), 120: (400, 0.15, 1500)}.items():
    add(f"suc-{mm}", "suction_cup", f"吸盤（直径 {mm / 10:.0f} cm{'、レバー式' if mm >= 120 else ''}）", "cylinder", (mm / 1000, mm / 4000 + 0.005, mm / 1000), kg, yen,
        variant="", tags=("窓", "壁", "くっつく", "車"), forceN=force)
for mm, (force, kg, yen) in {10: (20, 0.004, 100), 20: (80, 0.02, 300)}.items():
    add(f"mag-{mm}", "magnet", f"ネオジム磁石（直径 {mm} mm）", "cylinder", (mm / 1000, 0.004, mm / 1000), kg, yen, variant="", tags=("くっつく", "鉄", "外せる"), forceN=force)
add("mag-pot40", "magnet", "フック付き強力磁石（直径 40 mm）", "cylinder", (0.04, 0.01, 0.04), 0.08, 800, variant="", tags=("くっつく", "鉄", "吊るす"), forceN=250)
add("hook-s", "hook", "S 字フック", "box", (0.04, 0.08, 0.006), 0.02, 100, variant="", tags=("掛ける", "吊るす"), loadKg=5)
add("hook-wall", "hook", "壁掛けフック", "box", (0.02, 0.04, 0.03), 0.02, 200, variant="", tags=("掛ける", "壁"), loadKg=3)
add("hook-carabiner", "hook", "カラビナ", "box", (0.04, 0.08, 0.008), 0.03, 500, variant="", tags=("つなぐ", "外せる", "リュック"), loadKg=20)
add("vel-small", "velcro", "面ファスナー（2 cm × 10 cm）", "box", (0.1, 0.004, 0.02), 0.003, 100, variant="", tags=("付け外し", "服", "腕"), forceN=15)
add("vel-large", "velcro", "面ファスナー（5 cm × 50 cm）", "box", (0.5, 0.004, 0.05), 0.03, 500, variant="", tags=("付け外し", "体", "固定"), forceN=60)
add("hdl-pull", "handle", "小さな取っ手", "box", (0.08, 0.025, 0.02), 0.03, 200, variant="", tags=("持つ", "引く", "ふた"))
add("hdl-carry", "handle", "持ち手（グリップ）", "box", (0.12, 0.04, 0.03), 0.08, 500, variant="", tags=("持つ", "運ぶ", "握る"))
add("hdl-trolley", "handle", "伸びる引き手（キャリー用）", "box", (0.25, 0.5, 0.03), 0.8, 3000, variant="", tags=("引く", "運ぶ", "キャリー"), strokeM=0.4)

# ---------------- 新しい種類：守る ----------------
add("gsk-o20", "gasket", "O リング（直径 20 mm）", "ring", (0.02, 0.002, 0.02), 0.001, 30, ip=7, variant="", tags=("防水", "ふた", "すき間"))
add("gsk-o50", "gasket", "O リング（直径 50 mm）", "ring", (0.05, 0.003, 0.05), 0.003, 80, ip=7, variant="", tags=("防水", "ふた", "すき間"))
add("gsk-strip", "gasket", "すき間テープ（ゴム、1 m）", "box", (1.0, 0.005, 0.01), 0.02, 300, ip=5, variant="", tags=("防水", "すき間", "におい", "閉じ込める"))
add("mesh-fine", "mesh_cover", "細かいメッシュ（防虫・防じん）", "box", (0.2, 0.002, 0.2), 0.02, 300, variant="", tags=("通気", "虫", "ほこり"))
add("mesh-grille", "mesh_cover", "金属グリル（ファン・スピーカー用）", "box", (0.08, 0.003, 0.08), 0.02, 300, variant="", tags=("守る", "指", "ファン"))
add("mesh-net", "mesh_cover", "通気ネット（布）", "box", (0.3, 0.002, 0.3), 0.01, 200, variant="", tags=("通気", "蒸れない", "服"))
add("win-acrylic", "window", "アクリル窓（2 mm）", "box", (0.1, 0.002, 0.1), 0.024, 300, variant="", tags=("見える", "のぞく", "ふた"))
add("win-pc", "window", "ポリカーボネート窓（割れにくい、3 mm）", "box", (0.2, 0.003, 0.15), 0.11, 800, variant="", tags=("見える", "割れない", "盾"))
add("win-visor", "window", "くもり止めバイザー", "dome", (0.22, 0.06, 0.22), 0.06, 1500, variant="", tags=("顔", "見える", "雨", "守る"))


def check():
    ids = [m["id"] for m in M]
    dup = {i for i in ids if ids.count(i) > 1}
    assert not dup, dup
    for m in M:
        assert all(v > 0 for v in m["size"]), m["id"]
        assert m["massKg"] > 0, m["id"]


check()
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump({"version": 1, "models": M}, f, ensure_ascii=False, indent=1)
    f.write("\n")
types = {}
for m in M:
    types[m["type"]] = types.get(m["type"], 0) + 1
print(len(M), "models", types)
print("variants", sorted({(m["type"], m["shape"], m["variant"]) for m in M if m["variant"]}))
