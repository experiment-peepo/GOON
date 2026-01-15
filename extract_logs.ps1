$logPath = "d:\Projects\Develop\GOON\GOON\bin\Debug\net8.0-windows\Data\GOON.log"
$outputPath = "d:\Projects\Develop\GOON\log_final.txt"
$startTime = "20:41:34"

$lines = Get-Content $logPath
$startIndex = -1

for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -like "*$startTime*") {
        $startIndex = $i
        break
    }
}

if ($startIndex -ne -1) {
    # Extract 10,000 lines or until the end
    $endIndex = [Math]::Min($startIndex + 10000, $lines.Length - 1)
    $lines[$startIndex..$endIndex] | Out-File $outputPath -Encoding utf8
    Write-Output "Successfully extracted lines from $startIndex to $endIndex"
} else {
    Write-Output "Start time $startTime not found."
}
