param([switch]$FrameworkDependent)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$destination=Join-Path $root 'dist\claude-in-codex'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$plugin=Join-Path $destination 'plugins\claude-in-codex'
$argsList=@('publish',(Join-Path $root 'plugins\claude-in-codex\src\ClaudeCompanion\ClaudeCompanion.csproj'),'-c','Release','-r','win-x64','-o',(Join-Path $plugin 'bin'),'--self-contained',(-not $FrameworkDependent).ToString().ToLowerInvariant())
& dotnet @argsList
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($folder in @('.codex-plugin','skills','scripts')) {Copy-Item -LiteralPath (Join-Path $root "plugins\claude-in-codex\$folder") -Destination $plugin -Recurse -Force}
Copy-Item -LiteralPath (Join-Path $root '.agents') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $destination -Force
$zip=Join-Path $root 'dist\claude-in-codex-win-x64.zip'
Compress-Archive -Path (Join-Path $destination 'plugins'),(Join-Path $destination '.agents'),(Join-Path $destination 'README.md'),(Join-Path $destination 'LICENSE') -DestinationPath $zip -Force
Write-Output $zip
