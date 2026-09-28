$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$Repo = 'skwlrna/Mbi_auto_update'
$ExpectedUser = 'skwlrna'
$LogRepo = 'skwlrna/Mbi_auto_log'
$LogPath = Join-Path $PSScriptRoot 'GITHUB_MIGRATION_LOG.txt'

try { Start-Transcript -Path $LogPath -Force | Out-Null } catch {}

function Finish([int]$Code, [string]$Message) {
    Write-Host ""
    if ($Code -eq 0) {
        Write-Host $Message -ForegroundColor Green
    } else {
        Write-Host $Message -ForegroundColor Red
        Write-Host "로그: $LogPath" -ForegroundColor Yellow
    }
    Write-Host ""
    try { Stop-Transcript | Out-Null } catch {}
    Read-Host "Enter 키를 누르면 종료합니다"
    exit $Code
}

function Refresh-Path {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $extra = @(
        "$env:ProgramFiles\Git\cmd",
        "$env:ProgramFiles\GitHub CLI",
        "$env:LOCALAPPDATA\Programs\Git\cmd"
    ) -join ';'
    $env:Path = "$machine;$user;$extra"
}

function Ensure-Command([string]$Command, [string]$WingetId, [string]$DisplayName) {
    Refresh-Path
    if (Get-Command $Command -ErrorAction SilentlyContinue) { return }

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "$DisplayName 이(가) 필요합니다. Microsoft Store의 App Installer(winget)를 설치한 뒤 다시 실행해 주세요."
    }

    Write-Host "[준비] $DisplayName 설치 중..." -ForegroundColor Yellow
    & winget install --id $WingetId -e --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "$DisplayName 설치 실패 (code=$LASTEXITCODE)"
    }

    Refresh-Path
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        throw "$DisplayName 설치 후에도 $Command 명령을 찾지 못했습니다. PC를 재부팅한 뒤 다시 실행해 주세요."
    }
}

Write-Host "============================================================"
Write-Host " MABI AUTO V0.1.76 -> NEW GITHUB ONE CLICK MIGRATION"
Write-Host " target: $Repo"
Write-Host "============================================================"
Write-Host ""

