// Run from the repository root after a Release build of tests/Morphir.Lang.CSharp.Tests:
// dotnet fsi tests/Morphir.BlackBox.Tests/Fixtures/Models/Shapes/make-shapes.fsx tests/Morphir.BlackBox.Tests/Fixtures/Models/Shapes/shapes-v4.json
#r "../../../../Morphir.Lang.CSharp.Tests/bin/Release/net11.0/Morphir.Models.dll"
open Morphir.IR
open Morphir.IR.V4
let name (t: string) = match CanonicalNames.parseName t with Ok n -> n | Error m -> failwith m
let pathOf (t: string) = match CanonicalNames.parsePath t with Ok n -> n | Error m -> failwith m
let fq (t: string) = match CanonicalNames.parseFQName t with Ok n -> n | Error m -> failwith m
let ta = TypeAttributes.empty
let ordered e = match OrderedMap.ofList e with Ok m -> m | Error k -> failwith "dup"
let intT = Type.Reference(ta, fq "morphir/SDK:basics#int", [])
let own l = Type.Reference(ta, fq ("acme/shapes:shapes#" + l), [])
let recordT fields = Type.Record(ta, [ for n, t in fields -> name n, t ])
let pub d : AccessControlled<Documented<TypeDefinition>> = { Access = Public; Value = { Doc = None; Value = d } }
let alias body = TypeAliasDefinition([], body)
let custom cases = CustomTypeDefinition([], Public, ordered [ for c, fs in cases -> name c, [ for f, t in fs -> name f, t ] ])
let m : ModuleDefinition =
  { Types = ordered [ name "circle", pub (alias (recordT [ "radius", intT ]))
                      name "rect", pub (alias (recordT [ "width", intT; "height", intT ]))
                      name "shape", pub (custom [ "circle", [ "circle", own "circle" ]; "rect", [ "rect", own "rect" ] ])
                      name "size", pub (custom [ "small", []; "large", [] ]) ]
    Values = ordered []; Doc = None }
let pkg : PackageDefinition = { Modules = ordered [ ModulePath(pathOf "shapes"), ({ Access = Public; Value = m } : AccessControlled<ModuleDefinition>) ] }
let dist = Library(PackageName(pathOf "acme/shapes"), OrderedMap.empty, pkg)
match Morphir.Json.V4.V4JsonWriter.writeDistribution true dist with
| Ok bytes -> System.IO.File.WriteAllBytes(fsi.CommandLineArgs[1], bytes)
| Error e -> failwithf "%A" e
