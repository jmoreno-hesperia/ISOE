$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 no está disponible.' }
New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force | Out-Null
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$root\dist\ISOE.exe" "/win32manifest:$root\src\app.manifest" "/resource:$root\catalog.json,ISOE.catalog.json" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$root\src\ISOE.cs"
if ($LASTEXITCODE -ne 0) { throw 'La compilación ha fallado.' }
Get-FileHash (Join-Path $root 'dist\ISOE.exe') -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  ISOE.exe" } | Set-Content (Join-Path $root 'dist\SHA256SUMS.txt') -Encoding ascii
Write-Output 'Compilado: dist\ISOE.exe'
