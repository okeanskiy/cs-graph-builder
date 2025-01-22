Write-Host "[build.ps1] Building the project..."
dotnet build ./cs-graph-builder/cs-graph-builder.csproj -c Release -o ./app
if ($LASTEXITCODE -ne 0) {
    Write-Host "[build.ps1] Build failed. Exiting..."
    exit $LASTEXITCODE
}