try {
    Ensure-Command 'git' 'Git.Git' 'Git for Windows'
    Ensure-Command 'gh' 'GitHub.cli' 'GitHub CLI'

    Write-Host ""
    Write-Host "[1/6] 새 GitHub 계정 인증 확인..."

    $activeUser = $null
    try {
        $activeUser = (& gh api user --jq .login 2>$null).Trim()
    } catch {}

    if ($activeUser -ne $ExpectedUser) {
        if ($activeUser) {
            Write-Host "현재 GitHub CLI 계정: $activeUser" -ForegroundColor Yellow
            Write-Host "새 계정 $ExpectedUser 로 다시 인증합니다." -ForegroundColor Yellow
            try { & gh auth logout -h github.com -u $activeUser } catch {}
        } else {
            Write-Host "GitHub CLI 로그인이 필요합니다." -ForegroundColor Yellow
        }

        Write-Host ""
        Write-Host "브라우저가 열리면 GitHub 계정 '$ExpectedUser' 로 로그인/승인하세요." -ForegroundColor Cyan
        & gh auth login --hostname github.com --git-protocol https --web --scopes repo,workflow
        if ($LASTEXITCODE -ne 0) { throw "GitHub CLI 로그인 실패" }

        $activeUser = (& gh api user --jq .login).Trim()
    }

    if ($activeUser -ne $ExpectedUser) {
        throw "연결된 GitHub 계정이 '$ExpectedUser'가 아닙니다. 현재=$activeUser"
    }

    & gh auth setup-git
    if ($LASTEXITCODE -ne 0) { throw "Git 자격증명 연결 실패" }

    Write-Host "인증 확인: $activeUser" -ForegroundColor Green

    Write-Host ""
    Write-Host "[2/6] 저장소 확인..."
    & gh repo view $Repo --json nameWithOwner,visibility,url | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "저장소 $Repo 에 접근할 수 없습니다."
    }

    # Log repo is optional here; report its status but don't block source migration.
    & gh repo view $LogRepo --json nameWithOwner,visibility,url *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "로그 저장소 확인: $LogRepo" -ForegroundColor Green
    } else {
        Write-Host "참고: $LogRepo 는 아직 확인되지 않습니다. 소스 이전은 계속합니다." -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "[3/6] V0.1.76 소스 Git 저장소 준비..."

    if (-not (Test-Path '.git')) {
        & git init
        if ($LASTEXITCODE -ne 0) { throw "git init 실패" }
    }

    & git config user.name $ExpectedUser
    & git config user.email '92665975+skwlrna@users.noreply.github.com'

    # v2 fix: a brand-new local repo has no 'origin'.
    # Do not call 'git remote remove origin' unless it actually exists,
    # because ErrorActionPreference=Stop turns the harmless "No such remote"
    # message into a terminating error on Windows PowerShell 5.1.
    $remoteNames = @(& git remote)
    if ($LASTEXITCODE -ne 0) { throw "기존 Git remote 확인 실패" }

    if ($remoteNames -contains 'origin') {
        & git remote remove origin
        if ($LASTEXITCODE -ne 0) { throw "기존 origin 제거 실패" }
    }

    & git remote add origin "https://github.com/$Repo.git"
    if ($LASTEXITCODE -ne 0) { throw "새 origin 등록 실패" }

    & git fetch origin main
    if ($LASTEXITCODE -ne 0) { throw "원격 main 브랜치 가져오기 실패" }

    & git checkout -B main origin/main
    if ($LASTEXITCODE -ne 0) { throw "main 브랜치 준비 실패" }

    Write-Host ""
    Write-Host "[4/6] 전체 소스/템플릿/Actions 업로드..."

    & git add -A
    if ($LASTEXITCODE -ne 0) { throw "git add 실패" }

    $changes = (& git status --porcelain)
    if ($changes) {
        & git commit -m 'Import Mabi Auto V0.1.76 source and GitHub Actions'
        if ($LASTEXITCODE -ne 0) { throw "git commit 실패" }

        & git push -u origin main
        if ($LASTEXITCODE -ne 0) { throw "GitHub push 실패" }
    } else {
        Write-Host "이미 같은 소스가 올라가 있습니다."
    }

    Write-Host ""
    Write-Host "[5/6] GitHub Actions 등록 확인..."
    Start-Sleep -Seconds 4

    $workflowFound = $false
    for ($i = 0; $i -lt 12; $i++) {
        & gh workflow view release-v0176.yml -R $Repo *> $null
        if ($LASTEXITCODE -eq 0) {
            $workflowFound = $true
            break
        }
        Start-Sleep -Seconds 5
    }

    if (-not $workflowFound) {
        throw "release-v0176.yml 워크플로가 GitHub에 등록되지 않았습니다."
    }

    Write-Host ""
    Write-Host "[6/6] V0.1.76 빌드/Release 실행..."
    & gh workflow run release-v0176.yml -R $Repo
    if ($LASTEXITCODE -ne 0) { throw "Release workflow 실행 요청 실패" }

    $runId = $null
    for ($i = 0; $i -lt 18; $i++) {
        Start-Sleep -Seconds 5
        try {
            $json = & gh run list -R $Repo --workflow release-v0176.yml --limit 1 --json databaseId,status,conclusion,url
            $runs = $json | ConvertFrom-Json
            if ($runs -and $runs.Count -gt 0) {
                $runId = $runs[0].databaseId
                Write-Host "Release run: $($runs[0].url)"
                break
            }
        } catch {}
    }

    if (-not $runId) {
        throw "Release workflow run ID를 찾지 못했습니다. 저장소 Actions 화면에서 확인해 주세요."
    }

    & gh run watch $runId -R $Repo --exit-status
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub Actions Release 빌드가 실패했습니다."
    }

    & gh release view V0.1.76 -R $Repo --json tagName,name,url,publishedAt | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "V0.1.76 Release 확인 실패"
    }

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " 새 GitHub 이전 완료" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "소스 저장소: https://github.com/$Repo"
    Write-Host "Release:     https://github.com/$Repo/releases/tag/V0.1.76"
    Write-Host ""
    Write-Host "V0.1.76을 한 번 설치하면 이후 자동업데이트는 새 계정 저장소를 사용합니다."
    Write-Host "런타임 오류 업로드는 앱의 '에러 전송' 설정에서 새 로그 저장소/PAT를 지정해야 합니다."

    Finish 0 "V0.1.76 GitHub 이전과 Release 생성이 완료되었습니다."
}
catch {
    Write-Host ""
    Write-Host "실패 원인:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Finish 1 "GitHub 이전 작업이 중단되었습니다."
}
