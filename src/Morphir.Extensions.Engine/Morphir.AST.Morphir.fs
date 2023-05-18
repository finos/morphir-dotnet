namespace Morphir.AST.Morphir

type EntityPath = SourcePath of string

type EntityRef = { FullName: string; Path: EntityPath }

type ModuleDecl = { Name: string; Entity: EntityRef }
