$pub = 'E:\Atest1.0\AudioStudio\AudioStudio\publish\AudioStudio.exe'
$src = 'E:\Atest1.0\AudioStudio\AudioStudio\MainWindow.xaml'
$cs = 'E:\Atest1.0\AudioStudio\AudioStudio\MainWindow.xaml.cs'

$out = @()
$out += "=== EXE ==="
$out += (Get-Item $pub).LastWriteTime.ToString()
$out += "=== XAML ==="
$out += (Get-Item $src).LastWriteTime.ToString()
$out += "=== CS ==="
$out += (Get-Item $cs).LastWriteTime.ToString()

$out | Out-File 'E:\Atest1.0\AudioStudio\timestamps.txt' -Encoding UTF8
