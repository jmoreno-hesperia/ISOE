$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 no está disponible.' }
New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force | Out-Null
& $compiler /nologo /target:exe /platform:x64 "/out:$root\dist\BrandAssets.exe" "/resource:$root\assets\hesperiaworld.png,ISOE.brand.wordmark" "/resource:$root\assets\hesperiaworld-favicon.jpg,ISOE.brand.favicon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$root\scripts\BrandAssets.cs" "$root\src\Brand.cs"
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron generar los recursos de marca.' }
& "$root\dist\BrandAssets.exe" "$root\dist\brand"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar el icono.' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$root\dist\ISOE.exe" "/win32manifest:$root\src\app.manifest" "/win32icon:$root\dist\brand\ISOE.ico" "/resource:$root\dist\brand\ISOE.ico,ISOE.icon" "/resource:$root\assets\hesperiaworld.png,ISOE.brand.wordmark" "/resource:$root\assets\hesperiaworld-favicon.jpg,ISOE.brand.favicon" "/resource:$root\catalog-v2.json,ISOE.catalog.json" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$root\src\ISOE.cs" "$root\src\Brand.cs" "$root\src\InstalledSoftware.cs" "$root\src\UpdatePolicy.cs"
if ($LASTEXITCODE -ne 0) { throw 'La compilación ha fallado.' }
Get-FileHash (Join-Path $root 'dist\ISOE.exe') -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  ISOE.exe" } | Set-Content (Join-Path $root 'dist\SHA256SUMS.txt') -Encoding ascii
Write-Output 'Compilado: dist\ISOE.exe'
