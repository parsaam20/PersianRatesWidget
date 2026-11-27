@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo  Building PersianRatesWidget Standalone EXE
echo ============================================

dotnet publish "%~dp0PersianRatesWidget.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0publish"
if %errorlevel% neq 0 (
    echo [ERROR] dotnet publish failed. Make sure .NET 8 SDK is installed.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  Building Windows Installer (Setup.exe)
echo ============================================

set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_PATH%" set "ISCC_PATH=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"

if exist "%ISCC_PATH%" (
    "%ISCC_PATH%" "%~dp0installer.iss"
    if %errorlevel% equ 0 (
        echo.
        echo [SUCCESS] Installer created in: .\installer_output\PersianRatesWidget_Setup_v1.0.0.exe
    ) else (
        echo [ERROR] Inno Setup compilation failed.
    )
) else (
    echo [INFO] Inno Setup 6 not found.
    echo        Standalone EXE is ready at: .\publish\PersianRatesWidget.exe
    echo        To also create Setup.exe, install Inno Setup 6:
    echo        winget install JRSoftware.InnoSetup
)

pause
