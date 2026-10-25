@echo off
chcp 65001 >nul
setlocal
title Сборка Source SDK Hub

rem Ищем компилятор C#, который уже встроен в Windows (.NET Framework 4.x)
set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not defined CSC (
    echo Не найден компилятор .NET Framework 4.x ^(csc.exe^).
    echo Включите компонент ".NET Framework 4.8" в "Включение или отключение компонентов Windows".
    pause
    exit /b 1
)

rem Результат кладём в корень SDK - рядом с Crowbar.exe
set "OUT=%~dp0..\SourceSDK.exe"

rem Если оболочка сейчас запущена, exe занят - закройте её перед сборкой.
echo Компилятор: %CSC%
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
    /out:"%OUT%" ^
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Microsoft.VisualBasic.dll ^
    "%~dp0Loc.cs" "%~dp0Native.cs" "%~dp0Theme.cs" "%~dp0Dialogs.cs" "%~dp0Pane.cs" "%~dp0Explorer.cs" "%~dp0Views.cs" "%~dp0Workspace.cs" "%~dp0Stages.cs" "%~dp0Notify.cs" "%~dp0Vtf.cs" "%~dp0Mdl.cs" "%~dp0Anim.cs" "%~dp0Phy.cs" "%~dp0Bindings.cs" "%~dp0Gl.cs" "%~dp0ModelView.cs" "%~dp0SourceSDK.cs"

if errorlevel 1 (
    echo.
    echo Ошибка сборки.
    pause
    exit /b 1
)

echo.
echo Готово: %OUT%
echo Запускайте SourceSDK.exe из корневой папки SDK.
if /i not "%~1"=="nopause" pause
