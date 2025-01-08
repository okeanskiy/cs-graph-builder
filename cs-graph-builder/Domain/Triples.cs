using System;
using System.Collections.Generic;

namespace Strazh.Domain
{
    public abstract class Triple
    {
        public Node NodeA { get; set; }

        public Node NodeB { get; set; }

        public Relationship Relationship { get; set; }

        protected Triple(Node nodeA, Node nodeB, Relationship relationship)
        {
            if (nodeA == null)
                throw new ArgumentNullException(nameof(nodeA));
            if (nodeB == null)
                throw new ArgumentNullException(nameof(nodeB));
            if (relationship == null)
                throw new ArgumentNullException(nameof(relationship));
            
            
            NodeA = nodeA;
            NodeB = nodeB;
            Relationship = relationship;
        }

        public override string ToString()
            => $"MERGE (a:{NodeA.Label} {{ pk: \"{NodeA.Pk}\" }}) ON CREATE SET {NodeA.Set("a")} ON MATCH SET {NodeA.Set("a")} MERGE (b:{NodeB.Label} {{ pk: \"{NodeB.Pk}\" }}) ON CREATE SET {NodeB.Set("b")} ON MATCH SET {NodeB.Set("b")} MERGE (a)-[:{Relationship.Type}]->(b);";

        public (string Query, Dictionary<string, object> Parameters) GetQueryAndParameters()
        {
            // Build the query with parameter placeholders
            var query = $@"
MERGE (a:{NodeA.Label} {{ pk: $nodeA_pk }})
ON CREATE SET a += $nodeA_properties
ON MATCH SET a += $nodeA_properties
MERGE (b:{NodeB.Label} {{ pk: $nodeB_pk }})
ON CREATE SET b += $nodeB_properties
ON MATCH SET b += $nodeB_properties
MERGE (a)-[:{Relationship.Type}]->(b);";

            // Collect parameters
            var parameters = new Dictionary<string, object>
            {
                { "nodeA_pk", NodeA.Pk },
                { "nodeA_properties", NodeA.GetPropertiesDictionary() },
                { "nodeB_pk", NodeB.Pk },
                { "nodeB_properties", NodeB.GetPropertiesDictionary() },
            };

            return (query, parameters);
        }
    }

    // Structure

    public class TripleDependsOnProject : Triple
    {
        public TripleDependsOnProject(
            ProjectNode projectA,
            ProjectNode projectB)
            : base(projectA, projectB, new DependsOnRelationship())
        { }
    }

    public class TripleDependsOnPackage : Triple
    {
        public TripleDependsOnPackage(
            ProjectNode projectA,
            PackageNode packageB)
            : base(projectA, packageB, new DependsOnRelationship())
        { }
    }

    public class TripleIncludedIn : Triple
    {
        public TripleIncludedIn(
            ProjectNode contentA,
            FolderNode contentB)
            : base(contentA, contentB, new IncludedInRelationship())
        { }

        public TripleIncludedIn(
            FolderNode contentA,
            FolderNode contentB)
            : base(contentA, contentB, new IncludedInRelationship())
        { }

        public TripleIncludedIn(
            FileNode contentA,
            FolderNode contentB)
            : base(contentA, contentB, new IncludedInRelationship())
        { }
    }

    public class TripleDeclaredAt : Triple
    {
        public TripleDeclaredAt(
            TypeNode typeA,
            FileNode fileB)
            : base(typeA, fileB, new DeclaredAtRelationship())
        { }
    }

    // Code

    public class TripleInvoke : Triple
    {
        public TripleInvoke(
            MethodNode methodA,
            MethodNode methodB)
            : base(methodA, methodB, new InvokeRelationship())
        { }
    }

    public class TripleImplement : Triple
    {
        public TripleImplement(
            MethodNode iface,
            MethodNode implementation)
            : base(iface, implementation, new ImplementRelationship())
        {}
    }

    public class TripleHave : Triple
    {
        public TripleHave(
            TypeNode typeA,
            MethodNode methodB)
            : base(typeA, methodB, new HaveRelationship())
        { }
    }

    public class TripleConstruct : Triple
    {
        public TripleConstruct(
            MethodNode methodA,
            ClassNode classB)
            : base(methodA, classB, new ConstructRelationship())
        { }
    }

    public class TripleOfType : Triple
    {
        public TripleOfType(
            ClassNode classA,
            TypeNode typeB)
            : base(classA, typeB, new OfTypeRelationship())
        { }

        public TripleOfType(
            InterfaceNode interfaceA,
            InterfaceNode interfaceB)
            : base(interfaceA, interfaceB, new OfTypeRelationship())
        { }
    }
}