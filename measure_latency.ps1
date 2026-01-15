param (
    [string]$Url,
    [string]$Referer
)

$client = New-Object System.Net.Http.HttpClient
$client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")
if ($Referer) {
    $client.DefaultRequestHeaders.Add("Referer", $Referer)
}

Write-Host "Testing URL: $Url"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
try {
    # Send HEAD request first for TTFB of headers
    $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).Result
    $ttfb = $sw.ElapsedMilliseconds
    Write-Host "SUCCESS: TTFB (Headers) = $ttfb ms"
    Write-Host "Status: $($response.StatusCode)"
    
    if ($response.IsSuccessStatusCode) {
        $sw.Restart()
        $stream = $response.Content.ReadAsStreamAsync().Result
        $buffer = New-Object byte[] 1024 # Read just 1KB
        $read = $stream.Read($buffer, 0, $buffer.Length)
        $firstByteTime = $sw.ElapsedMilliseconds
        Write-Host "First 1KB read: $firstByteTime ms"
        
        # Now try a range request to see if it's faster for seeking
        $sw.Restart()
        $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $Url)
        $request.Headers.Add("Range", "bytes=1000000-2000000")
        $rangeResponse = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).Result
        $rangeTtfb = $sw.ElapsedMilliseconds
        Write-Host "Range Request TTFB (1MB offset): $rangeTtfb ms"
    }
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)"
}
finally {
    $client.Dispose()
}
