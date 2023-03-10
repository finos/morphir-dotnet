module Morphir.IR.AccessControlled

open Morphir.SDK.Maybe

type AccessControlled<'a> = { access: Access; value: 'a }

and Access =
    | Public
    | Private

let inline mkPublic value = { access = Public; value = value }

let inline mkPrivate value = { access = Private; value = value }

let withPublicAccess ac =
    match ac.access with
    | Public -> Just ac.value
    | Private -> Nothing

let withPrivateAccess ac =
    match ac.access with
    | Public -> ac.value
    | Private -> ac.value

let map f ac =
    { access = ac.access
      value = f (ac.value) }
