using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Strazh.Domain;
using Buildalyzer;
using Buildalyzer.Workspaces;
using System.Collections.Generic;
using System;
using Strazh.Database;
using static Strazh.Analysis.AnalyzerConfig;
using System.IO;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Strazh.Analysis
{
    public class Analyzer
    {
        private readonly Dictionary<SyntaxTree, Compilation> _compilations = new();
        private AnalyzerManager _manager;
        private Solution _solution;

        public async Task Analyze(AnalyzerConfig config)
        {
            Console.WriteLine($"Setup analyzer...");

            _manager = config.IsSolutionBased
                ? new AnalyzerManager(config.Solution)
                : new AnalyzerManager();

            var projectAnalyzers = config.IsSolutionBased
                ? _manager.Projects.Values
                : config.Projects.Select(x => _manager.GetProject(x));
            
            Console.WriteLine($"Analyzer ready to analyze {projectAnalyzers.Count()} project/s.");

            var workspace = new AdhocWorkspace();
            var projects = new List<(Project, IProjectAnalyzer)>();

            var sortedProjectAnalyzers = SortProjectAnalyzers(projectAnalyzers);

            foreach (var projectAnalyzer in sortedProjectAnalyzers)
            {
                var project = projectAnalyzer.AddToWorkspace(workspace);
                projects.Add((project, projectAnalyzer));
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                {
                    foreach (var syntaxTree in compilation.SyntaxTrees)
                    {
                        _compilations[syntaxTree] = compilation;
                    }
                }
            }

            _solution = workspace.CurrentSolution;
            
            for (var index = 0; index < projects.Count; index++)
            {
                var triples = await AnalyzeProject(projects[index].Item1, projects[index].Item2, config.Tier);
                triples = triples.GroupBy(x => x.ToString()).Select(x => x.First()).OrderBy(x => x.NodeA.Label).ToList();
                await DbManager.InsertData(triples, config.Credentials, config.IsDelete && index == 0);
            }
            workspace.Dispose();
        }

        private IEnumerable<IProjectAnalyzer> SortProjectAnalyzers(IEnumerable<IProjectAnalyzer> projectAnalyzers)
        {
            // Step 1: Build a dependency graph
            var graph = new Dictionary<IProjectAnalyzer, List<IProjectAnalyzer>>();
            var inDegree = new Dictionary<IProjectAnalyzer, int>();

            foreach (var analyzer in projectAnalyzers)
            {
                graph[analyzer] = new List<IProjectAnalyzer>();
                inDegree[analyzer] = 0; // Initialize in-degrees
            }

            foreach (var analyzer in projectAnalyzers)
            {
                var projectReferences = analyzer.Build().FirstOrDefault()?.ProjectReferences;

                if (projectReferences != null)
                {
                    foreach (var reference in projectReferences)
                    {
                        var dependency = projectAnalyzers.FirstOrDefault(pa => pa.ProjectFile.Path == reference);

                        if (dependency != null)
                        {
                            graph[analyzer].Add(dependency);
                            inDegree[dependency]++;
                        }
                    }
                }
            }

            // Step 2: Topological sort using Kahn's algorithm
            var sorted = new List<IProjectAnalyzer>();
            var queue = new Queue<IProjectAnalyzer>();

            foreach (var analyzer in projectAnalyzers)
            {
                if (inDegree[analyzer] == 0)
                {
                    queue.Enqueue(analyzer);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                sorted.Add(current);

                foreach (var dependency in graph[current])
                {
                    inDegree[dependency]--;
                    if (inDegree[dependency] == 0)
                    {
                        queue.Enqueue(dependency);
                    }
                }
            }

            // Step 3: Return sorted list
            if (sorted.Count != projectAnalyzers.Count())
            {
                throw new InvalidOperationException("Circular dependency detected among projects!");
            }

            return sorted;
        }

        private async Task<IList<Triple>> AnalyzeProject(Project project, IProjectAnalyzer projectAnalyzer, Tiers mode)
        {
            Console.WriteLine($"Project #{project.Name}:");
            var root = GetRoot(project.FilePath);
            var rootNode = new FolderNode(root, root);
            var projectName = GetProjectName(project.Name);
            Console.WriteLine($"Analyzing {projectName} project...");

            var triples = new List<Triple>();
            if (mode == Tiers.All || mode == Tiers.Project)
            {
                Console.WriteLine($"Analyzing Project tier...");
                var projectBuild = projectAnalyzer.Build().FirstOrDefault();
                var projectNode = new ProjectNode(projectName);
                triples.Add(new TripleIncludedIn(projectNode, rootNode));
                projectBuild.ProjectReferences.ToList().ForEach(x =>
                {
                    var node = new ProjectNode(GetProjectName(x));
                    triples.Add(new TripleDependsOnProject(projectNode, node));
                });
                projectBuild.PackageReferences.ToList().ForEach(x =>
                {
                    var version = x.Value.Values.FirstOrDefault(x => x.Contains(".")) ?? "none";
                    var node = new PackageNode(x.Key, x.Key, version);
                    triples.Add(new TripleDependsOnPackage(projectNode, node));
                });
                Console.WriteLine($"Analyzing Project tier complete.");
            }

            if (project.SupportsCompilation
                && (mode == Tiers.All || mode == Tiers.Code))
            {
                Console.WriteLine($"Analyzing Code tier...");
                var compilation = await project.GetCompilationAsync();
                var syntaxTreeRoot = compilation.SyntaxTrees.Where(x => !x.FilePath.Contains("obj"));
                foreach (var st in syntaxTreeRoot)
                {
                    var sem = compilation.GetSemanticModel(st);
                    await AnalyzeTree<InterfaceDeclarationSyntax>(triples, st, rootNode);
                    await AnalyzeTree<ClassDeclarationSyntax>(triples, st, rootNode);
                }
                Console.WriteLine($"Analyzing Code tier complete.");
            }

            Console.WriteLine($"Analyzing {projectName} project complete.");
            return triples;
        }

        /// <summary>
        /// Entry to analyze class or interface
        /// </summary>
        public async Task AnalyzeTree<T>(IList<Triple> triples, SyntaxTree st, FolderNode rootFolder)
            where T : TypeDeclarationSyntax
        {
            var root = st.GetRoot();
            var filePath = root.SyntaxTree.FilePath;
            var index = filePath.IndexOf(rootFolder.Name);
            filePath = index < 0 ? filePath : filePath[index..];
            var fileName = Extractor.GetName(filePath);
            var fileNode = new FileNode(filePath, fileName);
            Extractor.GetFolderChain(filePath, fileNode).ForEach(triples.Add);
            var declarations = root.DescendantNodes().OfType<T>();
            foreach (var declaration in declarations)
            {
                var semanticModel = _compilations[declaration.SyntaxTree].GetSemanticModel(declaration.SyntaxTree);
                var node = semanticModel.GetDeclaredSymbol(declaration).CreateTypeNode(declaration);
                if (node != null)
                {
                    triples.Add(new TripleDeclaredAt(node, fileNode));
                    GetInherits(triples, declaration, node);
                    await GetMethodsAllAsync(triples, declaration, node);
                }
            }
        }

        /// <summary>
        /// Type inherited from BaseType
        /// </summary>
        public void GetInherits(IList<Triple> triples, TypeDeclarationSyntax declaration, TypeNode node)
        {
            if (declaration.BaseList != null)
            {
                foreach (var baseTypeSyntax in declaration.BaseList.Types)
                {
                    var semanticModel = _compilations[baseTypeSyntax.SyntaxTree].GetSemanticModel(baseTypeSyntax.SyntaxTree);
                    
                    var parentNode = semanticModel.GetTypeInfo(baseTypeSyntax.Type).CreateTypeNode();
                    if (node is ClassNode classNode)
                    {
                        triples.Add(new TripleOfType(classNode, parentNode));
                    }
                    if (node is InterfaceNode interfaceNode && parentNode is InterfaceNode parentInterfaceNode)
                    {
                        triples.Add(new TripleOfType(interfaceNode, parentInterfaceNode));
                    }
                }
            }
        }
        
        /// <summary>
        /// Class or Interface have some method AND some method can call another method AND some method can creates an object of class
        /// </summary>
        public async Task GetMethodsAllAsync(IList<Triple> triples, TypeDeclarationSyntax declaration, TypeNode node)
        {
            var methods = declaration.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var semanticModel = _compilations[method.SyntaxTree].GetSemanticModel(method.SyntaxTree);
                var methodSymbol = semanticModel.GetDeclaredSymbol(method);
                var methodNode = methodSymbol.CreateMethodNode(method);

                var implementation = (await SymbolFinder.FindImplementationsAsync(methodSymbol, _solution)).ToList();
                if (implementation.Any())
                {
                    var symbols = implementation.SelectMany(i => i.DeclaringSyntaxReferences)
                        .Select(syntaxRef => syntaxRef.GetSyntax())
                        .Where(syntaxNode => syntaxNode.IsKind(SyntaxKind.MethodDeclaration))
                        .Select(s => (MethodDeclarationSyntax)s)
                        .Select(syntaxNode => _compilations[syntaxNode.SyntaxTree].GetSemanticModel(syntaxNode.SyntaxTree).GetDeclaredSymbol(syntaxNode))
                        .Select(s => s.CreateMethodNode());

                    var implementTriples = symbols.Select(s => new TripleImplement(methodNode, s));
                    foreach (var implementTriple in implementTriples)
                    {
                        triples.Add(implementTriple);
                    }
                }

                triples.Add(new TripleHave(node, methodNode));

                foreach (var syntax in method.DescendantNodes().OfType<ExpressionSyntax>())
                {
                    var innerSemanticModel = _compilations[syntax.SyntaxTree].GetSemanticModel(syntax.SyntaxTree);
                    switch (syntax)
                    {
                        case ObjectCreationExpressionSyntax creation:
                            var classNode = innerSemanticModel.GetTypeInfo(creation).CreateClassNode();
                            triples.Add(new TripleConstruct(methodNode, classNode));
                            break;

                        case InvocationExpressionSyntax invocation:
                            if (innerSemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol invokedSymbol)
                            {
                                var invokedMethod = invokedSymbol.CreateMethodNode();
                                triples.Add(new TripleInvoke(methodNode, invokedMethod));
                            }
                            break;
                    }
                }
            }
        }

        public void GetMethodImplementationsAll(IList<Triple> triples, TypeDeclarationSyntax declaration, TypeNode node)
        {
            var methods = declaration.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var semanticModel = _compilations[method.SyntaxTree].GetSemanticModel(method.SyntaxTree);

                var methodNode = semanticModel.GetDeclaredSymbol(method).CreateMethodNode(method);
                triples.Add(new TripleHave(node, methodNode));

                foreach (var syntax in method.DescendantNodes().OfType<ExpressionSyntax>())
                {
                    var innerSemanticModel = _compilations[syntax.SyntaxTree].GetSemanticModel(syntax.SyntaxTree);
                    switch (syntax)
                    {
                        case InvocationExpressionSyntax invocation:
                            if (innerSemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol invokedSymbol)
                            {
                                var invokedMethod = invokedSymbol.CreateMethodNode();
                                triples.Add(new TripleInvoke(methodNode, invokedMethod));
                            }
                            break;
                    }
                }
            }
            
        }

        private static string GetProjectName(string fullName)
            => fullName.Split(Path.DirectorySeparatorChar).Last().Replace(".csproj", "");

        private static string GetRoot(string filePath)
            => filePath.Split(Path.DirectorySeparatorChar).Reverse().Skip(1).FirstOrDefault();
    }
}