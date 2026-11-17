$root = $PSScriptRoot
$buildDir = Join-Path $root 'build'
$dataDir = Join-Path $root 'data'
$src = Join-Path $root 'src'
$exe = Join-Path $buildDir 'DauntlessCompanion.exe'
$newExe = Join-Path $buildDir 'DauntlessCompanion.new.exe'
$oldExe = Join-Path $buildDir 'DauntlessCompanion.old.exe'
$stamp = Join-Path $buildDir 'DauntlessCompanion.build'
$legacy = 'BehemothHealthBarsOverlay'
$icon = Join-Path (Join-Path $root 'icons') 'thrax.ico'
$log = Join-Path $dataDir 'healthbar-build.log'

foreach ($d in @($buildDir, $dataDir)) {
    if (-not (Test-Path -LiteralPath $d)) { try { [void](New-Item -ItemType Directory -Path $d -Force -ErrorAction Stop) } catch { } }
}

function Write-Log([string]$m) {
    try { [System.IO.File]::AppendAllText($log, (Get-Date).ToString('HH:mm:ss') + '  ' + $m + "`r`n") } catch { }
}

if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue }

function Stop-Overlay {
    foreach ($p in @(Get-Process -Name 'DauntlessCompanion', $legacy -ErrorAction SilentlyContinue)) {
        try { $p.Kill(); [void]$p.WaitForExit(2000) } catch { }
    }
}

try {
    $me = Get-CimInstance Win32_Process -Filter "ProcessId = $PID" -ErrorAction Stop
    Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe' OR Name = 'pwsh.exe'" -ErrorAction Stop |
        Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*healthbar-overlay.ps1*' -and $_.CreationDate -lt $me.CreationDate } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch { } }
} catch { }

$legacyFiles = @(foreach ($d in @($root, $buildDir)) { foreach ($n in @('.exe', '.build', '.new.exe', '.old.exe')) { Join-Path $d ($legacy + $n) } })
$legacyFound = @($legacyFiles | Where-Object { Test-Path -LiteralPath $_ })
if ($legacyFound.Count -gt 0) {
    Stop-Overlay
    foreach ($f in $legacyFound) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue }
}

if (-not (Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath $oldExe)) {
    try { [System.IO.File]::Move($oldExe, $exe); Write-Log 'Restored DauntlessCompanion.exe from the backup left by a failed update' } catch { }
}

$files = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { $_.FullName })
if ($files.Count -eq 0) {
    Write-Log 'src\*.cs is missing; overlay cannot start'
    exit 1
}

$hash = ''
try {
    $parts = foreach ($f in $files) { [System.IO.Path]::GetFileName($f) + ':' + (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = [System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($parts -join '|'))).Replace('-', '')
} catch { $hash = '' }
if ($hash -and (Test-Path -LiteralPath $icon)) {
    try { $hash += '-' + (Get-FileHash -LiteralPath $icon -Algorithm SHA256).Hash.Substring(0, 16) } catch { }
}
$build = -not (Test-Path -LiteralPath $exe)
if (-not $build) {
    $built = ''
    try { if (Test-Path -LiteralPath $stamp) { $built = ([System.IO.File]::ReadAllText($stamp)).Trim() } } catch { }
    $build = (-not $hash) -or ($built -ne $hash)
}

if ($build) {
    $ok = $false
    try {
        if (Test-Path -LiteralPath $newExe) { Remove-Item -LiteralPath $newExe -Force -ErrorAction SilentlyContinue }
        $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
        $cp = New-Object System.CodeDom.Compiler.CompilerParameters
        foreach ($ref in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')) { [void]$cp.ReferencedAssemblies.Add($ref) }
        $cp.GenerateExecutable = $true
        $cp.GenerateInMemory = $false
        $cp.OutputAssembly = $newExe
        $cp.CompilerOptions = '/target:winexe /optimize+'
        if (Test-Path -LiteralPath $icon) { $cp.CompilerOptions += ' /win32icon:"' + $icon + '"' }
        $result = $provider.CompileAssemblyFromFile($cp, [string[]]$files)
        $errors = @($result.Errors | Where-Object { -not $_.IsWarning })
        if ($errors.Count -gt 0) {
            Write-Log 'Couldn''t build the overlay from src:'
            foreach ($e in $errors) { Write-Log ('  ' + [System.IO.Path]::GetFileName($e.FileName) + ' line ' + $e.Line + ': ' + $e.ErrorNumber + ' ' + $e.ErrorText) }
        } elseif (-not (Test-Path -LiteralPath $newExe)) {
            Write-Log 'Couldn''t build the overlay: compiler failed to create file'
        } else {
            $ok = $true
        }
    } catch {
        Write-Log ('Couldn''t build overlay: ' + $_.Exception.Message)
    }

    if ($ok) {
        Stop-Overlay
        $moved = $false
        for ($i = 0; $i -lt 10 -and -not $moved; $i++) {
            try {
                if (Test-Path -LiteralPath $oldExe) {
                    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $oldExe -Force -ErrorAction Stop }
                    else { [System.IO.File]::Move($oldExe, $exe) }
                }
                if (Test-Path -LiteralPath $exe) { [System.IO.File]::Move($exe, $oldExe) }
                try {
                    [System.IO.File]::Move($newExe, $exe)
                    $moved = $true
                } catch {
                    if ((Test-Path -LiteralPath $oldExe) -and -not (Test-Path -LiteralPath $exe)) { [System.IO.File]::Move($oldExe, $exe) }
                    throw
                }
            } catch { Start-Sleep -Milliseconds 200 }
        }
        if ($moved) {
            try { [System.IO.File]::WriteAllText($stamp, $hash) } catch { }
            if (Test-Path -LiteralPath $oldExe) { Remove-Item -LiteralPath $oldExe -Force -ErrorAction SilentlyContinue }
        } else {
            if ((Test-Path -LiteralPath $oldExe) -and -not (Test-Path -LiteralPath $exe)) {
                try { [System.IO.File]::Move($oldExe, $exe) } catch { }
            }
            Write-Log 'Couldn''t replace the existing overlay executable. The previous version has been kept.'
        }
    }
    if (Test-Path -LiteralPath $newExe) { Remove-Item -LiteralPath $newExe -Force -ErrorAction SilentlyContinue }
    if (-not $ok) {
        if (Test-Path -LiteralPath $exe) { Write-Log 'Retaining previous overlay until src re-builds' }
        else { exit 1 }
    }
}

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Log 'Couldn''t find DauntlessCompanion.exe, overlay cannot start'
    exit 1
}

$pass = @($args | ForEach-Object { $a = [string]$_; if ($a -match '\s') { '"' + $a + '"' } else { $a } })
if ($pass.Count -gt 0) { Start-Process -FilePath $exe -ArgumentList $pass -WorkingDirectory $root }
else { Start-Process -FilePath $exe -WorkingDirectory $root }
