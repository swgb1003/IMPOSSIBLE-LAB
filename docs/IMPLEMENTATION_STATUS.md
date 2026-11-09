# 実装状況

最終更新：2026-11-04（Claude Code）

## 0. 環境調査結果（2026-11-04）

| 項目 | 結果 |
|---|---|
| リポジトリ | 着手時は `D:\IMPOSSIBLE_LAB` にドキュメント（CLAUDE.md / docs / DesignReferences）のみ。Unityプロジェクト本体・Git管理なし |
| Unity Editor | 6000.0.61f1（Android Build Support あり）、6000.2.10f1（Androidなし）→ **6000.0.61f1 を採用** |
| Unity CLI | 1.0.0-beta.14、Unity Personal ライセンス有効 |
| 同一PCの実績 | 既存VRプロジェクト（6000.0.61f1）で XRI 3.0.8 / OpenXR 1.15.1 / XR Management 4.5.3 / Input System 1.14.2 の Quest APK ビルド成功実績あり |
| フォント | Noto Sans JP（OFL）がユーザーフォントにインストール済み → プロジェクトへコピー（`.gitignore` で除外） |
| Quest実機 | 本セッションから接続・操作不可。**実機動作は未検証** |

判断：既存Unityプロジェクトが無いため `D:\IMPOSSIBLE_LAB` をUnityプロジェクトルートとして新規作成（詳細は DECISIONS.md D-01/D-02）。

## 1. 構成

```text
Assets/_ImpossibleLab/
  Scripts/Core/      InventionDna, Catalog（部品・機能の許可リスト）, DnaJson（スキーマ検証）, Revision（改良操作）,
                     RainExperiment（雨シミュレーション・DREAM/REALITY規則・比較）, InventionRepository（保存）,
                     AppStateMachine（仕様23.1の状態遷移）, DesignService（オフライン設計・改良ルール）
  Scripts/Runtime/   LabApp（統括）, LabUI（S01〜S06 World Space UI）, UiKit, LabTheme（デザイントークン）,
                     InventionBuilder＋機能モジュール（follow_head / rain_shield / stabilize）, RainLabController,
                     ScreenFader, EditorOnlyObject, DemoAutopilot（Editor検証用）
  Scripts/Editor/    ProjectSetup（URP/XR/Player/素材/フォント生成）, SceneBuilder（LabHubシーン生成）,
                     VerifyRunner（Play Mode 自動検証）, QuestBuild（APKビルド）
  Tests/EditMode/    CoreTests（28件）
  Data/Samples/Resources/ImpossibleLabSamples/handsfree_rain.json  … オフライン用サンプル設計図
  Scenes/LabHub.unity（生成物）  Settings/（URP・LabAssets）  Art/（Materials・Fonts）
```

## 2. タスク記録

### T1 プロジェクト作成・パッケージ・XR設定 — 完了
- 変更：Unityプロジェクト新規作成、`Packages/manifest.json`、XRI Starter Assets / XR Device Simulator サンプル導入、OpenXR（Meta Quest機能・Touch/Touch Plusプロファイル・Single Pass Instanced）、URP（MSAA4x・HDRオフ）、Android（IL2CPP・ARM64・Vulkan・API29〜35）、Input System のみ。
- テスト：バッチモードでコンパイルエラー0、`ProjectSetup.RunAll` 成功（`ILAB_SETUP_DONE`）。

### T2 発明DNA・検証・カタログ・保存 — 完了
- スキーマ検証：schemaVersion、必須項目、文字数、id形式（英小文字等のみ＝安全なファイル名）、カテゴリ/部品/機能の許可リスト、未知フィールド拒否、未知パラメータ拒否、NaN/型違い拒否、部品数上限12、範囲外はクランプ＋警告、id重複・参照先欠落の拒否。
- 保存：1発明1ファイル（現在版・改良履歴最大30・実験記録最大50）、一時ファイル経由で書き込み、破損ファイルはスキップ。
- テスト：EditMode（不正JSON4種、未知フィールド、未知タイプ、過大値クランプ、NaN/重複/参照欠落/パス文字、部品13個、版違い、保存→別インスタンスで復元、破損ファイル）すべて合格。

