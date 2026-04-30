# PortableConvertX 一键发布脚本 / One-click publish script
# 用法 / Usage: .\publish.ps1

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptRoot

$DistDir   = Join-Path $ScriptRoot 'dist'
$OutputDir = Join-Path $DistDir 'PortableConvertX'
$OutlistDir = Join-Path $DistDir 'outlist'
$Csproj    = Join-Path $ScriptRoot 'ConvertXPortable\ConvertXPortable.csproj'

# ================================================================
# Step 1: 输入版本号 / Enter version number
# ================================================================
Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  PortableConvertX 一键发布 / One-Click Publish' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

do {
    $Version = Read-Host '请输入版本号 (例如 / e.g. v1.0.0) | Enter version number (e.g. v1.0.0)'
    $Version = $Version.Trim()
    if ([string]::IsNullOrWhiteSpace($Version)) {
        Write-Host '版本号不能为空, 请重新输入.' -ForegroundColor Yellow
        Write-Host 'Version number cannot be empty, please try again.' -ForegroundColor Yellow
    }
} while ([string]::IsNullOrWhiteSpace($Version))

# Sanitize: replace chars that are unsafe in filenames
$SafeVersion = $Version -replace '[\\/:*?"<>|]', '_'
$ZipName     = "PortableConvertX_${SafeVersion}.zip"
$ZipPath     = Join-Path $DistDir $ZipName

Write-Host ''
Write-Host "版本号 / Version : $Version" -ForegroundColor Green
Write-Host ''

# ================================================================
# Step 2: dotnet publish
# ================================================================
Write-Host '[2/6] dotnet publish ...' -ForegroundColor Cyan
Write-Host '  dotnet publish .\ConvertXPortable\ConvertXPortable.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\dist\PortableConvertX'
Write-Host ''

$publishArgs = @(
    'publish', $Csproj,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-o', $OutputDir
)

$publishResult = & dotnet $publishArgs 2>&1
$exitCode = $LASTEXITCODE

if ($publishResult) {
    $publishResult | ForEach-Object { Write-Host $_ }
}

if ($exitCode -ne 0) {
    Write-Host ''
    Write-Host '[FAIL] dotnet publish 失败 / failed, 退出码 / exit code: ' -ForegroundColor Red -NoNewline
    Write-Host $exitCode -ForegroundColor Red
    exit $exitCode
}

Write-Host ''
Write-Host '[OK] dotnet publish 完成 / completed' -ForegroundColor Green
Write-Host ''

# ================================================================
# Step 3: 复制 conversions.json 和 TestTools / Copy extras
# ================================================================
Write-Host '[3/6] 复制额外文件 / Copying extra files ...' -ForegroundColor Cyan

$SrcConversions = Join-Path $ScriptRoot 'conversions.json'
$SrcTestTools   = Join-Path $ScriptRoot 'TestTools'
$DstConversions = Join-Path $OutputDir 'conversions.json'
$DstTestTools   = Join-Path $OutputDir 'TestTools'

Copy-Item -Path $SrcConversions -Destination $DstConversions -Force
Write-Host "  conversions.json -> $DstConversions" -ForegroundColor Gray

if (Test-Path $DstTestTools) {
    Write-Host '  TestTools\ 已存在, 跳过复制 / already exists, skipped' -ForegroundColor Gray
}
else {
    Copy-Item -Path $SrcTestTools -Destination $DstTestTools -Recurse -Force
    Write-Host "  TestTools\ -> $DstTestTools" -ForegroundColor Gray
}

Write-Host '[OK] 额外文件复制完成 / Extra files copied' -ForegroundColor Green
Write-Host ''

# ================================================================
# Step 4: 创建 zip 压缩包 / Create zip archive
# ================================================================
Write-Host '[4/6] 创建 zip / Creating zip archive ...' -ForegroundColor Cyan
Write-Host "  $ZipPath" -ForegroundColor Gray

# Remove old zip if exists
if (Test-Path $ZipPath) {
    Remove-Item -Path $ZipPath -Force
}

# 7z a -tzip: archive contents of OutputDir, NOT the parent folder itself
Push-Location $OutputDir
try {
    $zipResult = & 7z a -tzip -mx=9 $ZipPath '.' 2>&1
    if ($zipResult) {
        $zipResult | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $ZipPath)) {
    Write-Host '[FAIL] zip 创建失败 / zip creation failed' -ForegroundColor Red
    exit 1
}

$zipSize = (Get-Item $ZipPath).Length
$zipSizeMiB = [math]::Round($zipSize / 1MB, 2)
Write-Host ''
Write-Host "[OK] zip 创建完成 / created : $ZipPath" -ForegroundColor Green
Write-Host "  大小 / Size : $zipSize bytes ($zipSizeMiB MiB)" -ForegroundColor Gray
Write-Host ''

# ================================================================
# Step 5: 7zip 分卷压缩 / Split into volumes
# ================================================================
Write-Host '[5/6] 7zip 分卷 / Splitting into 200 MB volumes ...' -ForegroundColor Cyan
Write-Host "  输出目录 / Output dir : $OutlistDir" -ForegroundColor Gray

# Ensure outlist directory exists, remove old vol files
if (Test-Path $OutlistDir) {
    Remove-Item -Path "$OutlistDir\PortableConvertX.7z.*" -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "$OutlistDir\sha256.txt" -Force -ErrorAction SilentlyContinue
}
else {
    New-Item -ItemType Directory -Path $OutlistDir -Force | Out-Null
}

$SplitBase = Join-Path $OutlistDir 'PortableConvertX.7z'

$splitResult = & 7z a -v200m -mx0 $SplitBase $ZipPath 2>&1
if ($splitResult) {
    $splitResult | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
}

