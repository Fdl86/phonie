@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RENORMALIZE-GIT-INDEX-DEV0.4.2.0.ps1"
if errorlevel 1 (
  echo.
  echo ECHEC - ne pas pousser le depot.
  pause
  exit /b 1
)
echo.
echo OK - revenir dans GitHub Desktop pour verifier puis committer.
pause
