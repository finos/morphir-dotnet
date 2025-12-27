# Pluggable Pipeline Architecture - Diagrams

**Task**: Task 2.2 - Pipeline Architecture Design (Issue #320)
**Created**: 2025-12-26
**Related**: ADR-026, API Design Document

## Table of Contents

1. [Pipeline Overview](#pipeline-overview)
2. [Component Architecture](#component-architecture)
3. [Plugin Execution Flow](#plugin-execution-flow)
4. [MorphirFile State Transitions](#morphirfile-state-transitions)
5. [Processor Freezing](#processor-freezing)
6. [Class Diagrams](#class-diagrams)
7. [Sequence Diagrams](#sequence-diagrams)

---

## 1. Pipeline Overview

### High-Level Architecture

```mermaid
graph LR
    A[Input File] --> B[Parser Phase]
    B --> C[IR Tree]
    C --> D[Transform Phase]
    D --> E[Modified IR]
    E --> F[Stringify Phase]
    F --> G[Output File]

    B -.->|diagnostics| H[MorphirFile]
    D -.->|diagnostics| H
    F -.->|diagnostics| H

    style H fill:#f9f,stroke:#333,stroke-width:2px
```

### Three-Phase Pipeline

```mermaid
flowchart TB
    subgraph Parse["🔍 Parse Phase"]
        P1[Parser 1] --> P2[Parser 2] --> P3[Parser N]
    end

    subgraph Transform["⚙️ Transform Phase"]
        T1[Plugin 1] --> T2[Plugin 2] --> T3[Plugin N]
    end

    subgraph Stringify["📝 Stringify Phase"]
        S1[Compiler 1] --> S2[Compiler 2] --> S3[Compiler N]
    end

    Input[Input File] --> Parse
    Parse --> IRTree[IR Tree]
    IRTree --> Transform
    Transform --> ModifiedIR[Modified IR]
    ModifiedIR --> Stringify
    Stringify --> Output[Output File]

    Parse -.->|Messages| Diag[MorphirFile Diagnostics]
    Transform -.->|Messages| Diag
    Stringify -.->|Messages| Diag
```

---

## 2. Component Architecture

### Core Components

```mermaid
graph TB
    subgraph Processor["MorphirProcessor"]
        direction TB
        PP[Parsers: Parser list]
        PL[Plugins: Plugin list]
        PC[Compilers: Compiler list]
        PF[Frozen: bool]
        PD[Data: Dictionary]
    end

    subgraph File["MorphirFile"]
        direction TB
        FC[Content: IRNode?]
        FP[Path: string?]
        FH[History: string list]
        FM[Messages: Message list]
        FD[Data: Dictionary]
    end

    subgraph Plugin["Plugin"]
        direction TB
        PN[Name: string]
        PCF[Configure: Processor → Processor]
        PT[Transform: Node → File → (Node?, File)]
    end

    Processor -->|processes| File
    Processor -->|contains| Plugin
    Plugin -->|transforms| File
```

### Type Relationships

```mermaid
classDiagram
    class IRNode {
        <<interface>>
    }

    class Type {
        <<abstract>>
    }

    class Value {
        <<abstract>>
    }

    class Definition {
        <<abstract>>
    }

    IRNode <|-- Type
    IRNode <|-- Value
    IRNode <|-- Definition

    Type <|-- Variable
    Type <|-- Reference
    Type <|-- Function
    Type <|-- Tuple
    Type <|-- Record

    Value <|-- Literal
    Value <|-- Constructor
    Value <|-- Apply
    Value <|-- Lambda

    Definition <|-- TypeAlias
    Definition <|-- CustomType
    Definition <|-- ValueDef
```

---

## 3. Plugin Execution Flow

### Plugin Chain Execution

```mermaid
sequenceDiagram
    participant Proc as MorphirProcessor
    participant P1 as Plugin 1
    participant P2 as Plugin 2
    participant PN as Plugin N
    participant File as MorphirFile

    Proc->>P1: Transform(node, file)
    activate P1
    P1->>File: Add diagnostics
    P1-->>Proc: (node', file')
    deactivate P1

    Proc->>P2: Transform(node', file')
    activate P2
    P2->>File: Add diagnostics
    P2-->>Proc: (node'', file'')
    deactivate P2

    Proc->>PN: Transform(node'', file'')
    activate PN
    PN->>File: Add diagnostics
    PN-->>Proc: (finalNode, finalFile)
    deactivate PN

    Proc-->>Proc: Return finalFile
```

### Plugin Transform Logic

```mermaid
flowchart TB
    Start([Plugin.Transform Called]) --> Match{Match<br/>Node Type?}

    Match -->|Yes| Validate[Validate Node]
    Match -->|No| Pass[Pass Through]

    Validate --> Valid{Valid?}
    Valid -->|Yes| Transform[Transform Node]
    Valid -->|No| Error[Add Error to File]

    Transform --> AddInfo[Add Info Message]
    AddInfo --> Return1([Return Some node, file])

    Error --> Return2([Return None, file])
    Pass --> Return3([Return Some node, file])

    style Error fill:#f99
    style Return2 fill:#f99
    style Return1 fill:#9f9
    style Return3 fill:#9f9
```

---

## 4. MorphirFile State Transitions

### Message Accumulation

```mermaid
stateDiagram-v2
    [*] --> Empty: Create

    Empty --> WithInfo: Info()
    WithInfo --> WithInfo: Info()

    WithInfo --> WithWarning: Warn()
    WithWarning --> WithWarning: Warn()
    WithWarning --> WithInfo: Info()

    WithWarning --> WithError: Error()
    WithError --> WithError: Error()
    WithError --> WithWarning: Warn()
    WithError --> WithInfo: Info()

    WithError --> WithFatal: Fail()
    WithFatal --> WithFatal: Any Message

    note right of WithFatal
        Pipeline should halt
        but continues to collect
        remaining diagnostics
    end note
```

### Severity Levels

```mermaid
graph TD
    subgraph Severity Hierarchy
        Info[Info<br/>ℹ️ Informational]
        Warn[Warning<br/>⚠️ Non-fatal issue]
        Error[Error<br/>❌ Fatal, continues]
        Fatal[Fatal<br/>💀 Fatal, should halt]
    end

    Info -.->|escalates to| Warn
    Warn -.->|escalates to| Error
    Error -.->|escalates to| Fatal

    style Info fill:#9cf
    style Warn fill:#fc9
    style Error fill:#f99
    style Fatal fill:#f33,color:#fff
```

---

## 5. Processor Freezing

### Frozen vs Unfrozen

```mermaid
stateDiagram-v2
    [*] --> Unfrozen: Create Empty

    Unfrozen --> Unfrozen: Add Parser
    Unfrozen --> Unfrozen: Add Plugin
    Unfrozen --> Unfrozen: Add Compiler

    Unfrozen --> Frozen: Freeze()

    Frozen --> Unfrozen: Add Parser<br/>(creates copy)
    Frozen --> Unfrozen: Add Plugin<br/>(creates copy)
    Frozen --> Unfrozen: Add Compiler<br/>(creates copy)

    Frozen --> Frozen: Process File<br/>(read-only)

    note right of Frozen
        Frozen processors are
        immutable templates.
        Modifications create
        unfrozen copies.
    end note
```

### Processor Variants

```mermaid
graph TB
    Base[Base Processor<br/>frozen] -->|copy + add plugin| V1[Variant 1<br/>unfrozen]
    Base -->|copy + add plugin| V2[Variant 2<br/>unfrozen]
    Base -->|copy + add plugin| V3[Variant 3<br/>unfrozen]

    V1 -->|freeze| V1F[Variant 1<br/>frozen]
    V2 -->|freeze| V2F[Variant 2<br/>frozen]
    V3 -->|freeze| V3F[Variant 3<br/>frozen]

    style Base fill:#9cf,stroke:#333,stroke-width:3px
    style V1F fill:#9cf
    style V2F fill:#9cf
    style V3F fill:#9cf
```

---

## 6. Class Diagrams

### MorphirFile Class Diagram

```mermaid
classDiagram
    class MorphirFile {
        +Content: IRNode?
        +Path: string?
        +History: string list
        +Messages: MorphirMessage list
        +Data: Dictionary~string, object~
        +Info(message) MorphirFile
        +Warn(message, position?) MorphirFile
        +Error(message, position?) MorphirFile
        +Fail(message, position?) MorphirFile
        +HasErrors() bool
        +HasFatals() bool
    }

    class MorphirMessage {
        +Severity: MessageSeverity
        +Message: string
        +Position: SourceRange?
        +Source: string?
        +RuleId: string?
    }

    class MessageSeverity {
        <<enumeration>>
        Info
        Warning
        Error
        Fatal
    }

    class SourceRange {
        +Start: SourcePosition
        +End: SourcePosition
    }

    class SourcePosition {
        +Line: int
        +Column: int
        +Offset: int?
    }

    MorphirFile "1" *-- "*" MorphirMessage
    MorphirMessage --> MessageSeverity
    MorphirMessage "1" o-- "0..1" SourceRange
    SourceRange "1" *-- "2" SourcePosition
```

### MorphirProcessor Class Diagram

```mermaid
classDiagram
    class MorphirProcessor {
        +Parsers: Parser list
        +Plugins: Plugin list
        +Compilers: Compiler list
        +Frozen: bool
        +Data: Dictionary~string, object~
        +Parse(parser) MorphirProcessor
        +Plugin(plugin) MorphirProcessor
        +Stringify(compiler) MorphirProcessor
        +Freeze() MorphirProcessor
        +Process(file) MorphirFile
    }

    class Parser {
        <<delegate>>
        +Invoke(file) Result~IRNode, string~
    }

    class Compiler {
        <<delegate>>
        +Invoke(node, file) MorphirFile
    }

    class Plugin {
        +Name: string
        +Configure(processor) MorphirProcessor
        +Transform(node, file) (IRNode?, MorphirFile)
    }

    class IPlugin {
        <<interface>>
        +Name string
        +Configure(processor) MorphirProcessor
        +Transform(node, file) (IRNode?, MorphirFile)
    }

    MorphirProcessor "1" *-- "*" Parser
    MorphirProcessor "1" *-- "*" Plugin
    MorphirProcessor "1" *-- "*" Compiler
    Plugin ..|> IPlugin : implements (C#)
```

---

## 7. Sequence Diagrams

### Full Pipeline Execution

```mermaid
sequenceDiagram
    actor User
    participant Proc as MorphirProcessor
    participant Parse as Parser
    participant P1 as Plugin1
    participant P2 as Plugin2
    participant Comp as Compiler
    participant File as MorphirFile

    User->>Proc: ProcessPath("input.json")
    Proc->>File: Create from path

    rect rgb(200, 220, 255)
    Note over Proc,Parse: Parse Phase
    Proc->>Parse: Parse(file)
    Parse->>File: Set Content(IRNode)
    Parse-->>Proc: file with content
    end

    rect rgb(255, 220, 200)
    Note over Proc,P2: Transform Phase
    Proc->>P1: Transform(node, file)
    P1->>File: Info("P1 processing")
    P1-->>Proc: (node', file')

    Proc->>P2: Transform(node', file')
    P2->>File: Info("P2 processing")
    P2-->>Proc: (node'', file'')
    end

    rect rgb(200, 255, 220)
    Note over Proc,Comp: Stringify Phase
    Proc->>Comp: Stringify(node'', file'')
    Comp->>File: Info("Compilation done")
    Comp-->>Proc: file'''
    end

    Proc-->>User: Return file'''
    User->>File: HasErrors()?
    File-->>User: false
```

### Error Accumulation

```mermaid
sequenceDiagram
    participant Proc as Processor
    participant P1 as Validator
    participant P2 as Optimizer
    participant File as MorphirFile

    Proc->>P1: Transform(node, file)
    alt Validation fails
        P1->>File: Error("Type not found")
        P1->>File: Error("Undefined variable")
        P1-->>Proc: (None, file with 2 errors)
    else Validation passes
        P1->>File: Info("Validation passed")
        P1-->>Proc: (node, file with info)
    end

    Note over Proc: Continue despite errors

    Proc->>P2: Transform(node, file)
    alt Has errors
        P2->>File: Info("Skipping optimization (errors present)")
        P2-->>Proc: (node, file)
    else No errors
        P2->>File: Info("Optimization complete")
        P2-->>Proc: (optimized, file)
    end

    Proc-->>Proc: Return file with all messages
```

### Processor Freezing Flow

```mermaid
sequenceDiagram
    actor Dev as Developer
    participant Base as Base Processor
    participant V1 as Variant 1
    participant V2 as Variant 2

    Dev->>Base: Create empty
    Dev->>Base: Add parser
    Dev->>Base: Add common plugins
    Dev->>Base: Freeze()

    Note over Base: Base is now frozen<br/>(immutable template)

    Dev->>Base: Add plugin (for V1)
    Base->>V1: Create unfrozen copy
    V1->>V1: Add plugin
    Dev->>V1: Freeze()

    Note over V1: Variant 1 frozen

    Dev->>Base: Add different plugin (for V2)
    Base->>V2: Create unfrozen copy
    V2->>V2: Add different plugin
    Dev->>V2: Freeze()

    Note over V2: Variant 2 frozen

    Note over Base,V2: All three can process files<br/>independently and safely
```

---

## Summary

This document provides comprehensive visual documentation of the pluggable pipeline architecture through:

1. **Pipeline Overview**: High-level flow and three-phase architecture
2. **Component Architecture**: Core components and type hierarchies
3. **Plugin Execution**: How plugins transform nodes and accumulate diagnostics
4. **MorphirFile States**: Message accumulation and severity levels
5. **Processor Freezing**: Immutable templates and variant creation
6. **Class Diagrams**: Detailed type structures and relationships
7. **Sequence Diagrams**: Runtime execution flows and interactions

These diagrams support the ADR-026 decision and API design document, providing visual reference for implementation.

---

**Related Documents**:
- ADR-026: Pluggable Pipeline Architecture
- API Design Document
- Task 2.1 Research: unified.js architecture

**Status**: Proposed
**Created**: 2025-12-26
