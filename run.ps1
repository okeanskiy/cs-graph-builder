param(
    [Alias("b")]
    [switch]$Build
)

if ($Build) {
    Write-Host "[run.ps1] Building the project..."
    dotnet build ./cs-graph-builder/cs-graph-builder.csproj -c Release -o ./app
}

Write-Host "[run.ps1] Running the project..."

# Run the project
dotnet ./app/cs-graph-builder.dll -c neo4j:neo4j:strazhstrazh -p ./cs-graph-builder/cs-graph-builder.csproj
