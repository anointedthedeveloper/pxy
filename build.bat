@echo off
setlocal

echo ============================================
echo   CBT Exam System - Professional Build
echo ============================================
echo.

set PROJECT=src\CbtExam.Desktop\CbtExam.Desktop.csproj
set OUTPUT=publish\CbtExam

echo [1/4] Syncing wwwroot folders...
echo   Copying from main wwwroot to embedded wwwroot...
robocopy "wwwroot" "src\CbtExam.Desktop\wwwroot" /E /NFL /NDL /NJH /NJS >nul 2>&1
echo   Sync complete.
echo.

echo [2/4] Closing running instances of CbtExam.exe...
taskkill /F /IM CbtExam.exe /T >nul 2>&1
timeout /t 1 /nobreak >nul

echo [3/5] Preparing clean output directory...
if exist "%OUTPUT%" (
    echo   Cleaning %OUTPUT%...
    rd /s /q "%OUTPUT%" >nul 2>&1
    if exist "%OUTPUT%" (
        echo   WARNING: Manual cleaning failed. Attempting force delete of EXE...
        del /f /q "%OUTPUT%\CbtExam.exe" >nul 2>&1
    )
)
if not exist "%OUTPUT%" mkdir "%OUTPUT%"

echo [4/5] Restoring NuGet packages...
"C:\Program Files\dotnet\dotnet.exe" restore CbtExam.sln
"C:\Program Files\dotnet\dotnet.exe" restore "%PROJECT%" --runtime win-x64 --force

echo [5/5] Building and Publishing (Release win-x64)...
"C:\Program Files\dotnet\dotnet.exe" publish "%PROJECT%" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  --output "%OUTPUT%"

echo [Post-Build] Unblocking executable for Windows Smart App Control...
powershell -Command "Unblock-File -Path '%OUTPUT%\CbtExam.exe' -ErrorAction SilentlyContinue"
echo   Executable unblocked.

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
