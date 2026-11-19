# Morphir.Diagrams.Query

A bounded structural query language over trees, with an adapter for BPMN documents from
[Morphir.Bpmn](../Morphir.Bpmn/README.md). Patterns follow only the tree's containment: IDs,
reference attributes, coordinates and labels never connect nodes.

```csharp
using System;
using Morphir.Bpmn;
using Morphir.Diagrams.Query;

const string source = """
    <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" targetNamespace="urn:q">
      <bpmn:process id="P"><bpmn:userTask id="A" name="Approve"/><bpmn:serviceTask id="S"/></bpmn:process>
    </bpmn:definitions>
    """;

if (BpmnDocument.TryRead(source, out var document, out _) &&
    TreeQuery.TryCompile("((bpmn.userTask) @task (#attr-eq? @task \"name\" \"Approve\"))", null, QueryLimits.Default, out var query, out var error))
{
    var cursor = query.Run(BpmnQueryTree.Create(document), null);
    var batch = cursor.Next(64);
    foreach (var match in batch.Matches)
    {
        Console.WriteLine($"{match.PatternIndex}: {match.Captures[0].Name} = {match.Captures[0].Node.Id}");
    }
}
```

## Language version 1

- A node pattern is `(kind children…)` followed by captures `@name`. A kind is `prefix.name`
  (`bpmn`, `bpmndi`, `dc` and `di` are bound; `TreeQuery.Compile` takes more prefixes), `_` for any
  node, or `category.name`. A `bpmn.` name must be a BPMN 2.0.2 element.
- Categories: `document`, `comment`, `processing-instruction`, `text`, `activity`, `event`,
  `gateway`, `connection`, `data`, `artifact`, `participant`, `lane`, `choreography-activity`,
  `conversation`, `conversation-link`, `model`, `diagram-interchange` and `extension`.
- Child patterns match direct children in order, not necessarily adjacent, and every assignment
  is a match. `field: (pattern)` limits a child to a field such as `bpmn.flowElement`,
  `bpmn.artifact`, `bpmn.rootElement` or `bpmn.eventDefinition`; `!field` requires that the node
  has no child in it.
- `[a b]` is an alternative; each branch that matches gives its own match.
- A group `((pattern) (#predicate …) …)` adds predicates over its captures: `#attr-eq?`,
  `#attr-not-eq?` (the attribute must be present), `#has-attr?`, `#missing-attr?`, `#attr-in?` and
  `#text-eq?`.
- `;` starts a comment.

Tree-sitter features that version 1 does not have fail to compile with a stable code and the span
of the feature, in UTF-16 code units: `query/unsupported-quantifier` (`*`, `+`, `?`),
`query/unsupported-anchor` (`.`), `query/unsupported-directive` (`#set!`),
`query/unsupported-regex` (`#match?`), `query/unsupported-grouping` (sibling patterns in a
group), and `query/unknown-predicate`. Other errors are `query/unknown-kind`,
`query/unbound-prefix`, `query/unknown-category`, `query/unknown-field`,
`query/duplicate-capture`, `query/unknown-capture` and `query/syntax`.

## Running

`TreeQuery.Run(tree, root)` returns a cursor over the matches below a node, the node itself
included. Matches come in candidate preorder, then pattern order, then child positions, each with
its pattern index and its captures in the order they appear in the pattern. `Next(count)` reads a
batch and its status: `More`, `Complete` or `LimitReached` with the bound's name. `QueryLimits`
bounds the source, patterns, depth, work, pending child assignments, captures and results;
evaluation bounds count across batches, so the batch size never changes the matches.

`BpmnQueryTree.Create(document)` builds the tree of a BPMN document: the document node, then
comments, processing instructions, elements and text in order, without white space or the XML
declaration. Each `QueryNode` has its preorder `Ordinal`, `Kind`, `Category`, `Id`,
`Attribute(name)`, `Text`, `Line`, `Column`, `Depth`, `Parent`, `Children` and its `Source` node.
All 34 comparable cases of the pinned diagrams.mbt query corpus give the same matches and errors.
