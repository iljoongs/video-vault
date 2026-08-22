<#
.SYNOPSIS
    빌드 결과물(exe/dll)에 로컬 개발용 자체 서명 인증서로 서명한다.
    이 PC에서 Smart App Control(SAC)/WDAC이 서명되지 않은 새 빌드를 차단하는 문제를 우회하기 위한 것.
    인증서가 없는 PC(다른 개발자 PC 등)에서는 조용히 건너뛴다.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetDir,

    [Parameter(Mandatory = $true)]
    [string]$TargetName
)

$thumbprint = "0FA07E232C4B7401C433472ABAC8B9BE2941225F"
$cert = Get-Item "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue

if (-not $cert) {
    Write-Host "VideoVault: 개발용 서명 인증서(썸프린트 $thumbprint)를 찾을 수 없어 서명을 건너뜁니다."
    Write-Host "  (CLAUDE.md '코드 서명' 절을 참고해 이 PC에 인증서를 생성하면 자동으로 서명됩니다.)"
    exit 0
}

$exePath = Join-Path $TargetDir "$TargetName.exe"
$dllPath = Join-Path $TargetDir "$TargetName.dll"

foreach ($path in @($exePath, $dllPath)) {
    if (Test-Path $path) {
        $result = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256
        Write-Host "VideoVault: $([System.IO.Path]::GetFileName($path)) 서명 -> $($result.Status)"
    }
}
