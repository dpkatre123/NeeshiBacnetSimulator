$xmlPath = "$env:TEMP\bacnet_sim_1234.xml"
if (Test-Path $xmlPath) {
	Write-Host "=== XML Generated ===" 
	Get-Content $xmlPath | Select-Object -First 80
	Write-Host ""
	Write-Host "=== Looking for Point Objects ===" 
	Get-Content $xmlPath | Select-String "OBJECT_ANALOG|OBJECT_BINARY" | Select-Object -First 20
} else {
	Write-Host "XML not found at $xmlPath"
	Write-Host "Available XML files in temp:"
	Get-ChildItem $env:TEMP -Filter "bacnet_sim*.xml"
}