### T3 雨実験・DREAM/REALITY — 完了
- 解析的レイ判定（DECISIONS D-05/D-06）、XorShift乱数でシード固定、フレーム分割実行と一括実行が同結果。
- REALITY：半径・遮水効率の上限、風による流され、消費電力・推定稼働時間。画面に「簡易モデルによる参考値」を常時表示。
- テスト：決定性、分割＝一括、半径拡大で被濡れ減少、雨量・風速で値が変化、側面カバーで横雨減少、REALITYで制限・電力・流されが発生 — 合格。

### T6 REALITY 物理モデル（Q-02 / D-14） — 完了（Editor）
- 運動量理論のホバー電力、抗力式の風荷重、電池重量×エネルギー密度。係数17件を `reality_model.json` に分離（値・幅・確度・出典）。
- 実験設定（S04）に中央値と幅、「前提係数と出典」画面（3ページ）。結果（S05）に稼働時間の幅と「位置を保てない」表示。保存データに幅などを追加（旧データも読める）。
- 側面パネルがメイン画面の裏に隠れていたため、位置を外側（x ±1.18m・向き±36°）へ移動。
- 詳細・計算例・要調査項目は `docs/reality_model.md`。

### T4 中央研究室・XRリグ・World Space UI（S01〜S06） — 実装済み（Editorで画面確認、実機未確認）
- グレーボックス：中央研究室（床16m、発明台直径3m、天井円環照明、ロボットアーム2基、作業台、ガラス窓、ホログラム表示）、INVENTION GALLERY 展示台、EXPERIMENT GATE（可動扉）、RainLab（判定用マネキン・雨粒子）。
- XR：XRI Starter Assets の XR Origin、テレポート＋スナップターン（連続移動オフ）、TeleportationArea（Layer 31）、XRUIInputModule、TrackedDeviceGraphicRaycaster、暗転フェード、XR Device Simulator（Editorのみ）。
- パネルのはみ出し対策（2026-11-08）：Quest実機でAI設計（パラメータ9個）の編集画面が下にはみ出した。性能パラメータは5個を超えると4個ずつのページ送り（◀ ▶）、側面パネルの部品・機能一覧と改良差分は上限を超えた分を「ほか N 件」にまとめる、REALITY 実験設定の注意書きを見出し行へ移動。Editor 自動検証に「全要素（文字の描画範囲を含む）がパネル内か」のチェックを全画面で追加し、記録したAI応答（部品5・パラメータ9）での画面も検証：**67 PASS / 0 FAIL**。**Quest 3S 実機で全画面が収まることを確認（2026-11-08）**。
- パネル高さ（2026-11-08）：画面中心＝その時の目の高さ−0.5m＋好み（上+0.35m／下−0.7mまで、頭上には出ない）。起動直後・ヘッドセット再装着後・座る/立つで目の高さが大きく変わったときに自動で合わせ直す。高さ調整は右手A（下げる）/B（上げる）とフッターの▼▲。[ILAB]ログを端末の persistentDataPath/ilab_log.txt にも保存。Editor検証 39 PASS / 0 FAIL（疑似頭部で高さ判定を含む）。**Quest実機での確認は未実施**。
- 実機不具合の原因（2026-11-08）：XR Device Simulator が実機ビルドに含まれ、起動直後に本物のHMDを入力から外していた（実機ログで頭の高さが常に0、ポインタ非表示）。`StripEditorOnlyObjects`（IProcessSceneWithReport）でビルド時にシーンから除去。ビルドログで除去を確認済み。**Quest 3S 実機でポインタ表示・パネル正面配置を利用者が確認（2026-11-08）**。
- 操作：レイ＋トリガーでUI操作、グリップで発明品を掴む、掴んでトリガーで起動/停止、起動すると頭上に追従（試用）、左メニューボタン/Mキーでパネルを正面へ、座位/立位切替。
- UI：デザイントークンは `LabTheme` に集約、TMP＋Noto Sans JP、全ボタンに通常/ホバー/押下/無効状態、状態は色だけでなく文字・記号でも表示（✓完了・●処理中・▲改善・▼悪化 等）。

