# Build the project
dotnet build ./Strazh/Strazh.csproj -c Release -o ./app

# Run the project
dotnet ./app/Strazh.dll -c neo4j:neo4j:strazh -p ./Strazh/Strazh.csproj
