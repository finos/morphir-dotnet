@ECHO OFF
powershell -ExecutionPolicy ByPass -NoProfile -File "%~dp0build.ps1" %*
