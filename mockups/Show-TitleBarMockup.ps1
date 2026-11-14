# Renderiza TitleBarMockup.xaml tal cual, sin compilar nada.
# Uso:  powershell.exe -ExecutionPolicy Bypass -File .\mockups\Show-TitleBarMockup.ps1
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$xamlPath = Join-Path $PSScriptRoot 'TitleBarMockup.xaml'
[xml]$xaml = Get-Content -Path $xamlPath -Raw -Encoding UTF8
$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)
$window.ShowDialog() | Out-Null
