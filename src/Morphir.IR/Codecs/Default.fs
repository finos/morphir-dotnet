[<AutoOpen>]
module Morphir.Codecs.Default

open Json.Decode
open Morphir
open Morphir.IR
open Morphir.IR.FQName
open Morphir.IR.Name
open Morphir.IR.Path
open Morphir.IR.QName
open Morphir.IR.Type
open Morphir.IR.Codecs
open Json

let inline encodeUnit () = Encode.object []
let decodeUnit: Decoder<unit> = Decode.succeed ()

let inline encodeName (name: Name) = Name.encoder name

let decodeName: Decoder<Name> = Name.decoder

let inline encodePath (path: Path) = Path.encoder path

let inline decodePath path = Path.decoder path

let inline encodeQName (qname: QName) = QName.encoder qname

let decodeQName: Decoder<QName> = QName.decoder

let inline encodeFQName (fqname: FQName) = FQName.encoder fqname

let decodeFQName: Decoder<FQName> = FQName.decoder

let rec inline encodeType (encodeAttributes: 'a -> Encode.Value) (tpe: Type<'a>) : Encode.Value =
    Type.encoder encodeAttributes tpe

let rec inline decodeType (decodeAttributes: Decode.Decoder<'a>) : Decode.Decoder<Type.Type<'a>> =
    Type.decoder decodeAttributes
