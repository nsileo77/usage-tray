$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:winexe "/out:$PSScriptRoot\..\UsageTray.exe" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" "$PSScriptRoot\ReviewTests.cs" "$PSScriptRoot\UsageLayout.cs" "$PSScriptRoot\UsageTray.cs" "$PSScriptRoot\TaskbarStrip.cs" "$PSScriptRoot\TopmostRepair.cs" "$PSScriptRoot\SmoothText.cs" "$PSScriptRoot\TaskbarSurface.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }


