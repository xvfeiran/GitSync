@echo off
setlocal
rem ============================================================
rem  Publish a portable multi-file folder + zip.
rem  NOTE: a single-file exe is not usable on locked-down machines -
rem  security policy may block unsigned exe writing to %TEMP%,
rem  which breaks .NET single-file self-extraction.
rem  Output: dist\ (folder) and dist\GitSync-portable.zip
rem  Keep this file ASCII-only: cmd.exe mis-parses UTF-8 text.
rem ============================================================

set "ROOT=%~dp0"
cd /d "%ROOT%"

echo [1/3] Cleaning previous output...
if exist "dist" rmdir /s /q "dist"
if exist "publish" rmdir /s /q "publish"

echo [2/3] Building self-contained folder...
dotnet publish "VendorSync.csproj" -c Release -o "publish" --nologo
if errorlevel 1 goto fail

echo [3/3] Collecting portable package...
mkdir "dist" 2>nul
xcopy /e /i /y "publish" "dist" >nul
if errorlevel 1 goto fail

if exist "README.txt" copy /y "README.txt" "dist\README.txt" >nul

powershell -NoProfile -Command "Compress-Archive -Path 'dist\*' -DestinationPath 'dist\GitSync-portable.zip' -Force"
if errorlevel 1 goto fail

echo.
echo Done: %ROOT%dist\VendorSync.exe  (plus runtime files)
echo Zip : %ROOT%dist\VendorSync-portable.zip
exit /b 0

:fail
echo.
echo Publish failed. See the error output above.
exit /b 1
