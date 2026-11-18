#requires -version 5.1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$obj = Join-Path $root 'obj'
$assets = Join-Path $root 'assets'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

if (-not (Test-Path $csc)) { throw "找不到 C# 编译器：$csc" }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
New-Item -ItemType Directory -Force -Path $obj | Out-Null

$sources = @(
    (Join-Path $src 'Program.cs'),
    (Join-Path $src 'AssemblyInfo.cs'),
    (Join-Path $src 'Models.cs'),
    (Join-Path $src 'Native.cs'),
    (Join-Path $src 'Scanner.cs'),
    (Join-Path $src 'PackageScanner.cs'),
    (Join-Path $src 'ComProbe.cs'),
    (Join-Path $src 'FileTypes.cs'),
    (Join-Path $src 'Manager.cs'),
    (Join-Path $src 'Converters.cs'),
    (Join-Path $src 'RelayCommand.cs'),
    (Join-Path $src 'ViewModel.cs'),
    (Join-Path $src 'MainWindow.cs')
)

$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.XML.dll'),
    (Join-Path $fw 'System.Xml.Linq.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Xaml.dll'),
    (Join-Path $fw 'WPF\WindowsBase.dll'),
    (Join-Path $fw 'WPF\PresentationCore.dll'),
    (Join-Path $fw 'WPF\PresentationFramework.dll')
)

$out = Join-Path $obj 'ContextMenuManager.exe'
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/warn:4',
    '/codepage:65001',
    ('/out:"' + $out + '"'),
    ('/win32icon:"' + (Join-Path $assets 'app.ico') + '"'),
    ('/win32manifest:"' + (Join-Path $src 'app.manifest') + '"'),
    ('/resource:"' + (Join-Path $src 'MainWindow.xaml') + '",Cmm.MainWindow.xaml'),
    ('/resource:"' + (Join-Path $assets 'app.png') + '",Cmm.app.png')
)
foreach ($r in $refs) { $cscArgs += ('/r:"' + $r + '"') }
foreach ($s in $sources) { $cscArgs += ('"' + $s + '"') }

Write-Host '编译中…'
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$localized = Join-Path $dist '右键菜单管理器.exe'
Copy-Item -LiteralPath $out -Destination $localized -Force
$legacyCopy = Join-Path $dist 'ContextMenuManager.exe'
if (Test-Path -LiteralPath $legacyCopy) { Remove-Item -LiteralPath $legacyCopy -Force }

$exe = Get-Item -LiteralPath $localized
Write-Host ("完成：{0} ({1:N0} 字节)" -f $exe.FullName, $exe.Length)
