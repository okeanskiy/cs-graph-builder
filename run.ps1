param(
    [Alias("b")]
    [switch]$Build
)

if ($Build) {
    Write-Host "[run.ps1] Building the project..."
    dotnet build ./Strazh/Strazh.csproj -c Release -o ./app
}

Write-Host "[run.ps1] Running the project..."

# Run the project
dotnet ./app/Strazh.dll -c neo4j:neo4j:strazhstrazh -p ./Strazh/Strazh.csproj
