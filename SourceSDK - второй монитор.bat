@echo off
rem Запуск Source SDK Hub на весь экран на втором мониторе. Номер монитора - в параметре --screen=N.
start "" "%~dp0SourceSDK.exe" --fullscreen --screen=2