### T5 縦切りフロー（指示→生成→承認→組み立て→改良→実験→比較→保存→ギャラリー→再読込） — 完了（Editor）
- 状態遷移は `AppStateMachine` で一元管理、処理中の二重送信防止、設計のキャンセル、タイムアウト、未対応発明は理由と代替案を表示しサンプル設計へ誘導。
- 改良：オフライン改良ルール（「範囲を広げて」「横からの雨も防いで」「風に流されないように」「低く」等 → update_parameter）、差分プレビュー→承認/却下、元に戻す。未対応の指示は黙って無視せず対応可能な改良を提示。
- 実験結果：実測値のみ、同一条件の改良前との比較、DREAM/REALITY比較、前提・測定方法・限界、計測値に基づく改善案、Reality Bridge（推定・要調査を明記）。
- ギャラリー：カード（名称・カテゴリ・更新日時・版・実験済み）、読込・複製・削除（確認ダイアログ）、共有非対応を明記。

### T7 AI設計プロキシ（Claude）と自動オフライン切替 — 実装済み（Editor・単体テスト。実APIでの通しは未実施）
- `proxy/`：Node 24 + `@anthropic-ai/sdk`。`claude-opus-5-5`、構造化出力（zod スキーマ）、`fallbacks: "default"`、SDK再試行2回・60秒タイムアウト、127.0.0.1 待ち受け、レート制限、指示文はログに残さない。
- カタログは C# の `LabCatalog.ToProxyJson()` から `proxy/catalog.json` に書き出す（唯一の出典は C#）。
- アプリ：`ProxyDesignService`（UnityWebRequest）＋ `FallbackDesignService`。接続・混雑・鍵の問題のときだけオフラインへ切替、AIの出力が不正なときはエラー表示（勝手に差し替えない）。AIの出力は必ず DnaJson / RevisionEngine で検証。
- 接続：開発中は USB の `adb reverse tcp:8787 tcp:8787` で Quest → PC。HTTP は開発ビルドのみ許可（`insecureHttpOption = DevelopmentOnly`）。
- 自動検証（`-ilabVerify`）は常にオフライン設計で実行（結果を決定的に保つため）。

## 3. テスト結果（2026-11-04 実施）

| テスト | 方法 | 結果 |
|---|---|---|
| EditMode 単体テスト | `unity test . --mode EditMode` | **41/41 合格**（REALITYモデル6件、AIプロキシ通信7件を追加） |
| プロキシ単体テスト | `cd proxy && npm test` / `npm run typecheck` | **9/9 合格**、型エラー0（Claude API は呼ばない） |
| プロキシ起動確認（APIキーなし） | `npm start` + curl | `/health` 200、キー未設定で 503 `no_api_key`、空の指示 400、JSON以外 415 |
| 実APIでの設計・改良（PCから） | プロキシ + curl | **成功**：設計 200（8〜16秒、部品5・機能4）、改良 200（約6秒、パラメータ変更3件）。記録した応答で EditMode テスト（DNA検証→実験→改良適用）合格 |
| Questからの AI 設計 | adb reverse 経由（2026-11-08） | **成功**：クイック発明「手を使わない傘」→ `POST /v1/design` 200（9.7秒）→ 実験1回 → 保存（`ai-*.json`）。2回目（キーボード入力の指示）：設計 200（8.1秒）・改良 `POST /v1/revise` 200（6.1秒）。改良案は「承認して適用」を押さなかったため保存データは Rev.1 のまま |
| Play Mode 自動通し検証 | `Unity.exe -batchmode -projectPath . -executeMethod ImpossibleLab.EditorTools.VerifyRunner.Run -ilabVerify` | **68 チェック合格 / 失敗0 / 例外0**（2026-11-08、パネル内収まりチェック・記録AI応答の画面を含む）、画面21枚を `Logs/verify/` に出力 |
| 計測例（雨量30mm/h・風速4m/s・シード2026・10秒） | 同上 | Rev.1 DREAM：被濡れ 30.8粒/秒・防護率40.9% → Rev.3（側面カバー0.25m＋半径0.8m）DREAM：17.5粒/秒・66.4% → Rev.3 REALITY（物理モデル）：33.9粒/秒・34.9%・1.306Wh（消費電力 約470W、稼働 約6分〔2〜16分〕）・流され0.42m |
| Quest APK ビルド | `QuestBuild.BuildDevelopment` | **ビルド成功**（`Builds/Android/ImpossibleLab-dev.apk`, 約63MB, 所要16分）。**実機へのインストール・起動は未実施** |

### 受け入れテスト（仕様25章）の状況

