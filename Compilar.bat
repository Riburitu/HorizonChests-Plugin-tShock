
@echo off
setlocal
cd /d "%~dp0"

title Compilar HorizonChests

echo ==========================================
echo       tShock - Compilador de Plugins
echo ==========================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: No se encuentra dotnet.
    echo Instala el SDK de .NET 9.
    pause
    exit /b 1
)

if not exist "HorizonChests.csproj" (
    echo ERROR: No se encuentra el archivo csproj.
    pause
    exit /b 1
)

echo SDK instalado:
dotnet --version
echo.
echo Compilando en Release...
echo.

dotnet build "HorizonChests.csproj" -c Release

if errorlevel 1 (
    echo.
    echo ERROR: La compilacion ha fallado.
    pause
    exit /b 1
)

echo.
echo ==========================================
echo COMPILACION CORRECTA
echo ==========================================
echo DLL:
echo %CD%\bin\Release\net9.0\InfiniteChestsV3.dll
echo.
pause
endlocal
