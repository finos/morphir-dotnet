namespace Morphir.Internal.CodeGeneration.Generators

open Myriad.Core

/// Generates visitor pattern for discriminated unions
///
/// Example input:
/// [<GenerateVisitor>]
/// type TypeExpr =
///     | TInt
///     | TString
///     | TFunc of input: TypeExpr * output: TypeExpr
///
/// Example output:
/// type TypeExprVisitor<'Result> = {
///     VisitTInt: unit -> 'Result
///     VisitTString: unit -> 'Result
///     VisitTFunc: input: TypeExpr -> output: TypeExpr -> 'Result
/// }
///
/// module TypeExpr =
///     let accept (visitor: TypeExprVisitor<'Result>) (expr: TypeExpr) : 'Result =
///         match expr with
///         | TInt -> visitor.VisitTInt()
///         | TString -> visitor.VisitTString()
///         | TFunc(input, output) -> visitor.VisitTFunc input output
[<MyriadGenerator("visitor")>]
type VisitorGenerator() =
    interface IMyriadGenerator with
        member _.ValidInputExtensions = seq { ".fs" }
        
        member _.Generate(_context: GeneratorContext) : Output =
            // TODO: Implement visitor generation
            Output.Ast []