if ($LASTEXITCODE -ne 0) {
    Write-Host '[FAIL] 7zip 分卷失败 / split failed' -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[OK] 分卷完成 / Split complete" -ForegroundColor Green
Write-Host ''

# ================================================================
# Step 6: 生成 sha256.txt / Generate sha256.txt
# ================================================================
Write-Host '[6/6] 生成 SHA256 校验文件 / Generating SHA256 checksums ...' -ForegroundColor Cyan

$VolumeFiles = Get-ChildItem -Path $OutlistDir -Filter 'PortableConvertX.7z.*' |
    Sort-Object Name |
    ForEach-Object { $_.FullName }

$VolumeCount = $VolumeFiles.Count
$ZipHash    = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLower()
$ZipSizeStr = "{0:N0} bytes" -f $zipSize

# Build sha256.txt lines
$lines = @()
$lines += 'SHA256 checksums for PortableConvertX split volumes'
$lines += "Generated: $(Get-Date -Format 'yyyy-MM-dd')"
$lines += ('=' * 72)
$lines += ''
$lines += 'Original file:'
$lines += "  $ZipName"
$lines += "  SHA256: $ZipHash"
$lines += "  Size:   $ZipSizeStr ($zipSizeMiB MiB)"
$lines += ''
$lines += ('=' * 72)
$lines += ''
$lines += "Split volumes (200 MB each, $VolumeCount volumes total):"

# Pass 1: collect metadata and compute total sizes
$totalVolSize = 0L
$volInfo = foreach ($vol in $VolumeFiles) {
    $item = Get-Item $vol
    $name = $item.Name
    $size = $item.Length
    $hash = (Get-FileHash -Path $vol -Algorithm SHA256).Hash.ToLower()
    $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($name)
    $lines += "  $name  SHA256: $hash  Size: {0:N0} bytes" -f $size
    [PSCustomObject]@{
        Path      = $vol
        Name      = $name
        Size      = $size
        Hash      = $hash
        NameBytes = $nameBytes
    }
    $totalVolSize += $size
}

$lines += ''
$lines += ('=' * 72)
$lines += ''

# Allocate buffers once
$allBytesCombined    = [byte[]]::new($totalVolSize)
$totalNamedSize      = ($volInfo | ForEach-Object { $_.NameBytes.Length + $_.Size } | Measure-Object -Sum).Sum
$allNamedBytesCombined = [byte[]]::new($totalNamedSize)

# Pass 2: stream each file into buffers
$offsetData   = 0
$offsetNamed = 0
foreach ($v in $volInfo) {
    $fs = $null
    try {
        $fs = [System.IO.File]::OpenRead($v.Path)
        $bytes = [byte[]]::new($v.Size)
        $fs.Read($bytes, 0, $bytes.Length) | Out-Null

        [System.Buffer]::BlockCopy($bytes, 0, $allBytesCombined, $offsetData, $bytes.Length)
        $offsetData += $bytes.Length

        [System.Buffer]::BlockCopy($v.NameBytes, 0, $allNamedBytesCombined, $offsetNamed, $v.NameBytes.Length)
        $offsetNamed += $v.NameBytes.Length
        [System.Buffer]::BlockCopy($bytes, 0, $allNamedBytesCombined, $offsetNamed, $bytes.Length)
        $offsetNamed += $bytes.Length
    }
    finally {
        if ($fs) { $fs.Dispose() }
    }
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
$combinedHash      = [BitConverter]::ToString($sha256.ComputeHash($allBytesCombined)).Replace('-', '').ToLower()
$combinedNamedHash = [BitConverter]::ToString($sha256.ComputeHash($allNamedBytesCombined)).Replace('-', '').ToLower()
$sha256.Dispose()

$totalVolSizeStr = "{0:N0} bytes" -f $totalVolSize
$totalVolSizeMiB = [math]::Round($totalVolSize / 1MB, 2)

$lines += "Combined hash for all $VolumeCount volumes:"
$lines += "  SHA256 (data):       $combinedHash"
$lines += "  SHA256 (data+names): $combinedNamedHash"
$lines += "  Total size:          $totalVolSizeStr ($totalVolSizeMiB MiB)"
$lines += ''
$lines += ('=' * 72)
$lines += ''
$lines += "Verification (7z t PortableConvertX.7z.001):"
$lines += '  7z runs integrity check across all volumes automatically.'
$lines += '  Run: 7z t PortableConvertX.7z.001'

$sha256Path = Join-Path $OutlistDir 'sha256.txt'
$lines -join "`r`n" | Out-File -FilePath $sha256Path -Encoding utf8

Write-Host "[OK] SHA256 文件已生成 / sha256.txt written" -ForegroundColor Green
Write-Host ''

# ================================================================
# 完成摘要 / Summary
# ================================================================
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  发布完成! / Publish Complete!' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host "  版本 / Version    : $Version" -ForegroundColor White
Write-Host "  Zip               : $ZipName" -ForegroundColor White
Write-Host "  大小 / Size       : $ZipSizeStr ($zipSizeMiB MiB)" -ForegroundColor White
Write-Host "  SHA256 (zip)      : $ZipHash" -ForegroundColor White
Write-Host ''
Write-Host "  分卷 / Volumes ($VolumeCount files):" -ForegroundColor White
Get-ChildItem -Path $OutlistDir -Filter 'PortableConvertX.7z.*' |
    Sort-Object Name |
    ForEach-Object {
        $sz = "{0:N0}" -f $_.Length
        Write-Host "    $($_.Name)  ($sz bytes)" -ForegroundColor Gray
    }
Write-Host ''
Write-Host "  校验文件 / Checksum : $sha256Path" -ForegroundColor White
Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
