# ArtSource — 研究室のモデルの制作元

Blender 5.2（MCP for Blender 2.0）で、スクリプトから作る。`blender/CentralLab.blend` は作業ファイル（リポジトリには入れない）。
各スクリプトを Blender で `exec(open(...).read())` すれば同じ形を作り直せる。書き出しは `blender/export_unity.py` の `export_all()`。

| スクリプト | 中身 | 書き出し先（Assets/_ImpossibleLab/Art/Models） |
|---|---|---|
| `lab_common.py` | 共通（マテリアル・円環/円柱/箱・面取り） | — |
| `central_device.py` | 発明台・ホログラム円筒・天井ユニット・床リング | CentralDevice.fbx |
| `robot_arm.py` | ロボットアーム（台座／肩／肘で分割） | RobotArm.fbx |
| `lab_room.py` | 床・壁・窓・天井・サインの背板 | LabRoom.fbx |
| `lab_props.py` | 作業台・展示ケース・実験ゲート（扉は別オブジェクト） | Workbench.fbx・GalleryCase.fbx・Gate.fbx |
| `plant.py` | 観葉植物（葉は Poly Haven、鉢は自作） | Plant.fbx |

座標：スクリプトは Unity の値で書き、Blender 座標へ直す（Unity (X,Y,Z) = (−bx, bz, −by)）。
マテリアル名 `MAT_*` は Unity の `ProjectSetup.ImportModel` で LabAssets のマテリアルに置き換える。

## 外部素材（すべて CC0、Poly Haven https://polyhaven.com）

| 素材 | 作者 | 使い方 |
|---|---|---|
| concrete_floor_worn_001（テクスチャ 1K） | Dimitrios Savva, Rico Cilliers | 床。色は明るさ・コントラストを調整した版（`_diff_1k_light.jpg`） |
| kloofendal_43d_clear（HDRI のトーンマップ済み JPG） | Greg Zaal | 窓の外の空。2048×1024 に縮小してスカイボックスに |
| potted_plant_02（モデル 1K） | Rico Cilliers | 葉だけ使用（色＋切り抜きを 1 枚に合成）。土・鉢は使わない |
| white_plaster_02（テクスチャ 1K） | Rob Tuytel | 壁の凹凸とつやむら。色は白に近づけた版（`textures/make_lab_textures.py` で加工） |

## 自作の質感の画像

`textures/make_lab_textures.py`（`python -I` で実行）が `Assets/_ImpossibleLab/Art/Textures/Lab/` に書き出す。金属のヘアライン・樹脂のつやむらは乱数の種を固定した自作。研究室のモデルの UV は `lab_common.box_uv`（面の向きごとの投影、1m = 1）。
