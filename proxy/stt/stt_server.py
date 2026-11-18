"""
IMPOSSIBLE LAB 音声入力の文字起こし（PC 上でだけ動く。音声は外部へ送らず、保存もしない）。
faster-whisper（Whisper の CPU 向け実装、MIT）で日本語の短い発話（最大 30 秒）を文字にする。
プロキシ（src/server.ts）が起動時にこのサーバーを子プロセスとして立ち上げ、/v1/transcribe から呼ぶ。

  POST /transcribe  本体：WAV（PCM 16bit、モノラルかステレオ、8〜48 kHz）→ {"text": "...", "duration": 秒, "ms": 処理時間}
  GET  /health      → {"ok": true, "model": "small"}

環境変数：ILAB_STT_PORT（既定 8788）、ILAB_STT_MODEL（既定 small。large-v3-turbo は正確だが CPU では約4倍遅い）
ログには長さと処理時間だけを出し、話した内容（文字）は出さない（仕様 24章：音声・発言を無断で記録しない）。
"""
import io
import json
import os
import sys
import time
import wave
from http.server import BaseHTTPRequestHandler, HTTPServer

import numpy as np
from faster_whisper import WhisperModel

PORT = int(os.environ.get("ILAB_STT_PORT", "8788"))
MODEL_NAME = os.environ.get("ILAB_STT_MODEL", "small")
MAX_BYTES = 2_000_000
MAX_SECONDS = 30.0
RATE = 16000


def decode_wav(data: bytes) -> np.ndarray:
    """WAV → 16 kHz モノラルの float32。形式が合わなければ ValueError。"""
    with wave.open(io.BytesIO(data)) as w:
        if w.getsampwidth() != 2:
            raise ValueError("16bit の WAV だけに対応しています")
        ch, rate, n = w.getnchannels(), w.getframerate(), w.getnframes()
        if ch not in (1, 2) or not (8000 <= rate <= 48000):
            raise ValueError("WAV の形式に対応していません")
        if n / rate > MAX_SECONDS:
            raise ValueError(f"音声は {int(MAX_SECONDS)} 秒までです")
        pcm = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float32) / 32768.0
    if ch == 2:
        pcm = pcm.reshape(-1, 2).mean(axis=1)
    if rate != RATE and len(pcm) > 1:
        t = np.arange(int(len(pcm) * RATE / rate)) * (rate / RATE)
        pcm = np.interp(t, np.arange(len(pcm)), pcm).astype(np.float32)
    return pcm


class Handler(BaseHTTPRequestHandler):
    model: WhisperModel = None

    def _send(self, status: int, obj: dict):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("content-type", "application/json; charset=utf-8")
        self.send_header("content-length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            return self._send(200, {"ok": True, "model": MODEL_NAME})
        self._send(404, {"error": "not found"})

    def do_POST(self):
        if self.path != "/transcribe":
            return self._send(404, {"error": "not found"})
        n = int(self.headers.get("content-length") or 0)
        if n <= 44 or n > MAX_BYTES:
            return self._send(413, {"error": "音声の大きさが範囲外です"})
        try:
            pcm = decode_wav(self.rfile.read(n))
        except (ValueError, wave.Error, EOFError) as e:
            return self._send(400, {"error": str(e) or "WAV を読めません"})
        started = time.time()
        segments, info = self.model.transcribe(pcm, language="ja", beam_size=1, vad_filter=True)
        text = "".join(s.text for s in segments).strip()
        ms = int((time.time() - started) * 1000)
        print(f"[stt] {len(pcm) / RATE:.1f}s -> {len(text)} chars in {ms}ms", flush=True)
        self._send(200, {"text": text, "duration": round(len(pcm) / RATE, 2), "ms": ms})

    def log_message(self, *args):  # 既定のアクセスログは出さない（上で長さと時間だけ出す）
        pass


def main():
    t = time.time()
    Handler.model = WhisperModel(MODEL_NAME, device="cpu", compute_type="int8", cpu_threads=max(1, (os.cpu_count() or 4) // 2))
    server = HTTPServer(("127.0.0.1", PORT), Handler)
    print(f"[stt] model={MODEL_NAME} loaded in {time.time() - t:.1f}s, listening on http://127.0.0.1:{PORT}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    sys.exit(main())
