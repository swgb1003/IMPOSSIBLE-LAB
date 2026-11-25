# IMPOSSIBLE LAB AI設計プロキシ

Quest のアプリと Claude API の間に置く中継サーバー。**APIキーはこのサーバーの `.env` だけに置き、アプリには入れない**（CLAUDE.md 実行手順5）。

```
Quest アプリ ──HTTP（USB: adb reverse）──▶ このPCのプロキシ :8787 ──HTTPS──▶ Claude API（claude-opus-5-5）
```

## 初回セットアップ

1. Node.js 22.18 以上（このPCは v24）
2. `cd proxy && npm install`
3. `.env.example` を `.env` にコピーし、`ANTHROPIC_API_KEY=` の後ろに APIキーを書く（`.env` は `.gitignore` 済み。チャットやコードには貼らない）
4. カタログ `catalog.json` は Unity から書き出す：メニュー「IMPOSSIBLE LAB → 6. AIプロキシ用カタログを書き出す」（「1. プロジェクト設定」でも自動で書き出す）。手で編集しない

## 起動と接続

```bash
cd proxy
npm start                       # http://127.0.0.1:8787 で待ち受け
adb reverse tcp:8787 tcp:8787   # Quest を USB 接続した状態で。Quest の 127.0.0.1:8787 → このPC
```

- 動作確認：`curl http://127.0.0.1:8787/health` → `"apiKey": true` なら準備完了
- Quest を再接続・再起動したら `adb reverse` をもう一度実行する
- プロキシが止まっている・接続できないときは、アプリは自動でオフラインのサンプル設計図に切り替える（画面に表示）

## API

| メソッド | パス | 入力 | 出力 |
|---|---|---|---|
| GET | `/health` | — | `{ok, model, effort, apiKey, schemaVersion, behaviors}` |
| POST | `/v1/design` | `{request}`（200文字以内） | `{status:"ok", dna(v2), designPoints, plans, fixes, usage}` / `{status:"unsupported", message, suggestions}` |
| POST | `/v1/plan` | `{dna, wish?}` | `{status:"ok", plans, usage}` / `{status:"unsupported", message}` |
| POST | `/v1/revise` | `{dna, instruction, lastResult?}` | `{status:"ok", proposal, usage}` / `{status:"unsupported", message}` |

- `dna` は発明DNA v3（タグ・取り付け位置・形・素材・機能対応表。必要なときだけ動作プログラム `program` と部品の関節 `joint`、仕様 28章）。実験計画の `approach_device` には期待 `expect` が付く。`plans` は実験計画 1〜3 案（`{title, aim, plan}`、plan はアプリの ExperimentPlan と同じ形）。どちらもアプリ側の検証器を必ず通す。
- `fixes`：プロキシで直した小さな食い違い（機能対応表に設計に無い機能が書かれていた、など）。評価で数える。
- `usage`：トークン数・所要時間・料金の目安（`src/cost.ts`、claude-opus-5-5 の公開価格。請求額そのものではない）。
- 改良の操作は `update_parameter` / `add_part` / `remove_part` / `add_behavior` / `remove_behavior` と、動作プログラムの `add_rule` / `remove_rule` / `update_rule` / `add_joint` / `update_joint`（最大6）。ルール・関節はアプリの DNA の検証器で検証する。
- カタログ（部品・機能・実験カタログ）は Unity の `ProjectSetup.ExportProxyCatalog` で `catalog.json` に書き出す。手で編集しない。

エラーは `{status:"error", code, message}`（400/401/413/415/422/429/502/503/504）。

## 安全のための決まり

- APIキーはサーバー側の環境変数のみ。アプリ・ログ・応答に出さない
- 既定は `127.0.0.1` だけで待ち受け（LANに出さない）。LANに公開する場合は `ILAB_HOST=0.0.0.0` と `ILAB_PROXY_TOKEN` を設定し、アプリ側 `proxy.json` の `token` に同じ値を入れる
- 指示は200文字・本文32KBまで、10分あたり40回まで（`ILAB_RATE_PER_10MIN`）
- Claude の出力は構造化出力（JSONスキーマ）で形を固定し、アプリ側で発明DNA検証・改良検証を必ず通す。**AIの出力をコードとして実行しない**
- 利用者の指示文はログに残さない（記録するのはモデル名・トークン数・所要時間・HTTPステータスのみ）
- 拒否（refusal）時は `fallbacks: "default"`（beta `server-side-fallback-2026-07-01`）でサーバー側の推奨モデルに自動切替

## 発明ライブラリ（IMPOSSIBLE LIBRARY、2026-11-25）

Quest で公開した発明を PC の中のサイトに載せ、研究者・開発者が採用する流れ（設計は `docs/PUBLISH_LIBRARY.md`）。AI は呼ばない。データは `proxy/library/`（`ILAB_LIBRARY_DIR` で変更、git に入れない）に保存し、PC の外には出さない。

