# Custom Myriad Plugins

Guide to creating custom Myriad plugins for morphir-dotnet.

## When to Create a Plugin

**Threshold:** 5+ types with same pattern

**Good Candidates:**
- JSON codecs for IR types
- Visitor patterns for IR traversal
- Lenses for nested updates

## Plugin Structure

```fsharp
module MyPlugin

open Myriad.Core
open FSharp.Compiler.SyntaxTree

[<MyriadGenerator("my-plugin")>]
type MyGenerator() =
    interface IMyriadGenerator with
        member _.Generate(context: GeneratorContext) : Output =
            // 1. Parse input AST
            let types = Myriad.Core.Ast.extractTypeDefn context.InputFilename
            
            // 2. Generate code
            let generated = generateForTypes types
            
            // 3. Return AST
            Output.Ast generated
```

## MSBuild Integration

```xml
<ItemGroup>
  <Compile Include="IR/Type.fs">
    <MyriadFile>true</MyriadFile>
    <MyriadNameSpace>Morphir.IR.Type.Generated</MyriadNameSpace>
  </Compile>
</ItemGroup>
```

## Usage

```fsharp
[<MyPlugin>]
type MyType = ...
```

## Scaffolding

```bash
dotnet fsi .claude/skills/elm-to-fsharp-guru/scripts/generate-myriad-plugin.fsx MyPlugin
```

## References

- [Myriad Plugin Guide](https://moiraesoftware.github.io/myriad/how-to/Create-Plugins.html)
- [Template](../templates/myriad-plugin.fs)

## History

**Version:** 1.0  
**Created:** 2025-12-21
