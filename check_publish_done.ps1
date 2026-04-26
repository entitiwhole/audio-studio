$ts = (Get-Item 'E:\Atest1.0\AudioStudio\AudioStudio\publish\AudioStudio.exe').LastWriteTime.ToString()
$ts | Out-File 'E:\Atest1.0\AudioStudio\success.txt'
