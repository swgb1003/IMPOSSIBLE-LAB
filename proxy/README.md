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
| GET | `/health` | — | `{ok, model, apiKey, categories}` |
| POST | `/v1/design` | `{request}`（200文字以内） | `{status:"ok", dna, designPoints}` / `{status:"unsupported", message, suggestions}` |
| POST | `/v1/revise` | `{dna, instruction, lastResult?}` | `{status:"ok", proposal}` / `{status:"unsupported", message}` |

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
