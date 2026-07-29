@echo off
REM ============================================================================
REM  Epicora Checkup - protótipo PowerShell (Fase 1) e fallback permanente.
REM
REM  Contorna a ExecutionPolicy sem alterá-la na máquina do cliente:
REM  -ExecutionPolicy Bypass vale apenas para este processo.
REM
REM  Rode como Administrador para a coleta completa. Sem elevação também roda:
REM  os coletores privilegiados aparecem como "Ignorado - sem privilégio"
REM  e o relatório sai parcial e honesto.
REM ============================================================================
setlocal

set "SCRIPT=%~dp0Invoke-EpicoraCheckup.ps1"

if not exist "%SCRIPT%" (
    echo.
    echo  ERRO: Invoke-EpicoraCheckup.ps1 nao foi encontrado ao lado deste .bat
    echo  Caminho esperado: %SCRIPT%
    echo.
    pause
    exit /b 1
)

echo.
set /p TECH="Tecnico responsavel: "
set /p CLIENTE="Empresa cliente: "
set /p DIAG="Numero do diagnostico (ex. DIAG-2026-014): "
set /p MAQUINA="Identificacao da maquina no padrao do cliente (ex. ADM-04): "
set /p RESP="Responsavel / usuario principal: "
set /p SETOR="Setor: "
echo.

powershell.exe -NoProfile -NoLogo -ExecutionPolicy Bypass -File "%SCRIPT%" ^
    -Technician "%TECH%" ^
    -Client "%CLIENTE%" ^
    -DiagnosticId "%DIAG%" ^
    -MachineLabel "%MAQUINA%" ^
    -Responsible "%RESP%" ^
    -Department "%SETOR%"

echo.
pause
endlocal
