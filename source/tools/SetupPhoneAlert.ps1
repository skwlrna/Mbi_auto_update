$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cfg = Join-Path $root 'FishingAutomation\notification.json'
$releaseCfg = Join-Path $root 'release\notification.json'

Clear-Host
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host ' Mabinogi Macro - Telegram Phone Alert' -ForegroundColor Cyan
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Paste your NEW Telegram Bot Token.'
$token = (Read-Host 'Bot Token').Trim()
Write-Host ''
Write-Host 'Paste your Telegram Chat ID.'
$chat = (Read-Host 'Chat ID').Trim()

if ([string]::IsNullOrWhiteSpace($token) -or $token -notmatch '^\d+:[A-Za-z0-9_-]+$') {
    Write-Host ''
    Write-Host 'ERROR: Bot Token format is not valid.' -ForegroundColor Red
    Write-Host 'Example format: 1234567890:AAAbbbCCC_ddd-EEEfff123'
    exit 1
}
if ([string]::IsNullOrWhiteSpace($chat) -or $chat -notmatch '^-?\d+$') {
    Write-Host ''
    Write-Host 'ERROR: Chat ID must contain numbers only (a leading - is allowed).' -ForegroundColor Red
    exit 1
}

$parent = Split-Path -Parent $cfg
if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }

$o = [ordered]@{
    Enabled = $true
    BotToken = $token
    ChatId = $chat
    FishingStallSeconds = 60
    WatchdogSeconds = 90
    ExitProcedureTimeoutSeconds = 90
    NetworkRetryAlertCount = 3
    SendScreenshot = $true
    RemoteControlEnabled = $true
}
$json = $o | ConvertTo-Json
$json | Set-Content -LiteralPath $cfg -Encoding UTF8
if (Test-Path (Split-Path -Parent $releaseCfg)) {
    $json | Set-Content -LiteralPath $releaseCfg -Encoding UTF8
}

Write-Host ''
Write-Host ('Saved settings: ' + $cfg) -ForegroundColor Green
if (Test-Path $releaseCfg) { Write-Host ('Runtime settings: ' + $releaseCfg) -ForegroundColor Green }
Write-Host 'Sending a test message to Telegram...'
try {
    $uri = 'https://api.telegram.org/bot' + $token + '/sendMessage'
    $body = @{ chat_id = $chat; text = '[Mabinogi Macro] Phone alert test OK' }
    $r = Invoke-RestMethod -Method Post -Uri $uri -Body $body -TimeoutSec 15
    if ($r.ok) {
        Write-Host ''
        Write-Host 'SUCCESS: Check your phone. The Telegram test message was sent.' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host 'FAILED: Telegram returned an error.' -ForegroundColor Red
    }
} catch {
    Write-Host ''
    Write-Host 'FAILED: Could not send the Telegram test message.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'The settings were still saved. Check Bot Token, Chat ID, and internet connection.'
}
