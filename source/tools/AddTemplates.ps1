Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path -Parent $PSScriptRoot
$templateDir = Join-Path $root "FishingAutomation\templates"
$releaseTemplateDir = Join-Path $root "release\templates"
New-Item -ItemType Directory -Force -Path $templateDir | Out-Null

function Pick-And-Copy([string]$title, [string]$targetName) {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Title = $title
    $dlg.Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $dst = Join-Path $templateDir $targetName
        Copy-Item -LiteralPath $dlg.FileName -Destination $dst -Force
        if (Test-Path (Join-Path $root "release")) {
            New-Item -ItemType Directory -Force -Path $releaseTemplateDir | Out-Null
            Copy-Item -LiteralPath $dst -Destination (Join-Path $releaseTemplateDir $targetName) -Force
        }
        return $true
    }
    return $false
}

$h = Pick-And-Copy "Select HEALTH BAR template image" "healthbar.png"
$c = Pick-And-Copy "Select COMPASS template image" "compass.png"

if ($h -and $c) {
    [System.Windows.Forms.MessageBox]::Show("healthbar.png and compass.png were added.", "FishingAutomation") | Out-Null
} else {
    [System.Windows.Forms.MessageBox]::Show("One or more optional templates were not selected. You can run this tool again later.", "FishingAutomation") | Out-Null
}
