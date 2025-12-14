@echo off
setlocal
REM Launch auno_minifetch on port 8797
python "%~dp0auno_minifetch.py" --serve :8797
endlocal