| ID | 状況 |
|---|---|
| AT-01 Questで起動・メニュー操作 | **未検証**（実機なし）。Editor で XR Device Simulator によるUI構成のみ |
| AT-02 サンプル雨具の生成 | Editor で合格（自動検証） |
| AT-03 防護範囲で計測値が変化 | EditMode・Editor で合格 |
| AT-04 「防護範囲を広げて」改良・取り消し | Editor で合格（検証済みパラメータのみ変化、Undo確認） |
| AT-05 DREAM/REALITY切替 | Editor で合格（制限・電力・限定モデル表示） |
| AT-06 保存・再起動後に復元 | 別インスタンスからの復元で合格。**アプリ再起動での確認は未実施** |
| AT-07 AI通信遮断 | AI未接続のため対象外。オフライン動作・未対応時の代替表示は合格 |
| AT-08 無効JSON・過大値 | EditMode で合格 |
| AT-09 座位と立位 | **未検証**（実機なし） |
| AT-10 6画面の通しデモ | Editor 自動操作で合格。**VRコントローラーでの通し操作は未検証** |

## 4. 未実装・未検証・既知の制約

- **Quest 3S 実機での起動・操作・可読性・フレームレート・メモリは未計測**。文字サイズ・パネル距離は初期値（実機で調整）。
- Quest単体でのテキスト入力：ビルド時に AndroidManifest へ `oculus.software.overlay_keyboard`（required=false）を追加（`Editor/AddQuestSystemKeyboard.cs`、Meta XR SDK は不要）。**Quest 3S 実機で入力欄からシステムキーボードが開き、日本語入力できることを確認（2026-11-08）**。
- 承認待ちの改良案がある状態で保存しようとすると、保存せずに「未適用の改良案があります…」と表示する（実機で利用者が承認を押し忘れたため・2026-11-08）。Editor 検証で確認。
- AI改良で、構造化出力の空の操作（partId が空の remove_part など）が混ざり「削除対象の部品がありません」で改良全体が止まった（Quest実機）。プロキシで中身のない操作だけを取り除き、プロンプトでも禁止（`proxy/test` 10/10 合格）。**Quest 3S 実機で再確認（2026-11-08）**：AI改良 200（5.2秒）→ 承認前の保存は止まる → 承認後に保存し Rev.2（改良履歴 Rev.1）で保存された。
- 音声入力、運搬ロボット・空飛ぶ靴（未対応と明示）、外観プリセット、ライトベイク、オブジェクトプール化、専用ハンドメニューは未実装。
- REALITY 係数17件のうち11件が「推定・要調査」、3件が「調整用仮定」（`docs/reality_model.md` 6章の確認作業が未了）。
- `Logs/verify/` のスクリーンショットはEditorの検証用カメラ（FOV62°）で撮影したもので、VRの見え方とは異なる。

## 5. 再現手順

```bash
# 設定・素材・シーンの再生成（冪等）
Unity.exe -batchmode -nographics -quit -projectPath D:/IMPOSSIBLE_LAB -executeMethod ImpossibleLab.EditorTools.ProjectSetup.RunAllAndBuildScene
# 単体テスト
unity test D:/IMPOSSIBLE_LAB --mode EditMode
# Play Mode 自動通し検証（-nographics なし）
Unity.exe -batchmode -projectPath D:/IMPOSSIBLE_LAB -executeMethod ImpossibleLab.EditorTools.VerifyRunner.Run -ilabVerify
# Quest APK
Unity.exe -batchmode -nographics -quit -projectPath D:/IMPOSSIBLE_LAB -buildTarget Android -executeMethod ImpossibleLab.EditorTools.QuestBuild.BuildDevelopment
```
Editor からはメニュー「IMPOSSIBLE LAB」→ 1〜5 で同じ操作ができる。

## 6. 次のタスク

1. Quest 3S 実機で AT-01 / AT-09 / AT-10 を確認（文字サイズ、パネル距離、テレポート、掴む/起動、72fps）。
2. 実機結果に基づくUI調整、ライトベイクと描画負荷の計測。
3. AI設計プロキシ（Node、APIキーはサーバー側環境変数）＋ `IInventionDesignService` のオンライン実装、タイムアウト・再試行2回・オフライン切替（AT-07）。
4. Quest向け文字入力（XRI Spatial Keyboard 等）の検討。
