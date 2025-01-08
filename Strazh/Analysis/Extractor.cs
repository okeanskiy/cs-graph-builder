using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using Strazh.Domain;
using System.IO;

namespace Strazh.Analysis
{
    public static class Extractor
    {
        public static TypeNode CreateTypeNode(this ISymbol symbol, TypeDeclarationSyntax declaration)
        {
            (string fullName, string name) = (symbol.ContainingNamespace.ToString() + '.' + symbol.Name, symbol.Name);
            switch (declaration)
            {
                case ClassDeclarationSyntax _:
                    return new ClassNode(fullName, name, declaration.Modifiers.MapModifiers());
                case InterfaceDeclarationSyntax _:
                    return new InterfaceNode(fullName, name, declaration.Modifiers.MapModifiers());
            }
            return null;
        }

        public static ClassNode CreateClassNode(this TypeInfo typeInfo)
            => new ClassNode(GetFullName(typeInfo), GetName(typeInfo));

        private static InterfaceNode CreateInterfaceNode(this TypeInfo typeInfo)
            => new InterfaceNode(GetFullName(typeInfo), GetName(typeInfo));

        private static string[] MapModifiers(this SyntaxTokenList syntaxTokens)
            => syntaxTokens.Select(x => x.ValueText).ToArray();

        public static TypeNode CreateTypeNode(this TypeInfo typeInfo)
        {
            switch (typeInfo.ConvertedType.TypeKind)
            {
                case TypeKind.Interface:
                    return CreateInterfaceNode(typeInfo);

                case TypeKind.Class:
                    return CreateClassNode(typeInfo);

                default:
                    return null;
            }
        }

        public static string GetName(this TypeInfo typeInfo)
            => typeInfo.Type.Name;

        private static string GetFullName(this TypeInfo typeInfo)
            => typeInfo.Type.ContainingNamespace.ToString() + "." + GetName(typeInfo);

        private static string GetNamespaceName(this INamespaceSymbol namespaceSymbol, string name)
        {
            var nextName = namespaceSymbol?.Name;
            if (string.IsNullOrEmpty(nextName))
            {
                return name;
            }
            return GetNamespaceName(namespaceSymbol.ContainingNamespace, $"{nextName}.{name}");
        }

        public static MethodNode CreateMethodNode(this IMethodSymbol symbol, MethodDeclarationSyntax declaration = null)
        {
            var temp = $"{symbol.ContainingType}.{symbol.Name}";
            var fullName = symbol.ContainingNamespace.GetNamespaceName($"{symbol.ContainingType.Name}.{symbol.Name}");
            var args = symbol.Parameters.Select(x => (name: x.Name, type: x.Type.ToString())).ToArray();
            var returnType = symbol.ReturnType.ToString();
            var sourceCode = declaration?.ToString();
            return new MethodNode(fullName,
                symbol.Name,
                args,
                returnType,
                declaration?.Modifiers.MapModifiers(),
                sourceCode);
        }

        public static string GetName(string filePath)
            => filePath.Split(Path.DirectorySeparatorChar).Reverse().FirstOrDefault();

        public static List<TripleIncludedIn> GetFolderChain(string filePath, FileNode file)
        {
            var triples = new List<TripleIncludedIn>();
            var chain = filePath.Split(Path.DirectorySeparatorChar);
            FolderNode prev = null;
            var path = string.Empty;
            foreach (var item in chain)
            {
                if (string.IsNullOrEmpty(path))
                {
                    path = item;
                    prev = new FolderNode(path, item);
                    continue;
                }
                if (item == file.Name)
                {
                    triples.Add(new TripleIncludedIn(file, prev));
                    return triples;
                }
                else
                {
                    path = Path.DirectorySeparatorChar == '/' ? $"{path}/{item}" : $"{path}\\{item}";
                    triples.Add(new TripleIncludedIn(new FolderNode(path, item), new FolderNode(prev.FullName, prev.Name)));
                    prev = new FolderNode(path, item);
                }
            }
            return triples;
        }       

    }
}