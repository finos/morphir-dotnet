# Unist Specification Knowledge Base

**Task**: Task 2.1 - Unified.js Architecture Research (Issue #316)
**Created**: 2025-12-26
**Purpose**: Deep dive into unist (Universal Syntax Tree) specification for understanding tree structures and position tracking

## Table of Contents

1. [Unist Overview](#unist-overview)
2. [Node Interfaces](#node-interfaces)
3. [Position Tracking](#position-tracking)
4. [Tree Structure](#tree-structure)
5. [Traversal Patterns](#traversal-patterns)
6. [Ecosystem Extensions](#ecosystem-extensions)
7. [Comparison with Morphir IR](#comparison-with-morphir-ir)

---

## 1. Unist Overview

### 1.1 Core Concept

**Unist** (Universal Syntax Tree) is a specification for syntax trees. It defines a general-purpose format for representing syntax trees that is:
- **Universal**: Works across programming languages, markup languages, natural language
- **Interoperable**: Enables utilities to work with any conforming tree
- **Extensible**: Specifications can extend unist with domain-specific nodes

From documentation:
> "Unist is a specification for syntax trees. It has a big ecosystem of utilities in JavaScript for working with these trees."

### 1.2 Design Philosophy

**Minimal Core, Maximum Extensions**:
- Core defines only essential structure (Node, Parent, Literal)
- Ecosystems extend with domain-specific nodes
- Utilities work with any unist-conforming tree

**Plain JavaScript Objects**:
- Nodes are plain objects, not class instances
- Enables JSON serialization
- Simple to construct and inspect

**Position Information Optional**:
- Generated nodes may omit position
- Parsed nodes include source location
- Position tracking separate from tree structure

### 1.3 Ecosystem Overview

**Specifications Built on Unist**:
- **mdast**: Markdown Abstract Syntax Tree
- **hast**: HTML Abstract Syntax Tree
- **nlcst**: Natural Language Concrete Syntax Tree
- **xast**: XML Abstract Syntax Tree
- **esast**: ECMAScript Abstract Syntax Tree

**Utilities Ecosystem**: 30+ utilities for tree manipulation
- Traversal: `unist-util-visit`, `unist-util-visit-parents`
- Transformation: `unist-util-map`, `unist-util-filter`
- Inspection: `unist-util-is`, `unist-util-find`
- Position: `unist-util-position`, `unist-util-stringify-position`

---

## 2. Node Interfaces

### 2.1 Node (Base Interface)

**Definition**:
```typescript
interface Node {
  type: string           // Non-empty string identifying node variant
  data?: Data           // Information from ecosystem
  position?: Position   // Location in source file
}
```

**Fields**:

**`type`** (required):
- Non-empty string literal
- Identifies the node variant
- Examples: `'heading'`, `'paragraph'`, `'text'`, `'code'`
- Each ecosystem defines its own type values

**`data`** (optional):
- Associated metadata
- Implements `Data` interface (defined by ecosystem)
- Used for custom information not in core specification

**`position`** (optional):
- Location in source document
- Implements `Position` interface
- Absent for generated nodes

**Example**:
```javascript
{
  type: 'text',
  value: 'Hello, world!',
  position: {
    start: { line: 1, column: 1, offset: 0 },
    end: { line: 1, column: 14, offset: 13 }
  }
}
```

### 2.2 Parent (Nodes with Children)

**Definition**:
```typescript
interface Parent extends Node {
  children: Node[]  // List of child nodes
}
```

**Key Properties**:
- Extends `Node` interface
- Adds `children` field
- Children are ordered list
- Children can be any `Node` type (including other `Parent` nodes)

**Example**:
```javascript
{
  type: 'paragraph',
  children: [
    { type: 'text', value: 'Hello, ' },
    { type: 'emphasis', children: [
      { type: 'text', value: 'world' }
    ]},
    { type: 'text', value: '!' }
  ]
}
```

**Design Note**: `children` is always an array, never null/undefined. Empty array for parent with no children.

### 2.3 Literal (Nodes with Values)

**Definition**:
```typescript
interface Literal extends Node {
  value: any  // Primitive value
}
```

**Key Properties**:
- Extends `Node` interface
- Adds `value` field
- Value can be any JSON-serializable type (string, number, boolean, null)
- Typically leaf nodes (no children)

**Examples**:
```javascript
// Text literal
{ type: 'text', value: 'Hello' }

// Number literal
{ type: 'number', value: 42 }

// Code literal
{ type: 'code', value: 'const x = 1', lang: 'javascript' }
```

**Constraint**: From spec:
> "All values must be expressible in JSON values: string, number, object, array, true, false, or null."

### 2.4 Node Type Hierarchy

```
Node (base)
├── Parent (has children)
│   ├── Root
│   ├── Paragraph
│   ├── Heading
│   ├── List
│   └── ... (ecosystem-specific)
├── Literal (has value)
│   ├── Text
│   ├── Code
│   ├── InlineCode
│   └── ... (ecosystem-specific)
└── Other (custom nodes)
    └── ... (ecosystem-specific)
```

**Mixed Interfaces**: Some nodes can be both Parent and have additional data:
```javascript
{
  type: 'link',
  url: 'https://example.com',    // Link-specific property
  children: [                      // Parent interface
    { type: 'text', value: 'Click here' }
  ]
}
```

---

## 3. Position Tracking

### 3.1 Position Interface

**Definition**:
```typescript
interface Position {
  start: Point  // First character of parsed source
  end: Point    // First character after parsed source
}
```

**Semantics**:
- `start`: Points to first character **of** the node
- `end`: Points to first character **after** the node (exclusive)
- Range is `[start, end)` (half-open interval)

**Example**:
```javascript
// Source: "alpha\nbravo"
{
  type: 'text',
  value: 'alpha',
  position: {
    start: { line: 1, column: 1, offset: 0 },
    end: { line: 1, column: 6, offset: 5 }
  }
}
```

### 3.2 Point Interface

**Definition**:
```typescript
interface Point {
  line: number      // 1-indexed line number
  column: number    // 1-indexed column number
  offset?: number   // 0-indexed character position
}
```

**Field Semantics**:

**`line`** (required, 1-indexed):
- Line number in source file
- First line is 1
- Increments at line breaks (`\n`, `\r\n`, `\r`)

**`column`** (required, 1-indexed):
- Column number in line
- First column is 1
- Resets to 1 at line breaks

**`offset`** (optional, 0-indexed):
- Character position from start of file
- First character is 0
- Useful for precise source mapping

**Example Positions**:
```
Source: "alpha\nbravo"

"alpha":
  start: { line: 1, column: 1, offset: 0 }
  end:   { line: 1, column: 6, offset: 5 }

"\n":
  start: { line: 1, column: 6, offset: 5 }
  end:   { line: 2, column: 1, offset: 6 }

"bravo":
  start: { line: 2, column: 1, offset: 6 }
  end:   { line: 2, column: 6, offset: 11 }
```

### 3.3 Generated Nodes

From spec:
> "If the syntactic unit represented by a node is not present in the source file at the time of parsing, the node is said to be generated and it must not have positional information."

**Generated Node**:
```javascript
{
  type: 'emphasis',
  children: [
    { type: 'text', value: 'Generated content' }
  ]
  // No position field
}
```

**Why Track Generated Nodes**:
- Distinguish parsed vs. constructed content
- Enable accurate source maps
- Support incremental parsing

### 3.4 Position Utilities

**Stringify Position**:
```javascript
import { stringifyPosition } from 'unist-util-stringify-position'

const node = {
  type: 'text',
  value: 'example',
  position: {
    start: { line: 1, column: 5 },
    end: { line: 1, column: 12 }
  }
}

stringifyPosition(node)
// => '1:5-1:12'

stringifyPosition(node.position.start)
// => '1:5'
```

---

## 4. Tree Structure

### 4.1 Tree Terminology

**Key Relationships** (from spec):

**Child**: "A node whose parent's children array includes it"

**Parent**: "A node with a children array (Parent)"

**Sibling**: "Nodes that share the same parent"

**Root**: "A node without a parent; the top-level node in a tree"

**Descendant**: "A child or grandchild, recursively"

**Ancestor**: "A parent or grandparent, recursively"

**Leaf**: "A node with no children"

**Branch**: "A node with one or more children"

**Tree**: "A root node and all its descendants"

### 4.2 Tree Example

```javascript
// Tree structure:
//     root
//    /    \
//   p1     p2
//  /  \     |
// t1  t2   t3

const tree = {
  type: 'root',
  children: [
    {
      type: 'paragraph',
      children: [
        { type: 'text', value: 'Hello' },
        { type: 'text', value: 'world' }
      ]
    },
    {
      type: 'paragraph',
      children: [
        { type: 'text', value: 'Goodbye' }
      ]
    }
  ]
}

// Relationships:
// - root: Root, Branch, Ancestor of all, Parent of p1 and p2
// - p1, p2: Children of root, Siblings, Branches, Parents
// - t1, t2: Children of p1, Siblings, Leaves, Descendants of root
// - t3: Child of p2, Leaf, Descendant of root
```

### 4.3 Tree Constraints

**Well-Formed Tree**:
1. Exactly one root node (no parent)
2. All non-root nodes have exactly one parent
3. No cycles (a node cannot be its own ancestor)
4. Children array is ordered

**Validity Rules**:
- `children` must be an array (can be empty)
- All children must be valid nodes
- `type` must be non-empty string
- Position, if present, must be valid

### 4.4 Parent References

**Important**: Unist does NOT include parent references in nodes.

```javascript
// NOT in unist:
{
  type: 'text',
  value: 'Hello',
  parent: { type: 'paragraph', ... }  // ❌ Not part of spec
}
```

**Rationale**:
- Avoids circular references (JSON serialization)
- Simplifies tree transformation
- Parent context provided by traversal utilities

**Accessing Parents**:
```javascript
import { visit } from 'unist-util-visit'

visit(tree, 'text', (node, index, parent) => {
  // parent provided by visitor
  console.log(parent.type)
})
```

---

## 5. Traversal Patterns

### 5.1 Depth-First Traversal

**Preorder (NLR)**: Visit node, then left children, then right children
```javascript
function preorder(node, visitor) {
  visitor(node)  // Visit node first
  if (node.children) {
    for (const child of node.children) {
      preorder(child, visitor)  // Then children
    }
  }
}
```

**Postorder (LRN)**: Visit children, then node
```javascript
function postorder(node, visitor) {
  if (node.children) {
    for (const child of node.children) {
      postorder(child, visitor)  // Children first
    }
  }
  visitor(node)  // Then node
}
```

### 5.2 Breadth-First Traversal

Visit all nodes at depth N before depth N+1:
```javascript
function breadthFirst(root, visitor) {
  const queue = [root]
  while (queue.length > 0) {
    const node = queue.shift()
    visitor(node)
    if (node.children) {
      queue.push(...node.children)
    }
  }
}
```

### 5.3 Visitor Control Flow

From `unist-util-visit` documentation:

**CONTINUE**: Continue traversal normally
```javascript
visit(tree, (node) => {
  console.log(node.type)
  // Implicitly returns CONTINUE
})
```

**SKIP**: Skip node's descendants
```javascript
visit(tree, (node) => {
  if (node.type === 'code') {
    return SKIP  // Don't traverse code contents
  }
})
```

**EXIT**: Stop traversal immediately
```javascript
visit(tree, (node) => {
  if (node.type === 'error') {
    return EXIT  // Stop processing
  }
})
```

### 5.4 Index Management

When modifying siblings during traversal:
```javascript
visit(tree, 'item', (node, index, parent) => {
  // Remove current node
  parent.children.splice(index, 1)
  // Return new index to continue from
  return [SKIP, index]
})
```

---

## 6. Ecosystem Extensions

### 6.1 mdast (Markdown)

```typescript
// mdast extends unist with markdown-specific nodes

interface Heading extends Parent {
  type: 'heading'
  depth: 1 | 2 | 3 | 4 | 5 | 6
  children: PhrasingContent[]
}

interface Link extends Parent {
  type: 'link'
  url: string
  title?: string
  children: PhrasingContent[]
}

interface Code extends Literal {
  type: 'code'
  lang?: string
  meta?: string
  value: string
}
```

**Example**:
```javascript
// Markdown: # Hello, [world](https://example.com)!

{
  type: 'heading',
  depth: 1,
  children: [
    { type: 'text', value: 'Hello, ' },
    {
      type: 'link',
      url: 'https://example.com',
      children: [{ type: 'text', value: 'world' }]
    },
    { type: 'text', value: '!' }
  ]
}
```

### 6.2 hast (HTML)

```typescript
// hast extends unist with HTML-specific nodes

interface Element extends Parent {
  type: 'element'
  tagName: string
  properties: Properties
  children: Node[]
}

interface Text extends Literal {
  type: 'text'
  value: string
}
```

**Example**:
```javascript
// HTML: <p>Hello, <strong>world</strong>!</p>

{
  type: 'element',
  tagName: 'p',
  properties: {},
  children: [
    { type: 'text', value: 'Hello, ' },
    {
      type: 'element',
      tagName: 'strong',
      properties: {},
      children: [{ type: 'text', value: 'world' }]
    },
    { type: 'text', value: '!' }
  ]
}
```

### 6.3 Utility Ecosystem

**Traversal**:
- `unist-util-visit`: Visit nodes matching test
- `unist-util-visit-parents`: Visit with parent stack
- `unist-util-walk`: Custom traversal order

**Transformation**:
- `unist-util-map`: Transform each node
- `unist-util-filter`: Create filtered tree
- `unist-util-flatmap`: Map and flatten
- `unist-util-remove`: Remove nodes matching test

**Inspection**:
- `unist-util-is`: Test node type
- `unist-util-find`: Find first matching node
- `unist-util-find-all-after`: Find all after index
- `unist-util-find-all-before`: Find all before index

**Position**:
- `unist-util-position`: Get node position
- `unist-util-stringify-position`: Format position as string
- `unist-util-generated`: Check if node is generated

**Modification**:
- `unist-util-modify-children`: Modify children with visitor
- `unist-util-parents`: Add parent references
- `unist-util-index`: Create index of nodes

---

## 7. Comparison with Morphir IR

### 7.1 Unist vs. Morphir IR Structure

| Aspect | Unist | Morphir IR |
|--------|-------|-----------|
| **Base Node** | `{ type, data?, position? }` | Discriminated union / sealed record |
| **Children** | `children: Node[]` | Typed fields (e.g., `Elements`, `Body`) |
| **Values** | `value: any` (Literal) | Typed properties (e.g., `Name`, `Value`) |
| **Position** | Optional `Position` interface | Metadata field `'a` (generic) |
| **Type Safety** | String `type` field | Static type checking |
| **Serialization** | JSON-compatible | JSON + type discrimination |

### 7.2 Position Tracking Comparison

**Unist Position**:
```javascript
{
  type: 'text',
  value: 'example',
  position: {
    start: { line: 1, column: 5, offset: 4 },
    end: { line: 1, column: 12, offset: 11 }
  }
}
```

**Morphir IR Metadata**:
```fsharp
type Type<'a> =
    | Variable of 'a * Name
    | Reference of 'a * FQName * Type<'a> list
    | Tuple of 'a * Type<'a> list
    // ...

// Position info in metadata
type SourceLocation = {
    Start: { Line: int; Column: int }
    End: { Line: int; Column: int }
}

let typeWithPos =
    Type.Variable(
        { Start = { Line = 1; Column = 5 }
          End = { Line = 1; Column = 12 } },
        Name.fromString "example"
    )
```

**Key Difference**: Morphir uses generic metadata `'a`, unist uses optional `position` field.

### 7.3 Applicable Patterns for Morphir

**Adopt**:
- ✅ Separate position tracking from tree structure
- ✅ Optional position for generated nodes
- ✅ Visitor utilities with control flow (CONTINUE, SKIP, EXIT)
- ✅ Parent/child terminology for documentation

**Adapt**:
- ⚠️ Position interface → F# record with Start/End
- ⚠️ Plain objects → Discriminated unions / sealed records
- ⚠️ String `type` field → Static types
- ⚠️ Utility functions → Extension methods / modules

**Avoid**:
- ❌ Untyped `value: any` (use typed properties)
- ❌ Array-based children (use typed collections)
- ❌ Runtime type checking (use compile-time types)

### 7.4 Morphir Visitor Utilities

**Proposed Morphir Visitor**:
```fsharp
module IR =
    type VisitorAction =
        | Continue
        | Skip
        | Exit

    let rec visit
        (test: IR -> bool)
        (visitor: IR -> int option -> IR option -> VisitorAction)
        (tree: IR)
        : unit =

        let rec visitNode (node: IR) (index: int option) (parent: IR option) =
            if test node then
                match visitor node index parent with
                | Continue -> visitChildren node
                | Skip -> ()
                | Exit -> raise ExitTraversal
            else
                visitChildren node

        and visitChildren (parent: IR) =
            match parent with
            | Type.Tuple(_, elements) ->
                elements |> List.iteri (fun i child ->
                    visitNode child (Some i) (Some parent))
            | Value.Apply(_, func, arg) ->
                visitNode func None (Some parent)
                visitNode arg None (Some parent)
            | _ -> ()

        try
            visitNode tree None None
        with
        | ExitTraversal -> ()
```

**Usage**:
```fsharp
// Find all type variables
let variables = ResizeArray<Name>()
IR.visit
    (function | Type.Variable _ -> true | _ -> false)
    (fun node _ _ ->
        match node with
        | Type.Variable(_, name) ->
            variables.Add(name)
            Continue
        | _ -> Continue)
    myType
```

---

## Summary

**Key Insights from Unist**:

1. **Minimal Core**: Node, Parent, Literal interfaces provide foundation
2. **Position Tracking**: Separate from tree structure, optional for generated nodes
3. **Tree Terminology**: Clear definitions for parent, child, sibling, ancestor, descendant
4. **Traversal Patterns**: Depth-first (preorder/postorder) and breadth-first
5. **Visitor Control Flow**: CONTINUE, SKIP, EXIT actions for flexible traversal
6. **Ecosystem Extensions**: Domain-specific nodes extend base interfaces
7. **Utility Ecosystem**: Reusable utilities work with any unist tree

**For Morphir-dotnet**:

**Position Tracking**:
- Adopt optional position with Start/End points
- Use 1-indexed line/column, 0-indexed offset
- Distinguish parsed vs. generated nodes

**Visitor Utilities**:
- Implement control flow actions (Continue, Skip, Exit)
- Provide parent context during traversal
- Support index-aware modification

**Tree Structure**:
- Use clear terminology (parent, child, ancestor, descendant)
- Document tree constraints and validity rules
- Provide utilities for common traversal patterns

**Utility Ecosystem**:
- Build composable utilities (visit, map, filter, find)
- Separate traversal from transformation
- Enable type-safe operations on IR

---

**Related Documents**:
- [Unified.js Architecture](./unified-js-architecture.md)
- [VFile Pattern](./vfile-pattern.md)
- [Unified to .NET Adaptation](./unified-to-dotnet-adaptation.md)
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
