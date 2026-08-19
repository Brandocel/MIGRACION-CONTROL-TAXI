param(
    [string[]]$InputFiles = @(
        (Join-Path $env:USERPROFILE 'Downloads\CONTROL DEJADAS JUNIO 2026.xlsx'),
        (Join-Path $env:USERPROFILE 'Downloads\DEJADAS y comisiones cuadre 08 junio  2026 (2).xlsx'),
        (Join-Path $env:USERPROFILE 'Downloads\CONCENTRADO GENERAL TAXIS, UBER, VANS JUNIO 2026.xlsx')
    ),
    [switch]$Overwrite
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-XmlDocument([string]$text) {
    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $true
    $doc.LoadXml($text)
    return $doc
}

function Get-XmlText([System.Xml.XmlDocument]$doc) {
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.OmitXmlDeclaration = $false
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $false

    $sw = New-Object System.IO.StringWriter
    $xw = [System.Xml.XmlWriter]::Create($sw, $settings)
    try {
        $doc.Save($xw)
        $xw.Flush()
        return $sw.ToString()
    } finally {
        $xw.Dispose()
        $sw.Dispose()
    }
}

function Normalize-SheetXml([string]$xmlText) {
    $doc = Get-XmlDocument $xmlText
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('x', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')

    $cells = $doc.SelectNodes('//x:c[x:f]', $ns)
    foreach ($cell in @($cells)) {
        $valueNode = $cell.SelectSingleNode('x:v', $ns)
        $cached = if ($null -ne $valueNode) { [string]$valueNode.InnerText } else { '0' }
        if ([string]::IsNullOrWhiteSpace($cached) -or $cached.StartsWith('#')) {
            $cached = '0'
        }

        $formulaNodes = @($cell.SelectNodes('x:f', $ns))
        foreach ($formulaNode in $formulaNodes) {
            [void]$cell.RemoveChild($formulaNode)
        }

        if ($cell.HasAttribute('t')) {
            $cell.RemoveAttribute('t')
        }

        if ($null -eq $valueNode) {
            $valueNode = $doc.CreateElement('v', $doc.DocumentElement.NamespaceURI)
            [void]$cell.AppendChild($valueNode)
        }

        $valueNode.InnerText = $cached
    }

    return Get-XmlText $doc
}

function Fix-XlsxFile([string]$inputPath, [switch]$overwrite) {
    if (-not (Test-Path -LiteralPath $inputPath)) {
        Write-Warning "No existe: $inputPath"
        return
    }

    $dir = Split-Path -Parent $inputPath
    $base = [System.IO.Path]::GetFileNameWithoutExtension($inputPath)
    $ext = [System.IO.Path]::GetExtension($inputPath)
    $outputPath = if ($overwrite) { $inputPath } else { Join-Path $dir ($base + '_FIXED' + $ext) }

    $tempPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), [System.IO.Path]::GetRandomFileName() + '.xlsx')

    $source = [System.IO.Compression.ZipFile]::OpenRead($inputPath)
    try {
        $dest = [System.IO.Compression.ZipFile]::Open($tempPath, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in $source.Entries) {
                if ($entry.FullName -eq 'xl/calcChain.xml') {
                    continue
                }

                $newEntry = $dest.CreateEntry($entry.FullName, [System.IO.Compression.CompressionLevel]::Fastest)
                $srcStream = $entry.Open()
                $dstStream = $newEntry.Open()
                try {
                    if ($entry.FullName -like 'xl/worksheets/*.xml') {
                        $reader = New-Object System.IO.StreamReader($srcStream)
                        try {
                            $xmlText = $reader.ReadToEnd()
                            $fixedText = Normalize-SheetXml $xmlText
                            $writer = New-Object System.IO.StreamWriter($dstStream, (New-Object System.Text.UTF8Encoding($false)))
                            try {
                                $writer.Write($fixedText)
                                $writer.Flush()
                            } finally {
                                $writer.Dispose()
                            }
                        } finally {
                            $reader.Dispose()
                        }
                    } else {
                        $srcStream.CopyTo($dstStream)
                    }
                } finally {
                    $dstStream.Dispose()
                    $srcStream.Dispose()
                }
            }
        } finally {
            $dest.Dispose()
        }
    } finally {
        $source.Dispose()
    }

    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Force
    }
    Move-Item -LiteralPath $tempPath -Destination $outputPath
    Write-Host "OK -> $outputPath"
}

foreach ($file in $InputFiles) {
    Fix-XlsxFile -inputPath $file -overwrite:$Overwrite
}
