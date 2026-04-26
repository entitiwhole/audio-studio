$ErrorActionPreference = 'SilentlyContinue'
$path = 'E:\Atest1.0\AudioStudio\AudioStudio\publish'
$items = Get-ChildItem $path
Write-Host "Items in publish folder:"
foreach ($item in $items) {
    Write-Host $item.Name
}
Write-Host ""
Write-Host "Total count:"
Write-Host $items.Count
