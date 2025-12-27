# Unified.js Architecture Knowledge Base

**Task**: Task 2.1 - Unified.js Architecture Research (Issue #316)
**Created**: 2025-12-26
**Purpose**: Deep dive into unified.js pluggable transformation pipeline architecture for adaptation to morphir-dotnet

## Table of Contents

1. [Core Architecture](#core-architecture)
2. [Processor Pattern](#processor-pattern)
3. [Plugin Architecture](#plugin-architecture)
4. [Pipeline Composition](#pipeline-composition)
5. [Ecosystem Bridges](#ecosystem-bridges)
6. [Key Design Patterns](#key-design-patterns)
7. [Comparison with Morphir](#comparison-with-morphir)

---

## 1. Core Architecture

### 1.1 Three-Phase Pipeline

Unified implements a **Parse → Transform → Stringify** pipeline that separates concerns cleanly:

```
Input Text → [Parser] → Syntax Tree → [Transformers] → Modified Tree → [Compiler] → Output Text
```

**Phase 1: Parse**
- Converts input text to syntax tree (AST)
- Parser is syntax-specific (markdown, HTML, natural language)
- Produces unist-compatible tree structure

**Phase 2: Transform**
- Plugins inspect and modify the syntax tree
- Multiple transformers run in sequence
- Each transformer receives tree from previous transformer

**Phase 3: Stringify**
- Compiler converts syntax tree to output text
- Output format can differ from input format
- Enables cross-format transformations (markdown → HTML)

### 1.2 Interface Design

```javascript
// Unified processor creation
const processor = unified()
  .use(remarkParse)           // Attach parser
  .use(remarkRehype)          // Attach transformer (bridge)
  .use(rehypeStringify)       // Attach compiler

// Process content
const result = await processor.process('# Hello')
```

**Key Insight**: Each phase is pluggable, allowing arbitrary parsers, transformers, and compilers to be composed.

### 1.3 Processor as Interface

Unified acts as an **interface for processing content with syntax trees**. The processor:
- Manages plugin lifecycle
- Coordinates phase execution
- Provides consistent API regardless of ecosystem

**Contrast with Traditional Compilers**: Most compilers have monolithic pipelines. Unified externalizes each phase as a plugin, enabling mix-and-match composition.

---

## 2. Processor Pattern

### 2.1 Processor Lifecycle

```javascript
// Create processor (unfrozen)
const processor = unified()

// Configure with plugins (still unfrozen)
processor.use(plugin1)
processor.use(plugin2, options)

// Freeze processor (becomes immutable)
const frozen = processor.freeze()

// Create descendant processor (unfrozen copy)
const child = frozen()
child.use(plugin3)  // Configures child, not parent
```

**Immutability via Freezing**:
- Published processors are frozen to prevent global mutation
- Consumers call frozen processor to create new configurable instance
- Ensures plugin authors can't accidentally modify shared processors

### 2.2 Processor Inheritance

From documentation:
> "New unfrozen processor...configured to work the same as its ancestor. When descendant configured, it does not affect ancestral processor."

**Pattern**: Processors form inheritance hierarchies without class-based OOP:

```javascript
const base = unified().use(parser)
const variant1 = base().use(transformer1)  // Independent
const variant2 = base().use(transformer2)  // Independent
```

**Morphir Parallel**: Similar to how Morphir IR transformers can share base configurations but specialize independently.

### 2.3 Processor Methods

```typescript
interface Processor {
  // Plugin attachment
  use(plugin: Plugin, ...settings: any[]): Processor

  // Processing
  process(file: VFile): Promise<VFile>
  processSync(file: VFile): VFile

  // Tree manipulation
  parse(file: VFile): Node
  stringify(node: Node, file: VFile): string
  run(node: Node, file: VFile): Promise<Node>
  runSync(node: Node, file: VFile): Node

  // Lifecycle
  freeze(): Processor
  data(key: string, value?: any): any
}
```

**Key Methods**:
- `use()`: Attach plugins (modifies processor)
- `process()`: Run full pipeline (parse → transform → stringify)
- `run()`: Run only transform phase
- `parse()` / `stringify()`: Run individual phases
- `freeze()`: Make processor immutable
- `data()`: Store/retrieve metadata

---

## 3. Plugin Architecture

### 3.1 Plugin Anatomy

A plugin consists of two functions:
1. **Attacher**: Configures processor, returns transformer
2. **Transformer**: Modifies syntax tree or file

```javascript
// Plugin structure
export function myPlugin(options = {}) {  // Attacher
  return function transformer(tree, file) {  // Transformer
    // Modify tree or file
    visit(tree, 'heading', (node) => {
      node.depth = Math.min(node.depth + 1, 6)
    })
  }
}

// Usage
processor.use(myPlugin, { maxDepth: 5 })
```

**Attacher Function**:
- Receives options from `use()` call
- Configures processor via `this` context
- Returns transformer function (optional)

**Transformer Function**:
- Signature: `(tree, file, next?) => tree | void | Promise`
- Modifies tree in-place or returns new tree
- Can access/modify `file` (vfile instance)

### 3.2 Plugin Types

**Type 1: Parser Plugin**
```javascript
export function remarkParse(options) {
  this.Parser = createMarkdownParser(options)
  // No transformer needed
}
```

**Type 2: Compiler Plugin**
```javascript
export function rehypeStringify(options) {
  this.Compiler = createHTMLCompiler(options)
  // No transformer needed
}
```

**Type 3: Transformer Plugin**
```javascript
export function remarkGfm(options) {
  return function transformer(tree, file) {
    // Transform tree for GitHub Flavored Markdown
    enhanceWithGfm(tree, options)
  }
}
```

**Type 4: Bridge Plugin**
```javascript
export function remarkRehype(options) {
  return function transformer(mdast, file) {
    // Convert mdast → hast
    const hast = toHast(mdast, options)
    return hast
  }
}
```

### 3.3 Plugin Lifecycle

```
1. Processor.use(plugin, options)
   ↓
2. Call attacher with options
   ↓
3. Attacher configures processor (via `this`)
   ↓
4. Attacher returns transformer
   ↓
5. Processor stores transformer
   ↓
6. On process(), run all transformers in order
```

**Timing**:
- Attachers run **once** during processor configuration
- Transformers run **per file** during processing

---

## 4. Pipeline Composition

### 4.1 Sequential Transformation

Transformers run in order, each receiving output from previous:

```javascript
const processor = unified()
  .use(remarkParse)         // Step 1: Text → mdast
  .use(remarkGfm)           // Step 2: Enhance mdast
  .use(remarkRehype)        // Step 3: mdast → hast
  .use(rehypeHighlight)     // Step 4: Enhance hast
  .use(rehypeStringify)     // Step 5: hast → HTML

// Execution flow:
// Text → mdast → mdast+GFM → hast → hast+highlight → HTML
```

**Key Insight**: Each transformer sees cumulative effect of prior transformers.

### 4.2 Bridge Mode vs. Mutate Mode

**Bridge Mode**: Transformer returns new tree type
```javascript
function remarkRehype() {
  return (mdast) => {
    const hast = convertMdastToHast(mdast)
    return hast  // Different tree type!
  }
}
```

**Mutate Mode**: Transformer modifies tree in-place
```javascript
function remarkGfm() {
  return (tree) => {
    visit(tree, 'table', enhanceTable)
    // Return void or same tree
  }
}
```

**Morphir Parallel**: Bridge mode = IR version migration. Mutate mode = IR optimization passes.

### 4.3 Error Handling

Plugins report errors via vfile messages:

```javascript
function validatePlugin() {
  return (tree, file) => {
    visit(tree, 'link', (node) => {
      if (!node.url) {
        file.message('Missing URL in link', node)
      }
    })
  }
}
```

Messages have severity levels:
- `file.message()`: Warning (fatal=false)
- `file.info()`: Informational (fatal=undefined)
- `file.fail()`: Error (fatal=true)

---

## 5. Ecosystem Bridges

### 5.1 Cross-Ecosystem Transformations

Unified's power: transform across syntax tree ecosystems:

```javascript
unified()
  .use(remarkParse)      // markdown → mdast
  .use(remarkRehype)     // mdast → hast
  .use(rehypeRetext)     // hast → nlcst
  .use(retextStringify)  // nlcst → text
```

**Ecosystems**:
- **remark**: Markdown (mdast)
- **rehype**: HTML (hast)
- **retext**: Natural language (nlcst)

**Bridge Plugins**:
- `remark-rehype`: mdast → hast
- `rehype-remark`: hast → mdast
- `rehype-retext`: hast → nlcst

### 5.2 Format Conversion Example

```javascript
// Markdown → HTML with syntax highlighting
const processor = unified()
  .use(remarkParse)           // Text → mdast
  .use(remarkGfm)             // Add GFM support
  .use(remarkMath)            // Add math support
  .use(remarkRehype)          // mdast → hast
  .use(rehypeHighlight)       // Syntax highlighting
  .use(rehypeKatex)           // Math rendering
  .use(rehypeStringify)       // hast → HTML

const html = await processor.process('# Hello\n\n```js\nconst x = 1\n```')
```

**Morphir Opportunity**: Similar cross-format transformations:
- Morphir IR → TypeScript AST → TypeScript
- Morphir IR → Scala AST → Scala
- Morphir IR → SQL AST → SQL

---

## 6. Key Design Patterns

### 6.1 Visitor Pattern

Plugins use visitor pattern for tree traversal:

```javascript
import { visit } from 'unist-util-visit'

function myTransformer() {
  return (tree) => {
    visit(tree, 'heading', (node, index, parent) => {
      // Visit all heading nodes
      console.log(node.depth, node.children)
    })
  }
}
```

**Pattern**: `unist-util-visit` implements depth-first preorder traversal with control flow.

### 6.2 Middleware Pattern

Transformers act as middleware in a chain:

```javascript
// Each transformer is middleware
function middleware1() {
  return (tree, file, next) => {
    // Pre-processing
    const result = next()  // Call next transformer
    // Post-processing
    return result
  }
}
```

**Note**: Unified doesn't explicitly support `next()` callback pattern (unlike Express.js), but the sequential execution achieves similar composition.

### 6.3 Registry Pattern

Processor uses registry pattern for plugins:

```javascript
// Internal registry (simplified)
class Processor {
  constructor() {
    this.attachers = []
    this.transformers = []
    this.Parser = null
    this.Compiler = null
  }

  use(plugin, ...settings) {
    const transformer = plugin.apply(this, settings)
    if (transformer) {
      this.transformers.push(transformer)
    }
    return this
  }
}
```

### 6.4 Immutable Update Pattern

Transformers often use immutable updates:

```javascript
function incrementHeadings() {
  return (tree) => {
    visit(tree, 'heading', (node, index, parent) => {
      // Immutable update
      parent.children[index] = {
        ...node,
        depth: Math.min(node.depth + 1, 6)
      }
    })
  }
}
```

**Morphir Connection**: Similar to F# record updates `{ record with Field = value }`.

---

## 7. Comparison with Morphir

### 7.1 Unified vs. Morphir Architecture

| Aspect | Unified.js | Morphir-dotnet |
|--------|-----------|----------------|
| **Pipeline** | Parse → Transform → Stringify | Parse → IR → Transform → Codegen |
| **Plugin System** | Attacher + Transformer functions | No explicit plugin system (yet) |
| **Tree Format** | Unist (universal) | Morphir IR (domain-specific) |
| **Bridges** | Cross-format (mdast↔hast↔nlcst) | Cross-version (IR v1→v2→v3) |
| **Immutability** | Frozen processors | Immutable F# records |
| **Composition** | `.use()` chaining | Computation expressions |
| **Error Reporting** | VFile messages | Result<T, Error> |

### 7.2 Applicable Patterns

**For Morphir IR Pipeline**:

1. **Processor Pattern**: Create `MorphirProcessor` with `.use(plugin)` API
   ```csharp
   var processor = MorphirProcessor.Create()
       .Use(new TypeInferencePlugin())
       .Use(new OptimizationPlugin())
       .Use(new CSharpCodegenPlugin());
   ```

2. **Attacher/Transformer Split**: Separate configuration from execution
   ```fsharp
   type ITransformPlugin =
       abstract Configure: ProcessorContext -> unit
       abstract Transform: IR -> VFile -> IR
   ```

3. **Bridge Plugins**: IR version migration as first-class concept
   ```fsharp
   let v2ToV3Bridge = {
       Configure = fun ctx -> ()
       Transform = fun ir file -> IR.migrateV2ToV3 ir
   }
   ```

4. **VFile Pattern**: Track file metadata and diagnostics
   ```fsharp
   type MorphirFile = {
       Path: string
       Content: IR
       Messages: Message list
       Data: Map<string, obj>
   }
   ```

### 7.3 Morphir-dotnet Recommendations

**Adopt**:
- ✅ Processor inheritance (frozen/unfrozen pattern)
- ✅ Plugin registry with `.use()` API
- ✅ VFile-style diagnostic collection
- ✅ Visitor utilities for IR traversal

**Adapt**:
- ⚠️ Attacher/transformer pattern → F# record with `Configure` and `Transform`
- ⚠️ Middleware chaining → F# computation expressions
- ⚠️ Bridge plugins → IR version migration plugins

**Avoid**:
- ❌ JavaScript-specific patterns (prototype mutation)
- ❌ Implicit context (`this` in attacher) → Use explicit parameters
- ❌ Dynamic typing → Leverage F#/C# type systems

### 7.4 Implementation Strategy

**Phase 1: Core Processor**
```fsharp
type MorphirProcessor = {
    Plugins: Plugin list
    Data: Map<string, obj>
}
with
    static member Create() = { Plugins = []; Data = Map.empty }

    member this.Use(plugin: Plugin) =
        { this with Plugins = this.Plugins @ [plugin] }

    member this.Process(file: MorphirFile) =
        this.Plugins
        |> List.fold (fun f plugin -> plugin.Transform f) file
```

**Phase 2: Plugin Interface**
```fsharp
type Plugin = {
    Name: string
    Configure: ProcessorContext -> unit
    Transform: MorphirFile -> MorphirFile
}
```

**Phase 3: Visitor Utilities**
```fsharp
module IR =
    let visit (test: IR -> bool) (visitor: IR -> IR) (tree: IR) : IR =
        // Depth-first preorder traversal
        // Apply visitor to matching nodes
        ...
```

---

## Summary

**Key Architectural Insights**:

1. **Three-Phase Pipeline**: Clear separation of parse, transform, stringify enables pluggability
2. **Processor as Interface**: Unified provides consistent API across ecosystems
3. **Plugin Composition**: Attacher + transformer pattern enables flexible configuration
4. **Frozen Processors**: Immutability ensures safe sharing and inheritance
5. **Bridge Pattern**: Cross-format transformations are first-class citizens
6. **Visitor Pattern**: Tree traversal is externalized to utilities
7. **VFile Pattern**: File metadata and diagnostics travel with content

**For Morphir-dotnet**:
- Adopt processor pattern for IR transformation pipeline
- Use plugin architecture for extensibility
- Implement VFile-style diagnostic tracking
- Create visitor utilities for IR traversal
- Support bridge plugins for IR version migration

**Next Steps**:
- Design `MorphirProcessor` API
- Implement plugin registry
- Create base transformers (optimization, validation)
- Build visitor utilities for Morphir IR
- Develop bridge plugins for cross-version migration

---

**Related Documents**:
- [Unist Specification](./unist-specification.md)
- [VFile Pattern](./vfile-pattern.md)
- [Unified to .NET Adaptation](./unified-to-dotnet-adaptation.md)
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
