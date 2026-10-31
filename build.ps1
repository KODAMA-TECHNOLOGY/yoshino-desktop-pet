$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc=Join-Path $framework 'csc.exe'
$out=Join-Path $root 'build'
New-Item -ItemType Directory -Path (Join-Path $out 'assets') -Force | Out-Null
$references=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Xaml.dll') | ForEach-Object { '/reference:'+(Join-Path $framework $_) }
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:'+(Join-Path $framework ('WPF\'+$_)) }
$sources=Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName
& $csc /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/win32manifest:'+(Join-Path $root 'app.manifest')) ('/out:'+(Join-Path $out 'YoshinoPet.exe')) $references $sources
if($LASTEXITCODE -ne 0) { throw 'C# build failed' }
Copy-Item -LiteralPath (Join-Path $root 'app.config') -Destination (Join-Path $out 'YoshinoPet.exe.config') -Force
if(Test-Path -LiteralPath (Join-Path $root 'assets\yoshino-v2.png')) { Copy-Item -LiteralPath (Join-Path $root 'assets\yoshino-v2.png') -Destination (Join-Path $out 'assets\yoshino-v2.png') -Force }
if(Test-Path -LiteralPath (Join-Path $root 'assets\hq')) { Copy-Item -LiteralPath (Join-Path $root 'assets\hq') -Destination (Join-Path $out 'assets') -Recurse -Force }
Write-Output (Join-Path $out 'YoshinoPet.exe')
