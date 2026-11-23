@echo off
rem IMPOSSIBLE LAB：PC 側の準備をまとめて起動する（AI プロキシ・音声入力の Whisper・Quest への USB 中継）
rem 止めるときはこの窓で Ctrl+C を押す
cd /d "%~dp0proxy"
npm start
