@echo off
set VM_OPTIONS=
start "" "javaw" %VM_OPTIONS% -cp "%~dp0\bspsrc.jar" info.ata4.bspsrc.app.info.BspInfo %*