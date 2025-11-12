param(
    [string]$CsvPath = "vocabularies/french_vocabularies_balticsea.csv",
    [string]$Connection = "Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel"
)

function Parse-ConnectionString([string]$conn) {
    $map = @{}
    foreach ($segment in $conn.Split(";", [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $kv = $segment.Split("=", 2)
        if ($kv.Length -eq 2) {
            $map[$kv[0].Trim()] = $kv[1].Trim()
        }
    }
    return $map
}

if (-not (Test-Path $CsvPath)) {
    throw "CSV file '$CsvPath' not found."
}

function Convert-ToUtf8TempFile([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)

    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $content = $null

    try {
        $content = $utf8.GetString($bytes)
    }
    catch [System.Text.DecoderFallbackException] {
        $latin = [System.Text.Encoding]::GetEncoding("windows-1252")
        $content = $latin.GetString($bytes)
    }

    $tempFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tempFile, $content, (New-Object System.Text.UTF8Encoding($false)))
    return $tempFile
}

$parts = Parse-ConnectionString $Connection

$pgHost = $parts["Host"]
$port = $parts["Port"]
$user = $parts["Username"]
$password = $parts["Password"]
$database = $parts["Database"]

if (-not $pgHost -or -not $user -or -not $database) {
    throw "Connection string must include Host, Username, Password, and Database."
}

if (-not $port) { $port = "5432" }

$resolvedCsv = (Resolve-Path $CsvPath).Path
$utf8Csv = Convert-ToUtf8TempFile $resolvedCsv

Write-Host "Importing vocabulary from $resolvedCsv into $database@$pgHost..."

$env:PGPASSWORD = $password
try {
    $copyCommand = "\copy vocabulary(topic, language1, language2, example) FROM '$utf8Csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')"
    psql -h $pgHost -p $port -U $user -d $database -c $copyCommand
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    if (Test-Path $utf8Csv) {
        Remove-Item $utf8Csv -ErrorAction SilentlyContinue
    }
}
