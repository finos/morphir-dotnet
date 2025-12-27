# VFile Pattern Knowledge Base

**Task**: Task 2.1 - Unified.js Architecture Research (Issue #316)
**Created**: 2025-12-26
**Purpose**: Understanding the VFile (virtual file) pattern for file metadata, messages, and diagnostic management in transformation pipelines

## Table of Contents

1. [VFile Overview](#vfile-overview)
2. [Core Data Structure](#core-data-structure)
3. [Message Management](#message-management)
4. [Error Reporting](#error-reporting)
5. [File Path Management](#file-path-management)
6. [Integration with Unified](#integration-with-unified)
7. [Morphir Adaptation](#morphir-adaptation)

---

## 1. VFile Overview

### 1.1 Purpose

From documentation:
> "VFile is a virtual file format for text processing used in @unifiedjs. It provides an API to access the file value, path, metadata about the file, and specifically supports attaching lint messages and errors to certain places in these files."

**Key Capabilities**:
- Track file content (string, Uint8Array, or undefined)
- Manage file paths and metadata
- Collect diagnostic messages (errors, warnings, info)
- Store arbitrary data for plugin communication
- Maintain file history

### 1.2 Design Philosophy

**Separation of Concerns**:
- Content: Raw file data
- Metadata: Path, encoding, stats
- Diagnostics: Messages with locations
- Plugin Data: Custom information

**Immutable Updates**:
- Path changes create history entries
- Content updates don't mutate existing references
- Message collection is append-only

**Position-Aware Diagnostics**:
- Messages attach to specific locations
- Line/column information preserved
- Source/ruleId tracking for tooling

### 1.3 Use Cases

**Text Processing Pipelines**:
- Parse markdown → Lint → Transform → Generate HTML
- Each phase adds messages, modifies content

**Multi-File Processing**:
- Track relationships between files
- Store dependency information in `data`

**Diagnostic Reporting**:
- Accumulate errors/warnings across transformations
- Format messages for user display

---

## 2. Core Data Structure

### 2.1 VFile Interface

```typescript
interface VFile {
  // Content
  value?: string | Uint8Array

  // Path information
  cwd: string              // Current working directory
  path?: string            // Full file path
  basename?: string        // Filename with extension
  stem?: string            // Filename without extension
  extname?: string         // File extension
  dirname?: string         // Parent directory

  // Metadata
  history: string[]        // All previous paths
  messages: VFileMessage[] // Diagnostic messages
  data: Data               // Plugin-specific data

  // Methods
  toString(encoding?: string): string
  message(reason: string, place?: Position, origin?: string): VFileMessage
  info(reason: string, place?: Position, origin?: string): VFileMessage
  fail(reason: string, place?: Position, origin?: string): VFileMessage
}
```

### 2.2 Construction

**From String**:
```javascript
import { VFile } from 'vfile'

const file = new VFile('Hello, world!')
// { value: 'Hello, world!', cwd: process.cwd(), history: [], messages: [] }
```

**From Object**:
```javascript
const file = new VFile({
  path: '~/example.txt',
  value: 'Alpha *bravo* charlie.',
  data: { matter: { title: 'Example' } }
})
```

**From Existing VFile**:
```javascript
const copy = new VFile(existingFile)
// Shallow copy with shared references
```

**From URL**:
```javascript
const file = new VFile(new URL('file:///path/to/file.txt'))
```

### 2.3 Field Relationships

**Path Hierarchy**:
```
path = dirname / basename
basename = stem + extname
```

**Setting Effects**:
```javascript
const file = new VFile({ path: '/home/user/document.txt' })

// Setting path:
file.path = '/home/user/report.md'
// Updates: basename, stem, extname, dirname, history

// Setting basename:
file.basename = 'summary.html'
// Updates: path, stem, extname

// Setting stem:
file.stem = 'overview'
// Updates: path, basename

// Setting extname:
file.extname = '.json'
// Updates: path, basename
```

**History Tracking**:
```javascript
const file = new VFile({ path: '~/example.txt' })
file.extname = '.md'
console.log(file.history)
// ['~/example.txt', '~/example.md']
```

---

## 3. Message Management

### 3.1 VFileMessage Structure

```typescript
interface VFileMessage {
  reason: string        // Human-readable description
  fatal?: boolean       // true=error, false=warning, undefined=info
  line?: number        // 1-indexed line number
  column?: number      // 1-indexed column number
  place?: Position     // Detailed position (unist format)
  source?: string      // Tool that generated message
  ruleId?: string      // Specific rule identifier
  file?: string        // File path
  actual?: string      // Actual text at location
  expected?: string[]  // Expected values
  url?: string         // Documentation URL
  note?: string        // Additional context
}
```

### 3.2 Message Creation

**Warning** (fatal=false):
```javascript
const file = new VFile({ path: 'example.md', value: 'Some content' })

const message = file.message(
  'Unexpected unknown word braavo, did you mean bravo?',
  { line: 1, column: 8 },
  'spell'
)

message.fatal
// => false

message.toString()
// => 'example.md:1:8: Unexpected unknown word braavo, did you mean bravo?'
```

**Info** (fatal=undefined):
```javascript
const infoMsg = file.info(
  'This file has 3 headings',
  undefined,
  'heading-counter'
)

infoMsg.fatal
// => undefined
```

**Error** (fatal=true):
```javascript
try {
  file.fail('Invalid syntax', { line: 5, column: 10 }, 'parser')
} catch (error) {
  console.log(error.fatal)
  // => true
}
```

### 3.3 Position Formats

**Point** (simple):
```javascript
file.message('Warning', { line: 1, column: 8 })
```

**Position** (range):
```javascript
file.message('Warning', {
  start: { line: 1, column: 8 },
  end: { line: 1, column: 13 }
})
```

**Node** (unist node):
```javascript
const node = {
  type: 'text',
  value: 'example',
  position: {
    start: { line: 1, column: 5 },
    end: { line: 1, column: 12 }
  }
}

file.message('Node issue', node)
```

### 3.4 Source and Rule Tracking

```javascript
file.message(
  'Line too long',
  { line: 10, column: 81 },
  'remark-lint:maximum-line-length'
)

// Later parsed as:
// source: 'remark-lint'
// ruleId: 'maximum-line-length'
```

**Benefits**:
- Enable/disable specific rules
- Filter messages by source
- Link to rule documentation

---

## 4. Error Reporting

### 4.1 Severity Levels

**Info** (fatal=undefined):
- Informational messages
- Statistics, metrics
- Non-actionable observations

```javascript
file.info('Document contains 5 links')
```

**Warning** (fatal=false):
- Potential issues
- Style violations
- Linting suggestions

```javascript
file.message('Heading should use sentence case', node, 'lint:heading-style')
```

**Error** (fatal=true):
- Syntax errors
- Invalid structure
- Processing failures

```javascript
file.fail('Unexpected end of input', { line: 50, column: 1 }, 'parser')
```

### 4.2 Message Formatting

**Default Format**:
```javascript
const msg = file.message('Problem', { line: 5, column: 10 })
console.log(String(msg))
// => '5:10: Problem'

// With file path
file.path = 'example.md'
console.log(String(msg))
// => 'example.md:5:10: Problem'
```

**Custom Formatting** (with vfile-reporter):
```javascript
import { reporter } from 'vfile-reporter'

const files = [file1, file2, file3]
console.log(reporter(files))
```

Output:
```
example.md
  5:10  warning  Problem  source:rule

⚠ 1 warning
```

### 4.3 Error Handling Patterns

**Collect and Report**:
```javascript
function processFiles(files) {
  const results = []

  for (const file of files) {
    try {
      const processed = processor.processSync(file)
      results.push(processed)
    } catch (error) {
      // Error already attached to file.messages
      results.push(file)
    }
  }

  // Check for fatal errors
  const failed = results.filter(f =>
    f.messages.some(m => m.fatal)
  )

  if (failed.length > 0) {
    console.error(reporter(failed))
    process.exit(1)
  }

  return results
}
```

**Early Exit on Error**:
```javascript
function strictProcessor() {
  return (tree, file) => {
    visit(tree, 'link', (node) => {
      if (!node.url) {
        file.fail('Missing URL in link', node)
      }
    })
  }
}
```

---

## 5. File Path Management

### 5.1 Path Properties

**Current Working Directory**:
```javascript
const file = new VFile()
console.log(file.cwd)
// => process.cwd() or '/'
```

**Full Path**:
```javascript
file.path = '/home/user/documents/report.md'
console.log(file.path)
// => '/home/user/documents/report.md'
```

**Basename** (filename with extension):
```javascript
console.log(file.basename)
// => 'report.md'
```

**Stem** (filename without extension):
```javascript
console.log(file.stem)
// => 'report'
```

**Extension**:
```javascript
console.log(file.extname)
// => '.md'
```

**Directory**:
```javascript
console.log(file.dirname)
// => '/home/user/documents'
```

### 5.2 Path Manipulation

**Change Extension**:
```javascript
const file = new VFile({ path: 'input.md' })
file.extname = '.html'
console.log(file.path)
// => 'input.html'
```

**Change Directory**:
```javascript
file.dirname = '/output'
console.log(file.path)
// => '/output/input.html'
```

**Rename File**:
```javascript
file.basename = 'index.html'
console.log(file.path)
// => '/output/index.html'
```

### 5.3 History Tracking

```javascript
const file = new VFile({ path: 'draft.txt' })

file.path = 'draft.md'
file.path = 'final.md'
file.path = 'published.md'

console.log(file.history)
// => ['draft.txt', 'draft.md', 'final.md', 'published.md']
```

**Use Cases**:
- Track file transformations
- Generate source maps
- Debug processing pipeline

---

## 6. Integration with Unified

### 6.1 Plugin Communication via Data

```javascript
// Plugin 1: Store metadata
function frontmatterPlugin() {
  return (tree, file) => {
    const matter = extractFrontmatter(tree)
    file.data.matter = matter
  }
}

// Plugin 2: Use metadata
function titlePlugin() {
  return (tree, file) => {
    const title = file.data.matter?.title
    if (title) {
      // Use title for something
    }
  }
}
```

### 6.2 Message Accumulation Across Plugins

```javascript
function plugin1() {
  return (tree, file) => {
    file.message('Style issue', node, 'lint:style')
  }
}

function plugin2() {
  return (tree, file) => {
    file.message('Broken link', node, 'lint:links')
  }
}

const processor = unified()
  .use(plugin1)
  .use(plugin2)

const result = await processor.process(file)
console.log(result.messages.length)
// => 2 (accumulated from both plugins)
```

### 6.3 File as Pipeline State

```javascript
function remarkToRehype() {
  return (mdast, file) => {
    // Store original mdast
    file.data.mdast = mdast

    // Convert to hast
    const hast = toHast(mdast)

    // Store conversion metadata
    file.data.conversion = {
      from: 'mdast',
      to: 'hast',
      timestamp: Date.now()
    }

    return hast
  }
}
```

---

## 7. Morphir Adaptation

### 7.1 MorphirFile Structure

**Proposed F# Implementation**:
```fsharp
type SourcePosition = {
    Line: int           // 1-indexed
    Column: int         // 1-indexed
    Offset: int option  // 0-indexed
}

type SourceRange = {
    Start: SourcePosition
    End: SourcePosition
}

type MessageSeverity =
    | Info
    | Warning
    | Error

type MorphirMessage = {
    Reason: string
    Severity: MessageSeverity
    Position: SourceRange option
    Source: string option
    RuleId: string option
    Note: string option
}

type MorphirFile = {
    // Content
    Content: IR option

    // Path information
    Cwd: string
    Path: string option
    History: string list

    // Diagnostics
    Messages: MorphirMessage list

    // Plugin data
    Data: Map<string, obj>
}
```

### 7.2 Message Creation API

```fsharp
module MorphirFile =
    let create (content: IR option) =
        {
            Content = content
            Cwd = System.IO.Directory.GetCurrentDirectory()
            Path = None
            History = []
            Messages = []
            Data = Map.empty
        }

    let withPath (path: string) (file: MorphirFile) =
        { file with
            Path = Some path
            History = file.History @ [path]
        }

    let message
        (reason: string)
        (severity: MessageSeverity)
        (position: SourceRange option)
        (source: string option)
        (file: MorphirFile)
        : MorphirFile =

        let msg = {
            Reason = reason
            Severity = severity
            Position = position
            Source = source
            RuleId = None
            Note = None
        }

        { file with Messages = file.Messages @ [msg] }

    let info reason position source file =
        message reason Info position source file

    let warn reason position source file =
        message reason Warning position source file

    let error reason position source file =
        message reason Error position source file

    let fail reason position source file =
        let updated = error reason position source file
        failwithf "Error: %s" reason
```

### 7.3 Integration with Morphir Pipeline

```fsharp
type TransformPlugin = {
    Name: string
    Transform: MorphirFile -> MorphirFile
}

type MorphirProcessor = {
    Plugins: TransformPlugin list
}

module MorphirProcessor =
    let create () = { Plugins = [] }

    let use (plugin: TransformPlugin) (processor: MorphirProcessor) =
        { processor with Plugins = processor.Plugins @ [plugin] }

    let process (file: MorphirFile) (processor: MorphirProcessor) =
        processor.Plugins
        |> List.fold (fun f plugin ->
            try
                plugin.Transform f
            with ex ->
                f |> MorphirFile.error ex.Message None (Some plugin.Name)
        ) file
```

### 7.4 Example Plugin

```fsharp
let typeValidationPlugin = {
    Name = "type-validation"
    Transform = fun file ->
        match file.Content with
        | None -> file
        | Some ir ->
            let validateType pos typ =
                // Validation logic
                match typ with
                | Type.Variable(_, name) when not (isValidName name) ->
                    file
                    |> MorphirFile.error
                        (sprintf "Invalid type variable name: %s" (Name.toString name))
                        (Some pos)
                        (Some "type-validation")
                | _ -> file

            // Visit all types in IR
            IR.visit validateType ir
            file
}
```

### 7.5 C# Implementation

```csharp
public record SourcePosition(int Line, int Column, int? Offset = null);

public record SourceRange(SourcePosition Start, SourcePosition End);

public enum MessageSeverity { Info, Warning, Error }

public record MorphirMessage(
    string Reason,
    MessageSeverity Severity,
    SourceRange? Position = null,
    string? Source = null,
    string? RuleId = null,
    string? Note = null
);

public record MorphirFile(
    IR? Content,
    string Cwd,
    string? Path,
    ImmutableList<string> History,
    ImmutableList<MorphirMessage> Messages,
    ImmutableDictionary<string, object> Data
)
{
    public static MorphirFile Create(IR? content) =>
        new(
            Content: content,
            Cwd: Directory.GetCurrentDirectory(),
            Path: null,
            History: ImmutableList<string>.Empty,
            Messages: ImmutableList<MorphirMessage>.Empty,
            Data: ImmutableDictionary<string, object>.Empty
        );

    public MorphirFile WithPath(string path) =>
        this with
        {
            Path = path,
            History = History.Add(path)
        };

    public MorphirFile AddMessage(MorphirMessage message) =>
        this with { Messages = Messages.Add(message) };

    public MorphirFile Info(string reason, SourceRange? position = null, string? source = null) =>
        AddMessage(new MorphirMessage(reason, MessageSeverity.Info, position, source));

    public MorphirFile Warn(string reason, SourceRange? position = null, string? source = null) =>
        AddMessage(new MorphirMessage(reason, MessageSeverity.Warning, position, source));

    public MorphirFile Error(string reason, SourceRange? position = null, string? source = null) =>
        AddMessage(new MorphirMessage(reason, MessageSeverity.Error, position, source));

    public MorphirFile Fail(string reason, SourceRange? position = null, string? source = null)
    {
        var updated = Error(reason, position, source);
        throw new InvalidOperationException(reason);
    }
}
```

### 7.6 Benefits for Morphir

**Diagnostic Accumulation**:
```csharp
var file = MorphirFile.Create(ir)
    .Warn("Unused import", position, "import-checker")
    .Info("Function complexity: 5", position, "complexity")
    .Error("Type mismatch", position, "type-checker");

// All messages preserved for reporting
Console.WriteLine($"{file.Messages.Count} diagnostics");
```

**Pipeline State**:
```csharp
var file = MorphirFile.Create(ir);

// Plugin 1: Store type environment
file = file with {
    Data = file.Data.Add("typeEnv", typeEnvironment)
};

// Plugin 2: Use type environment
var typeEnv = (TypeEnvironment)file.Data["typeEnv"];
```

**Path Tracking**:
```csharp
var file = MorphirFile.Create(ir)
    .WithPath("input.morphir")
    .WithPath("validated.morphir")
    .WithPath("optimized.morphir");

// History: ["input.morphir", "validated.morphir", "optimized.morphir"]
```

---

## Summary

**Key Insights from VFile**:

1. **Separation of Concerns**: Content, metadata, diagnostics, plugin data
2. **Message Management**: Structured error/warning/info with positions
3. **Path Tracking**: History of transformations
4. **Plugin Communication**: Shared `data` object
5. **Immutable Updates**: Path changes create history

**For Morphir-dotnet**:

**Adopt**:
- ✅ Structured message format with severity levels
- ✅ Position tracking with line/column/offset
- ✅ History tracking for file transformations
- ✅ Plugin data sharing via Map/Dictionary

**Adapt**:
- ⚠️ String content → IR content
- ⚠️ JavaScript API → F#/C# idiomatic API
- ⚠️ Mutable messages list → Immutable list
- ⚠️ Source/ruleId parsing → Explicit fields

**Benefits**:
- Clear diagnostic reporting
- Pipeline state management
- Traceability through transformations
- Type-safe message handling

**Implementation Priority**:
1. MorphirFile record with Content/Messages/Data
2. Message creation API (Info/Warn/Error/Fail)
3. Position tracking with SourceRange
4. Integration with transformation pipeline

---

**Related Documents**:
- [Unified.js Architecture](./unified-js-architecture.md)
- [Unist Specification](./unist-specification.md)
- [Unified to .NET Adaptation](./unified-to-dotnet-adaptation.md)
- [Visitor Pattern Implementations](./visitor-pattern-implementations.md)
