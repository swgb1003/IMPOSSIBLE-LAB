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

## 発明ライブラリー（IMPOSSIBLE LIBRARY、2026-11-25。ログインと見せる範囲は 2026-11-26）

Quest で公開した発明を PC の中のサイトに載せ、開発者が採用する流れ（設計は `docs/PUBLISH_LIBRARY.md`）。AI は呼ばない。データは `proxy/library/`（`ILAB_LIBRARY_DIR` で変更、git に入れない）に保存し、PC の外には出さない。

**Quest の入口**（端末ごとの発案者キーを `x-ilab-creator` で送る）

| メソッド | パス | 内容 |
|---|---|---|
| POST | `/library` | 公開・更新（`{publication, glbBase64, images?}`、4MB まで。写真は JPEG 4 枚・1 枚 300KB まで）。同じ端末の同じ発明はページを更新 |
| GET | `/library/mine` | 自分の公開の状態・ほしい・試した数・採用の知らせ（採用した人の名前・所属・紹介）・やりとりの数 |
| POST | `/library/:id/withdraw`・`/tried`・`/want` | 取り下げ（公開した端末だけ）・VR で試した・ほしい（試した端末だけ） |
| POST | `/library/pair` | サイトに入る6桁の番号（10 分有効、1回だけ使える） |
| GET・POST | `/threads`・`/threads/:id`・`/threads/:id/messages` | 採用した人とのやりとり |
| GET | `/library?sort=&tag=&scene=&status=`・`/library/:id`・`model.glb`・`images/<0〜3>.jpg` | 一覧・1件（だれでも見られる形）・3D・写真（サイトも読む） |

**サイトの入口**（`/api/`。この PC の中からだけ。本人はログインの記録の cookie で決める）

| メソッド | パス | だれが | 内容 |
|---|---|---|---|
| GET | `/api/session` | だれでも | ログインしている人と、新しい知らせの数 |
| POST | `/api/signup`・`/api/login`・`/api/logout`・`/api/pair` | だれでも | 新規登録（開発者）・ログイン・ログアウト・Quest の番号で入る（発案者） |
| POST | `/api/verify-email`・`/api/password/forgot`・`/api/password/reset` | だれでも | メールアドレスの確認、パスワードの再設定 |
| GET・POST | `/api/me`・`/api/me/password`・`/api/me/delete`・`/api/logout-all` | ログインした人 | 自分の登録と見せる項目・パスワードの変更・退会・すべての端末でログアウト |
| GET・POST | `/api/notifications`・`/api/notifications/read` | ログインした人 | 知らせ（採用・検討中・返事・所属の確認・取り下げ） |
| GET | `/api/creators/:publicId` | だれでも | 表示名で公開した発案者のページ |
| GET | `/api/library/:id` | ログインした人 | 詳しい内容つきの1件（開発者・公開した発案者・運営。ほかの発案者にはだれでも見られる形）。開発者が読んだ記録を残す |
| POST | `/api/library/:id/consider`・`/adopt` | 開発者 | 検討中・採用（`{reason}`。理由は必須） |
| POST | `/api/library/:id/report` | だれでも | 運営に知らせる（通報） |
| GET・POST | `/api/threads`・`/api/threads/:id`・`/messages` | 当事者の2人 | やりとり |
| GET | `/api/mail` | だれでも | このデモで送ったメール（外へは送らない） |
| GET・POST | `/api/admin/library`・`/accounts`・`/reports`、`…/verify`・`…/takedown`・`…/resolve` | 運営 | 通報・取り下げ・確認済みの印 |

- **ログイン**：パスワードは scrypt で元に戻せない形にして保存する。ログインの記録は cookie（`HttpOnly`・`SameSite=Strict`、14 日）で、サーバーには札のハッシュだけを残す。ログインの失敗が 15 分に 5 回続くと待たせる。
- **見せる範囲**：発明のだれでも見られる形は、名前・何をするものか・3D と写真・実験の結果の要点（主な計測と合否）・ほしいと採用の数だけ。依頼の原文・部品としくみ・実験の条件とほかの計測・改良の履歴は、ログインした開発者（と公開した発案者・運営）だけに返し、開発者が読んだ記録（開発者ごとに最初と最後の時刻）を運営の画面に出す。開発者の一覧と個人のページは無く、名前・所属・専門・紹介は、採用した発明の発案者と運営にだけ見せる。検討中の知らせには、だれかを書かない。メールアドレスは本人と運営にだけ見せる。やりとりは当事者の2人だけ（ほかの人には、あるかどうかも見せない）。
- **守り**：この PC の中からの要求は、宛先名（Host）が `127.0.0.1`・`localhost` のときだけ受け付ける（DNS リバインディング対策）。サイトの書き込みは、送り元のページ（Origin）が同じサイトのときだけ。画面には CSP などの見出しを付ける。
- **メール**：外へは送らない。登録の確認と再設定のメールは、送ったメールの画面（`/mail`）に入る。リンクの札は `#` の後ろに置き、サーバーのアドレスの記録に残さない。
- 公開データの形は `src/library/schema.ts`（知らない欄は捨てる、文字数と数の上限、同意の版 `2026-11-26`。前の版 `2026-11-25` も受け付ける）。GLB は見出しと JSON の区切りを確かめ、1MB まで。
- URL には ID だけを入れる（ログに URL が出るため）。依頼文・やりとりの本文・パスワードはログに書かない。
- 書き込みは 10 分あたり 600 回まで（AI の回数制限とは別）。
- **画面**（`http://127.0.0.1:8787/`。部品は `site/` を `/site/` で、three.js は `node_modules/three` を `/vendor/three/` で出す）：入口 `/`・一覧 `/inventions`・1件 `/inventions/<ID>`・発案者 `/creators/<ID>`・ログイン `/login`・新規登録 `/signup`・Quest の番号で入る `/pair`・パスワードの再設定 `/forgot`・自分のページ `/me`・設定 `/settings`・知らせ `/notifications`・やりとり `/messages`・運営 `/admin`・利用規約 `/terms`・個人情報の扱い `/privacy`・送ったメール `/mail`。ログインが要る画面は、ログインしていなければ `/login` へ送る。前のアドレス（`/site/〇〇.html?id=`）は新しいアドレスへ移す。
- **見本の人のログイン**（PC の中だけのデモ用。パスワードはどれも `mihon-2026`）：開発者 `takase@sample.invalid`・`morishita@sample.invalid`・`nonomura@sample.invalid`、見本の発案者 `creator@sample.invalid`、運営 `unei@sample.invalid`。
- **Quest から送る本文を確かめる**：通し検証が保存の画面から公開したときの本文を `Logs/verify/library-publish-body.json` に残す。`npm run check-publish` で PC 側の形と中身（GLB・写真）の確かめに通るかを見る。
- **見本を入れる**：Unity の通し検証が `Logs/verify/library/<発明ID>/` に公開データ・3D・写真を書き出す。`npm run samples` でそれを形を確かめてからライブラリに公開し（見本の印つき、表示名は「見本の発案者」、何度入れても増えない）、見本の開発者 3 人と、見本の発案者・運営のログインも入れ、サイトがライブラリにつながらないときに読む見本のファイル（`site/sample/`。1件はだれでも見られる形だけ。入口でしくみを紹介する見本1件だけは詳しい内容つきの `showcase.json`）も作り直す。見本の人のログインだけを入れるときは `npm run samples -- --logins-only`。

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

