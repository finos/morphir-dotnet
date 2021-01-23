namespace Morphir.Lang.Elm

open System


module ElmAst =
    type Identifier = Identifier of Text:string
    type ModuleIdentifier = ModuleIdentifier of Text:string
    type ModuleName = ModuleName of Text:string
    type QualifiedModuleName = {Namespace:ModuleIdentifier List; Name:ModuleName}

    type ModulePath =
        | FullyQualifiedModulePath of QualifiedName:QualifiedModuleName
        | RelativeModulePath of QualifiedName:QualifiedModuleName
        | LocalModulePath of Name:ModuleName
        | RootModulePath of Name:ModuleName

    type ModuleDeclaration = ModuleDeclaration of Name:QualifiedModuleName
    type PortModuleDeclaration = PortModuleDeclaration of Name:QualifiedModuleName

    type ModuleKind =
        | Port
        | Default

    type ModuleDecl =
        | PortModule of PortModuleDeclaration
        | Module of ModuleDeclaration

    type Token<'a> = Token of Line:int64  * Column:int64 * Data:'a

    type ElmNode =
        | Module of Declaration:ModuleDeclaration
        | PortModule of Declaration:PortModuleDeclaration

    let moduleIdentifierToModuleName (ModuleIdentifier text) =
        ModuleName text

    let qualifiedModuleName (namespace_, name) =
        {Namespace = namespace_; Name = name}

    let rootModuleName name =
        {Namespace = List.empty; Name = name}
