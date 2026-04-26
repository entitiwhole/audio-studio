@echo off
echo ==== START ====
echo Current time: %date% %time%

echo.
echo ==== PUBLISHING ====
dotnet publish "E:\Atest1.0\AudioStudio\AudioStudio\AudioStudio.csproj" -c Release -r win-x64 --self-contained -o "E:\Atest1.0\AudioStudio\AudioStudio\publish" --force

echo.
echo ==== EXE INFO ====
dir "E:\Atest1.0\AudioStudio\AudioStudio\publish\AudioStudio.exe"

echo.
echo ==== ICO INFO ====
dir "E:\Atest1.0\AudioStudio\AudioStudio\app.ico"

echo.
echo ==== DONE ====
