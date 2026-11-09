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

- `dna` は発明DNA v2（タグ・取り付け位置・形・素材・機能対応表）。`plans` は実験計画 1〜3 案（`{title, aim, plan}`、plan はアプリの ExperimentPlan と同じ形）。どちらもアプリ側の検証器を必ず通す。
- `fixes`：プロキシで直した小さな食い違い（機能対応表に設計に無い機能が書かれていた、など）。評価で数える。
- `usage`：トークン数・所要時間・料金の目安（`src/cost.ts`、claude-opus-5-5 の公開価格。請求額そのものではない）。
- 改良の操作は `update_parameter` / `add_part` / `remove_part` / `add_behavior` / `remove_behavior`（最大6）。
- カタログ（部品・機能・実験カタログ）は Unity の `ProjectSetup.ExportProxyCatalog` で `catalog.json` に書き出す。手で編集しない。

エラーは `{status:"error", code, message}`（400/401/413/415/422/429/502/503/504）。

## 安全のための決まり

- APIキーはサーバー側の環境変数のみ。アプリ・ログ・応答に出さない
- 既定は `127.0.0.1` だけで待ち受け（LANに出さない）。LANに公開する場合は `ILAB_HOST=0.0.0.0` と `ILAB_PROXY_TOKEN` を設定し、アプリ側 `proxy.json` の `token` に同じ値を入れる
- 指示は200文字・本文32KBまで、10分あたり40回まで（`ILAB_RATE_PER_10MIN`）
- Claude の出力は構造化出力（JSONスキーマ）で形を固定し、アプリ側で発明DNA検証・改良検証を必ず通す。**AIの出力をコードとして実行しない**
- 利用者の指示文はログに残さない（記録するのはモデル名・トークン数・所要時間・HTTPステータスのみ）
- 拒否（refusal）時は `fallbacks: "default"`（beta `server-side-fallback-2026-07-01`）でサーバー側の推奨モデルに自動切替

## アプリ側の接続先を変える

Quest の `persistentDataPath/proxy.json`（`/sdcard/Android/data/jp.impossiblelab.vr/files/proxy.json`）：

```json
{ "url": "http://192.168.1.20:8787", "token": "", "enabled": true }
```

`"enabled": false` にすると常にオフライン設計になる。

## テスト

```bash
npm test          # API を呼ばない単体テスト（スキーマ・変換・入力検証）
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
