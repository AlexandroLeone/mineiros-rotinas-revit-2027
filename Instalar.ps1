param([string]$AddinsFolder = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2027'))
$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$source = Join-Path $packageRoot 'dist\MineirosRotinas.dll'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Compile the dist package first.' }
$addinRoot = [IO.Path]::GetFullPath($AddinsFolder)
$moduleRoot = Join-Path $addinRoot 'MineirosRotinas'
$manifestPath = Join-Path $addinRoot 'MineirosRotinas.addin'
$addinId = '8ECCDA40-579A-4DFA-9F2A-257654A59D2F'
if (Test-Path -LiteralPath $manifestPath) {
    [xml]$existingManifest = Get-Content -LiteralPath $manifestPath -Raw
    if ($existingManifest.RevitAddIns.AddIn.AddInId -ne $addinId) { throw 'Manifest name is owned by another add-in. Nothing changed.' }
}
function Get-OtherPluginHashes {
    $hashes = @{}
    if (Test-Path -LiteralPath $addinRoot) {
        Get-ChildItem -LiteralPath $addinRoot -Recurse -File | Where-Object {
            ($_.Extension -in @('.addin','.dll') -or $_.Name.EndsWith('.deps.json')) -and
            $_.FullName -ne $manifestPath -and -not $_.FullName.StartsWith($moduleRoot + '\',[StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object { $hashes[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    return $hashes
}
$before = Get-OtherPluginHashes
$dllHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($source).Version
$packageVersion = '{0}.{1}.{2}' -f $assemblyVersion.Major,$assemblyVersion.Minor,$assemblyVersion.Build
$versionFolder = Join-Path $moduleRoot ($packageVersion + '-' + $dllHash.Substring(0,12))
New-Item -ItemType Directory -Path $versionFolder -Force | Out-Null
foreach ($name in @('MineirosRotinas.dll','MineirosRotinas.deps.json','MineirosRotinas.pdb')) {
    $from = Join-Path $packageRoot ('dist\'+$name)
    if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { continue }
    $to = Join-Path $versionFolder $name
    if (Test-Path -LiteralPath $to) {
        if ((Get-FileHash -LiteralPath $from).Hash -ne (Get-FileHash -LiteralPath $to).Hash) { throw ('Version folder collision: '+$to) }
    } else { [IO.File]::Copy($from,$to,$false) }
}
$doc = New-Object System.Xml.XmlDocument
$root = $doc.CreateElement('RevitAddIns'); [void]$doc.AppendChild($root)
$entry = $doc.CreateElement('AddIn'); $entry.SetAttribute('Type','Application'); [void]$root.AppendChild($entry)
$fields = [ordered]@{
    Name='BIM Rotinas Mineiros'; Assembly=(Join-Path $versionFolder 'MineirosRotinas.dll');
    AddInId=$addinId; FullClassName='MineirosRotinas.App'; VendorId='DGMX';
    VendorDescription='Rotinas independentes de documentacao'; AllowLoadingIntoExistingSession='Yes'
}
foreach($field in $fields.GetEnumerator()) { $element=$doc.CreateElement($field.Key);$element.InnerText=$field.Value;[void]$entry.AppendChild($element) }
if (Test-Path -LiteralPath $manifestPath) {
    $backup=Join-Path $moduleRoot ('manifest-backup-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'.xml')
    [IO.File]::Copy($manifestPath,$backup,$false)
}
$doc.Save($manifestPath)
$after = Get-OtherPluginHashes
$changed = @($before.Keys | Where-Object { -not $after.ContainsKey($_) -or $before[$_] -ne $after[$_] })
if($changed.Count -gt 0) { throw ('Other plugin files changed during installation: '+($changed -join ', ')) }
$record=[ordered]@{ InstalledAt=[DateTimeOffset]::Now.ToString('O'); Manifest=$manifestPath; Assembly=(Join-Path $versionFolder 'MineirosRotinas.dll'); SHA256=$dllHash; OtherFilesVerified=$before.Count; OtherPluginsUnchanged=$true; PreservedFiles=$before }
$recordPath=Join-Path $packageRoot ('Instalacao-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'.json')
[IO.File]::WriteAllText($recordPath,($record | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Manifest=$manifestPath;Assembly=$record.Assembly;OtherFilesVerified=$before.Count;OtherPluginsUnchanged=$true;Record=$recordPath}
