<#
.SYNOPSIS
  Release 빌드 → 오프라인 하네스 검증 → (선택) GitHub 릴리즈 생성.

.DESCRIPTION
  버전은 mod\Core.cs 의 MelonInfo 에서 읽는다. 태그는 v<버전>.
  기본은 검증만 한다. -Publish 를 주면 gh 로 GitHub 릴리즈에 PLATiNALAB.Mod.dll 을 직접 올린다
  (CHANGELOG.md 의 해당 버전 항목을 릴리즈 노트로 쓴다).
  게임 파일·디컴파일 코드는 포함하지 않는다: 릴리즈 에셋에는 모드 DLL만 들어간다.

.EXAMPLE
  .\tools\release.ps1             # 검증
  .\tools\release.ps1 -Publish    # 검증 + 태그·GitHub 릴리즈 (PLATiNALAB.Mod.dll)
#>
param(
    [switch]$Publish,
    [string]$GameRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$core = Get-Content (Join-Path $root 'mod\Core.cs') -Raw
if ($core -notmatch 'MelonInfo\(typeof\(Core\),\s*"[^"]+",\s*"(?<v>[^"]+)"') { throw 'Core.cs 에서 버전을 찾지 못했습니다.' }
$version = $Matches['v']
$tag = "v$version"
Write-Host "버전 $version ($tag)"

if ((git status --porcelain) -ne $null -and $Publish) { throw '작업 트리가 깨끗하지 않습니다. 먼저 커밋하세요.' }

$buildArgs = @('build', 'mod\PLATiNALAB.Mod.csproj', '-c', 'Release')
if ($GameRoot) { $buildArgs += "-p:GameRoot=$GameRoot" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

$harnessArgs = @('run', '--project', 'tools\interop-harness', '-c', 'Release')
if ($GameRoot) { $env:PLATINALAB_GAME = $GameRoot }
& dotnet @harnessArgs
if ($LASTEXITCODE -ne 0) { throw '하네스 검증 실패 (종료 코드 0이어야 배포할 수 있습니다)' }

$dll = Join-Path $root 'mod\bin\Release\net6.0\PLATiNALAB.Mod.dll'
if (-not (Test-Path $dll)) { throw "DLL 없음: $dll" }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$dllHash = (Get-FileHash $dll -Algorithm SHA256).Hash
Write-Host "DLL: $dll`nSHA256: $dllHash"

if (-not $Publish) { Write-Host '(-Publish 를 주면 GitHub 릴리즈를 만듭니다)'; return }

# CHANGELOG 의 해당 버전 항목만 릴리즈 노트로
$log = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$pattern = "(?ms)^## \[$([regex]::Escape($version))\][^\r\n]*\r?\n(?<body>.*?)(?=^## \[|\z)"
$notes = if ($log -match $pattern) { $Matches['body'].Trim() } else { "v$version" }
$bt = [char]96
$notes += "`n`n**SHA256** (${bt}PLATiNALAB.Mod.dll${bt}): $bt$dllHash$bt`n`n"
$notes += "설치: ${bt}PLATiNALAB.Mod.dll${bt}을 ${bt}<게임 폴더>\Mods\${bt}에 넣으세요 (MelonLoader 0.7.3 필요)."
$notesFile = Join-Path $dist "notes-$version.md"
Set-Content -Path $notesFile -Value $notes -Encoding utf8

& gh release create $tag $dll --title "PLATiNA LAB Custom Mod $tag" --notes-file $notesFile --target main
if ($LASTEXITCODE -ne 0) { throw 'gh release create 실패' }
Write-Host "릴리즈 게시 완료: $tag"
