@echo off
setlocal

echo ============================================
echo   CBT Exam System - Professional Build
echo ============================================
echo.

set PROJECT=src\CbtExam.Desktop\CbtExam.Desktop.csproj
set OUTPUT=publish\CbtExam

echo [1/3] Closing running instances of CbtExam.exe...
taskkill /F /IM CbtExam.exe /T >nul 2>&1
timeout /t 1 /nobreak >nul

echo [2/3] Preparing clean output directory...
if exist "%OUTPUT%" (
    echo   Cleaning %OUTPUT%...
    rd /s /q "%OUTPUT%" >nul 2>&1
    if exist "%OUTPUT%" (
        echo   WARNING: Manual cleaning failed. Attempting force delete of EXE...
        del /f /q "%OUTPUT%\CbtExam.exe" >nul 2>&1
    )
)
if not exist "%OUTPUT%" mkdir "%OUTPUT%"

echo [3/4] Restoring NuGet packages...
"C:\Program Files\dotnet\dotnet.exe" restore CbtExam.sln
"C:\Program Files\dotnet\dotnet.exe" restore "%PROJECT%" --runtime win-x64 --force

echo [4/4] Building and Publishing (Release win-x64)...
"C:\Program Files\dotnet\dotnet.exe" publish "%PROJECT%" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  --output "%OUTPUT%"

if %errorlevel% neq 0 (
    echo.
    echo --------------------------------------------
    echo   BUILD FAILED! 
    echo   Please check the error messages above.
    echo --------------------------------------------
    powershell -Command "(New-Object Media.SoundPlayer 'C:\Windows\Media\Windows Critical Stop.wav').PlaySync()"
    pause
    exit /b 1
)

echo.
echo ============================================
echo   SUCCESS! New version compiled.
echo ============================================
echo Output: %CD%\%OUTPUT%\CbtExam.exe
echo.
echo NOTE: On first run, the app will update your
echo database schema automatically.
echo.
powershell -Command "(New-Object Media.SoundPlayer 'C:\Windows\Media\Windows Notify System Generic.wav').PlaySync()"
timeout /t 3 /nobreak >nul
