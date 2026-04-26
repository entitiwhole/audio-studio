$c = Get-Content 'E:\Atest1.0\AudioStudio\AudioStudio\MainWindow.xaml.cs'
$lines = $c[673..699]
$lines | Out-File 'E:\Atest1.0\AudioStudio\lines_674.txt' -Encoding UTF8
