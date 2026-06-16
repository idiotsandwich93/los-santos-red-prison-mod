# Local build for Los Santos RED (prison-mod integration work)
# Compiles against reference DLLs copied into .\libs and the net48 reference
# assemblies in .\.buildtools (no system-wide .NET 4.8 Developer Pack needed).
$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$proj    = Join-Path $root "Los Santos RED\Los Santos RED.csproj"
$out     = Join-Path $root "build_out\"
$fpo     = Join-Path $root ".buildtools\net48refs\build\.NETFramework\v4.8"
$log     = Join-Path $root "baseline_build.log"

& $msbuild $proj /nologo /m /v:minimal /p:Configuration=Release /p:Platform=AnyCPU `
    "/p:OutputPath=$out" "/p:FrameworkPathOverride=$fpo" | Out-File -FilePath $log -Encoding utf8
$code = $LASTEXITCODE
Write-Output ("MSBuild exit code: " + $code)
if ($code -ne 0) {
    Write-Output "===== ERRORS ====="
    Select-String -Path $log -Pattern ': error ' | Select-Object -ExpandProperty Line -Unique
} else {
    Write-Output ("OK -> " + (Join-Path $out 'Los Santos RED.dll'))
}
exit $code