| メソッド | パス | だれが | 内容 |
|---|---|---|---|
| POST | `/library` | Quest | 公開・更新（`{publication, glbBase64}`、2MB まで）。同じ端末の同じ発明はページを更新 |
| GET | `/library/mine` | Quest | 自分の公開の状態・ほしい・採用の知らせ・やりとりの数 |
| POST | `/library/:id/withdraw` | Quest | 取り下げ（公開した端末だけ） |
| POST | `/library/:id/tried` | Quest | VR で呼び出して試した |
| POST | `/library/:id/want` | Quest・サイト | ほしい。Quest は試した端末だけ、サイトは `{voter}`（ブラウザごと）。別々に数える |
| GET | `/library?sort=&tag=&scene=&status=` | だれでも | 一覧（並び：wants・new・adoptions・name） |
| GET | `/library/:id`・`/library/:id/model.glb` | だれでも | 1件の公開データと 3D |
| POST | `/library/:id/consider`・`/adopt` | サイト | 検討中・採用（`{accountId}`・`{accountId, reason}`。理由は必須） |
| GET・POST | `/accounts`、GET `/accounts/:id` | サイト | 研究者・開発者の登録と一覧 |
| GET | `/creators/:publicId` | サイト | 発案者のページ（表示名で公開したものだけ） |
| GET | `/threads`・`/threads/:id`、POST `/threads/:id/messages` | Quest・サイト | 採用した人と発案者のやりとり（2人だけ） |
| GET | `/admin/library`、POST `/admin/accounts/:id/verify`・`/admin/library/:id/takedown` | サイト | 運営（確認済みの印・取り下げ） |

- **Quest** は端末ごとの発案者キーを `x-ilab-creator` で送る（16〜64 文字の英数字・`-`・`_`）。サーバーはキーを保存せずハッシュだけ持つ。`ILAB_PROXY_TOKEN` を設定したときは `x-ilab-token` も要る。
- **サイトと運営**の入口は、この PC の中（127.0.0.1）からだけ受け付ける。サイトでの名乗りは `x-ilab-as: account:<ID>` か `creator:<発案者の公開 ID>`（ログインは形だけ）。
- 公開データの形は `src/library/schema.ts`（知らない欄は捨てる、文字数と数の上限、同意の版 `2026-11-25`）。GLB は見出しと JSON の区切りを確かめ、1MB まで。
- URL には ID だけを入れる（ログに URL が出るため）。依頼文・やりとりの本文はログに書かない。
- 書き込みは 10 分あたり 600 回まで（AI の回数制限とは別）。

## アプリ側の接続先を変える

Quest の `persistentDataPath/proxy.json`（`/sdcard/Android/data/jp.impossiblelab.vr/files/proxy.json`）：

```json
{ "url": "http://192.168.1.20:8787", "token": "", "enabled": true }
```

`"enabled": false` にすると常にオフライン設計になる。

## テスト

```bash
npm test          # API を呼ばない単体テスト（スキーマ・変換・入力検証・発明ライブラリ）
npm run typecheck # TypeScript 型チェック
```

## AI 評価セット（料金がかかる）

`eval/requests.json`：日常の困りごと 40 件（設計してほしい 38 件・断ってほしい 2 件、指示の書き換えを含む）。

```bash
npm start                                   # 別ターミナル（10分40回の制限に注意：ILAB_RATE_PER_10MIN=100 npm start）
npm run eval                                # 全件 → eval/results/design-<日時>.jsonl
node eval/run-eval.ts --only r01,r15        # 一部だけ
```

集計は Unity の `IMPOSSIBLE LAB/5. AI評価セットを集計`（`AiEvalReport.Run`、API は呼ばない）。最新の結果を、アプリと同じ検証器で数えて
`docs/eval/<同じ名前>.md`（①設計が通る割合 ②実験計画が通る割合 ③「検証できない」の割合 ④時間・費用）と、人の5段階評価を記入する `-rating.csv` を作る。

## 音声入力（文字起こし、2026-11-18）

Quest の「● 話す」で録音した声（WAV）を `POST /v1/transcribe`（content-type `audio/wav`、2MB・30 秒まで）で受け取り、PC 上の Whisper で文字にして `{status:"ok", text}` を返す。声は外部へ送らず、保存もしない。ログは長さと処理時間だけ（内容は出さない）。

準備（初回だけ）：
```bash
cd proxy
python -m venv stt/.venv
stt/.venv/Scripts/python.exe -m pip install -r stt/requirements.txt   # Windows（mac/Linux は stt/.venv/bin/python）
```
`npm start` で、`stt/.venv` があればプロキシが `stt/stt_server.py`（127.0.0.1:8788）を子プロセスで起動する。初回はモデル（small、約 0.5GB）をダウンロードする。`ILAB_STT=off` で無効、`ILAB_STT_MODEL=large-v3-turbo` でより正確なモデル（CPU では約 4 倍遅い）。

## まとめて起動（2026-11-23）

リポジトリ直下の `start_lab.cmd` をダブルクリック（または `cd proxy && npm start`）。プロキシ・音声入力の Whisper（`stt/.venv` がある場合）を起動し、USB でつないだ Quest への `adb reverse tcp:8787 tcp:8787` を15秒ごとに張り直す（抜き差ししても自動でつながる）。adb の場所は環境変数 `ADB` で変えられ、`ILAB_ADB_REVERSE=off` で中継をしない。Quest の S01 に「PC とつながっています／つながっていません」が出る。

