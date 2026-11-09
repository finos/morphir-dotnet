// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

open Fantomas.Core
open Fantomas.FCS.Syntax
open Fantomas.FCS.Text

/// Reads the untyped syntax tree of an input file into the generator model.
module Syntax =
    let private location (range: range) = { Line = range.StartLine; Column = range.StartColumn + 1 }

    let private names (ids: LongIdent) = ids |> List.map (fun id -> id.idText)

    let private shortAttributeName (ids: LongIdent) =
        match List.tryLast ids with
        | Some id when id.idText.EndsWith "Attribute" && id.idText.Length > "Attribute".Length -> id.idText.Substring(0, id.idText.Length - "Attribute".Length)
        | Some id -> id.idText
        | None -> ""

    let rec private typeSyntax (synType: SynType) : TypeSyntax =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = ids)) -> TypeSyntax.Named(names ids, [])
        | SynType.App(typeName = SynType.LongIdent(SynLongIdent(id = ids)); typeArgs = args) -> TypeSyntax.Named(names ids, args |> List.map typeSyntax)
        | SynType.Array(rank = 1; elementType = element) -> TypeSyntax.Array(typeSyntax element)
        | SynType.Tuple(isStruct = false; path = segments) ->
            segments
            |> List.choose (function
                | SynTupleTypeSegment.Type t -> Some(typeSyntax t)
                | _ -> None)
            |> TypeSyntax.Tuple
        | SynType.Paren(innerType = inner) -> typeSyntax inner
        | SynType.Var(typar = SynTypar(ident = id)) -> TypeSyntax.Variable("'" + id.idText)
        | SynType.WithNull(innerType = inner) -> TypeSyntax.WithNull(typeSyntax inner)
        | other -> TypeSyntax.Unsupported(string other.Range)

    let rec private attributeValue (expr: SynExpr) : AttributeValue =
        match expr with
        | SynExpr.Paren(expr = inner) -> attributeValue inner
        | SynExpr.Const(constant = SynConst.String(text = text)) -> AttributeValue.Text text
        | SynExpr.Const(constant = SynConst.Bool value) -> AttributeValue.Bool value
        | SynExpr.Const(constant = SynConst.Int32 value) -> AttributeValue.Int value
        | SynExpr.Const(constant = SynConst.SByte value) -> AttributeValue.Number(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.Byte value) -> AttributeValue.Number(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.Int16 value) -> AttributeValue.Number(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.UInt16 value) -> AttributeValue.Number(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.UInt32 value) -> AttributeValue.Number(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.Int64 value) -> AttributeValue.Number(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.UInt64 value) -> AttributeValue.Number(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.Single value) when System.Single.IsFinite value -> AttributeValue.Number(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Const(constant = SynConst.Double value) when System.Double.IsFinite value -> AttributeValue.Number(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
        | SynExpr.Ident id -> AttributeValue.Names [ id.idText ]
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) -> AttributeValue.Names [ String.concat "." (names ids) ]
        | SynExpr.App(isInfix = false; funcExpr = SynExpr.App(isInfix = true; funcExpr = op; argExpr = left); argExpr = right)
        | SynExpr.App(funcExpr = SynExpr.App(funcExpr = op; argExpr = left); argExpr = right) when
            (match op with
             | SynExpr.LongIdent(longDotId = SynLongIdent(id = [ id ]))
             | SynExpr.Ident id -> id.idText = "op_BitwiseOr"
             | _ -> false)
            ->
            match attributeValue left, attributeValue right with
            | AttributeValue.Names l, AttributeValue.Names r -> AttributeValue.Names(l @ r)
            | _ -> AttributeValue.Other
        | SynExpr.App(funcExpr = SynExpr.Ident id; argExpr = argument) when id.idText = "nameof" ->
            match attributeValue argument with
            | AttributeValue.Names [ name ] -> AttributeValue.Text(name.Split('.') |> Array.last)
            | _ -> AttributeValue.Other
        | SynExpr.TypeApp(expr = SynExpr.Ident id; typeArgs = [ t ]) when id.idText = "typeof" ->
            match typeSyntax t with
            | TypeSyntax.Named(n, []) -> AttributeValue.TypeOf(String.concat "." n)
            | _ -> AttributeValue.Other
        | _ -> AttributeValue.Other

    /// Splits <c>Name = value</c> from a positional argument.
    let private namedArgument (expr: SynExpr) =
        match expr with
        | SynExpr.App(funcExpr = SynExpr.App(isInfix = true; funcExpr = op; argExpr = SynExpr.Ident name); argExpr = value) when
            (match op with
             | SynExpr.LongIdent(longDotId = SynLongIdent(id = [ id ]))
             | SynExpr.Ident id -> id.idText = "op_Equality"
             | _ -> false)
            ->
            Some(name.idText, attributeValue value)
        | _ -> None

    let private attributeUse (attribute: SynAttribute) : AttributeUse =
        let arguments =
            match attribute.ArgExpr with
            | SynExpr.Paren(expr = SynExpr.Tuple(exprs = exprs)) -> exprs
            | SynExpr.Paren(expr = SynExpr.Const(constant = SynConst.Unit)) -> []
            | SynExpr.Const(constant = SynConst.Unit) -> []
            | SynExpr.Paren(expr = inner) -> [ inner ]
            | other -> [ other ]

        let named = arguments |> List.choose namedArgument

        {
            Name = shortAttributeName attribute.TypeName.LongIdent
            Positional = arguments |> List.filter (namedArgument >> Option.isNone) |> List.map attributeValue
            Named = named
            Location = location attribute.Range
        }

    let private attributes (lists: SynAttributes) =
        lists |> List.collect (fun list -> list.Attributes) |> List.map attributeUse

    let private field (SynField(attributes = attrs; idOpt = id; fieldType = fieldType; range = range)) : FieldSyntax =
        {
            Name = id |> Option.map (fun id -> id.idText)
            Type = typeSyntax fieldType
            Attributes = attributes attrs
            Location = location range
        }

    let private isPrivate (access: SynAccess option) =
        match access with
        | Some(SynAccess.Private _)
        | Some(SynAccess.Internal _) -> true
        | _ -> false

    let private shape (repr: SynTypeDefnRepr) : TypeShape =
        match repr with
        | SynTypeDefnRepr.Simple(simpleRepr = simple) ->
            match simple with
            | SynTypeDefnSimpleRepr.Record(accessibility = access; recordFields = fields) -> TypeShape.Record(fields |> List.map field, isPrivate access)
            | SynTypeDefnSimpleRepr.Union(accessibility = access; unionCases = cases) ->
                let case (SynUnionCase(attributes = attrs; ident = SynIdent(ident = id); caseType = kind; range = range)) =
                    {
                        Name = id.idText
                        Fields =
                            match kind with
                            | SynUnionCaseKind.Fields fields -> fields |> List.map field
                            | SynUnionCaseKind.FullType _ -> []
                        Attributes = attributes attrs
                        Location = location range
                    }

                TypeShape.Union(cases |> List.map case, isPrivate access)
            | SynTypeDefnSimpleRepr.Enum(cases = cases) ->
                cases
                |> List.map (fun (SynEnumCase(ident = SynIdent(ident = id); valueExpr = value)) ->
                    let number =
                        match value with
                        | SynExpr.Const(constant = SynConst.Int32 n) -> int64 n
                        | SynExpr.Const(constant = SynConst.Int64 n) -> n
                        | SynExpr.Const(constant = SynConst.Byte n) -> int64 n
                        | _ -> 0L

                    id.idText, number)
                |> TypeShape.Enum
            | SynTypeDefnSimpleRepr.TypeAbbrev(rhsType = rhs) -> TypeShape.Abbreviation(typeSyntax rhs)
            | _ -> TypeShape.Other
        | _ -> TypeShape.Other

    let private declaration (path: string list) (SynTypeDefn(typeInfo = info; typeRepr = repr; range = range)) =
        let (SynComponentInfo(attributes = attrs; typeParams = typars; longId = ids; accessibility = access)) = info

        let parameters =
            match typars with
            | Some decls -> decls.TyparDecls |> List.map (fun (SynTyparDecl(typar = SynTypar(ident = id))) -> "'" + id.idText)
            | None -> []

        {
            Name = (List.last ids).idText
            Path = path
            Parameters = parameters
            Shape = shape repr
            Attributes = attributes attrs
            IsPrivate = isPrivate access
            Location = location range
        }

    let rec private opens (decls: SynModuleDecl list) : string list =
        decls
        |> List.collect (function
            | SynModuleDecl.Open(target = SynOpenDeclTarget.ModuleOrNamespace(longId = SynLongIdent(id = ids))) -> [ String.concat "." (names ids) ]
            | SynModuleDecl.Open(target = SynOpenDeclTarget.Type(typeName = synType)) ->
                match typeSyntax synType with
                | TypeSyntax.Named(name, []) -> [ "type " + String.concat "." name ]
                | _ -> []
            | SynModuleDecl.NestedModule(decls = inner) -> opens inner
            | _ -> [])

    let rec private declarations (path: string list) (decls: SynModuleDecl list) : TypeDeclaration list =
        decls
        |> List.collect (function
            | SynModuleDecl.Types(typeDefns = defns) -> defns |> List.map (declaration path)
            | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = ids); decls = inner) -> declarations (path @ names ids) inner
            | _ -> [])

    let private read' (file: string) (input: ParsedInput) =
        match input with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = contents)) ->
            let scopes =
                contents
                |> List.choose (fun (SynModuleOrNamespace(longId = ids; kind = kind)) ->
                    match kind with
                    | SynModuleOrNamespaceKind.DeclaredNamespace
                    | SynModuleOrNamespaceKind.NamedModule -> Some(String.concat "." (names ids))
                    | SynModuleOrNamespaceKind.AnonModule
                    | SynModuleOrNamespaceKind.GlobalNamespace -> None)

            let types =
                contents
                |> List.collect (fun (SynModuleOrNamespace(longId = ids; kind = kind; decls = decls)) ->
                    let path =
                        match kind with
                        | SynModuleOrNamespaceKind.GlobalNamespace -> []
                        | _ -> names ids

                    declarations path decls)

            let opened =
                contents
                |> List.collect (fun (SynModuleOrNamespace(decls = decls)) -> opens decls)
                |> List.distinct

            { File = file; Scopes = scopes; Opens = opened; Types = types }
        | ParsedInput.SigFile _ -> { File = file; Scopes = []; Opens = []; Types = [] }

    /// Parses F# source text. A syntax error is a generation error that names the file.
    let read (file: string) (source: string) : Result<SourceFile, GenerationError list> =
        try
            match CodeFormatter.ParseAsync(false, source) |> Async.RunSynchronously |> Array.tryHead with
            | Some(input, _) -> Ok(read' file input)
            | None -> Error [ GenerationError.ParseFailed(file, "the parser returned no syntax tree.") ]
        with ex ->
            Error [ GenerationError.ParseFailed(file, ex.Message) ]
