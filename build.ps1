<#
  Builds LightGreen.dll without the .NET SDK, using the C# compiler that ships with PowerShell 7, and optionally
  copies it into a BepInEx plugins folder and packs the Thunderstore package.

  Requirements: PowerShell 7 (pwsh), the game, and BepInEx 5's core folder (BepInEx.dll + 0Harmony.dll).

  LightGreen has no shader bundle, by design: every effect uses the game's own shaders and URP's built-in effects.

  Examples:
    pwsh ./build.ps1
    pwsh ./build.ps1 -BepInExCore "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\core" `
                     -InstallTo   "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\plugins"
    pwsh ./build.ps1 -BepInExCore "<...>\BepInEx\core" -Package
      (writes build/lmkHenlo-LightGreen-<version>.zip: manifest.json, README.md, icon.png, CHANGELOG.md, LightGreen.dll, LICENSE)
    pwsh ./build.ps1 -BepInExCore "<...>\BepInEx\core" -Test
      (also runs the offline checks in tests/; the build fails if one fails)
#>
param(
    # Kept for the familiar command line: plugin is the only project.
    [string[]]$Project = @('plugin'),
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Super Battle Golf",
    [string]$ManagedDir = "",
    [string]$BepInExCore = "",
    [string]$OutDir = (Join-Path $PSScriptRoot "build/plugins"),
    [string]$InstallTo = "",
    [switch]$Package,
    [switch]$Test
)
$ErrorActionPreference = 'Stop'

$Project = @($Project | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ } | ForEach-Object { $_.ToLowerInvariant() })
if ($Project -contains 'bundles') { throw "LightGreen has no shader bundle (every effect uses the game's own shaders). Use -Project plugin." }
$unknown = @($Project | Where-Object { $_ -ne 'plugin' })
if ($unknown.Count -gt 0) { throw "-Project takes plugin (got: $($unknown -join ', '))." }

# ---- Plugin: Roslyn ------------------------------------------------------------------------------
if (-not $ManagedDir) { $ManagedDir = Join-Path $GameDir "Super Battle Golf_Data/Managed" }
if (-not $BepInExCore) { $BepInExCore = Join-Path $GameDir "BepInEx/core" }
if (-not (Test-Path (Join-Path $ManagedDir "GameAssembly.dll"))) { throw "GameAssembly.dll not found in '$ManagedDir'. Pass -GameDir or -ManagedDir." }
if (-not (Test-Path (Join-Path $BepInExCore "BepInEx.dll"))) { throw "BepInEx.dll not found in '$BepInExCore'. Pass -BepInExCore (e.g. your r2modman profile's BepInEx/core folder)." }

$pwshDir = Split-Path ([System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName)
$roslyn = Join-Path $pwshDir 'Microsoft.CodeAnalysis.CSharp.dll'
if (-not (Test-Path $roslyn)) { throw "Run this with PowerShell 7 (pwsh), which includes the C# compiler." }
Add-Type -Path (Join-Path $pwshDir 'Microsoft.CodeAnalysis.dll')
Add-Type -Path $roslyn

# The game's own assemblies (its Mono class library included) + BepInEx/Harmony.
$refFiles = @(Get-ChildItem $ManagedDir -Filter *.dll | ForEach-Object FullName) +
            @((Join-Path $BepInExCore 'BepInEx.dll'), (Join-Path $BepInExCore '0Harmony.dll'))
$refs = [Microsoft.CodeAnalysis.MetadataReference[]]@($refFiles | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_) })

$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp9)
$opts = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$opts = $opts.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).
              WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Disable).
              WithDeterministic($true)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$name = 'LightGreen'
$files = @(Get-ChildItem -Recurse -Filter *.cs (Join-Path $PSScriptRoot 'plugin/src') | ForEach-Object FullName)
$trees = [Microsoft.CodeAnalysis.SyntaxTree[]]@($files | ForEach-Object {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_), $parse, $_) })
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($name, $trees, $refs, $opts)

$out = Join-Path $OutDir "$name.dll"
$fs = [IO.File]::Create($out)
try { $res = $comp.Emit($fs) } finally { $fs.Dispose() }

# CS1701: BepInEx/Harmony reference mscorlib 2.0, the game ships 4.0. Normal for BepInEx 5 mods.
$diags = @($res.Diagnostics | Where-Object { $_.Severity -ge [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning -and $_.Id -ne 'CS1701' })
$diags | ForEach-Object { Write-Host $_.ToString() }
if (-not $res.Success) { Remove-Item $out -ErrorAction SilentlyContinue; throw "$name failed to build." }
Write-Host "Built $out"

# ---- Offline checks ------------------------------------------------------------------------------
# The parts that run without the game, compiled with tests/*.cs against this PowerShell's own .NET and the game's
# UnityEngine.CoreModule (only its plain structs and maths are used), then run here.
if ($Test) {
    $tested = @('Core/CubeLut.cs', 'Core/LutBuilder.cs', 'Core/Json.cs', 'Core/HudMath.cs', 'Core/FlakeArt.cs', 'Core/RainArt.cs',
                'Core/LocalRandom.cs', 'Core/MaterialEdits.cs', 'Core/SafeFile.cs', 'Core/CoreLog.cs', 'Core/TextureArt.cs',
                'Core/Biome.cs', 'Presets/Presets.cs', 'Presets/Filters.cs', 'Presets/FilterStore.cs', 'Presets/JsonFile.cs',
                'Presets/PresetStore.cs') |
              ForEach-Object { Join-Path $PSScriptRoot "plugin/src/$_" }
    $testFiles = @(Get-ChildItem -Filter *.cs (Join-Path $PSScriptRoot 'tests') | ForEach-Object FullName) + $tested
    $testTrees = [Microsoft.CodeAnalysis.SyntaxTree[]]@($testFiles | ForEach-Object {
        [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_), $parse, $_) })
    $unity = Join-Path $ManagedDir 'UnityEngine.CoreModule.dll'
    $bepinex = Join-Path $BepInExCore 'BepInEx.dll'
    # .NET's own class library only: PowerShell ships its own libraries beside it (a "Json" namespace among them),
    # which would clash with LightGreen's names.
    $platform = @(([AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES')) -split [IO.Path]::PathSeparator |
                  Where-Object { $_ -and (Split-Path -Leaf $_) -match '^(System\..+|System|mscorlib|netstandard)\.dll$' })
    $testRefs = [Microsoft.CodeAnalysis.MetadataReference[]]@(($platform + @($unity, $bepinex)) | ForEach-Object {
        [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_) })
    $testDir = Join-Path ([IO.Path]::GetTempPath()) ("LightGreen-tests-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testDir | Out-Null
    try {
        Copy-Item $unity, $bepinex $testDir
        $testDll = Join-Path $testDir 'LightGreen.Tests.dll'
        $testComp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('LightGreen.Tests', $testTrees, $testRefs, $opts)
        $fs = [IO.File]::Create($testDll)
        try { $testRes = $testComp.Emit($fs) } finally { $fs.Dispose() }
        # CS1701/CS1702: UnityEngine and BepInEx reference the older mscorlib. Normal here.
        $testRes.Diagnostics | Where-Object { $_.Severity -ge [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning -and $_.Id -notin 'CS1701', 'CS1702' } |
            ForEach-Object { Write-Host $_.ToString() }
        if (-not $testRes.Success) { throw "The offline checks failed to build." }
        # Run in a PowerShell of their own, so each run loads a fresh copy and nothing stays loaded (or locked) in this one.
        # LoadFrom finds UnityEngine.CoreModule and BepInEx beside the checks. The exit code is the number that failed.
        $run = "exit [Reflection.Assembly]::LoadFrom('$testDll').GetType('LightGreen.Tests.Tests').GetMethod('Run').Invoke(`$null, @())"
        & (Get-Process -Id $PID).Path -NoProfile -NonInteractive -Command $run
        if ($LASTEXITCODE -ne 0) { throw "$LASTEXITCODE offline check(s) failed." }
    }
    finally {
        Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($InstallTo) {
    $dest = Join-Path $InstallTo $name
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $out $dest -Force
    Write-Host "Installed $name to $dest"
}

# ---- Thunderstore package ------------------------------------------------------------------------
if ($Package) {
    $store = Join-Path $PSScriptRoot 'thunderstore'
    $manifest = Get-Content (Join-Path $store 'manifest.json') -Raw | ConvertFrom-Json
    $version = $manifest.version_number
    # The manifest, the plugin and the assembly must all say the same version.
    $pluginVersion = [regex]::Match((Get-Content (Join-Path $PSScriptRoot 'plugin/src/Plugin.cs') -Raw), 'Version = "([^"]+)"').Groups[1].Value
    if ($pluginVersion -ne $version) { throw "Version mismatch: thunderstore/manifest.json says $version, Plugin.cs says $pluginVersion." }

    $staging = Join-Path ([IO.Path]::GetTempPath()) "LightGreen-package-$version"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item (Join-Path $store 'manifest.json'), (Join-Path $store 'README.md'), (Join-Path $store 'icon.png') $staging
    Copy-Item (Join-Path $PSScriptRoot 'CHANGELOG.md') $staging
    Copy-Item $out $staging
    Copy-Item (Join-Path $PSScriptRoot 'LICENSE') $staging

    $zip = Join-Path (Split-Path $OutDir) "$($manifest.author)-$($manifest.name)-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
    Remove-Item $staging -Recurse -Force
    Write-Host "Packed $zip"
}
