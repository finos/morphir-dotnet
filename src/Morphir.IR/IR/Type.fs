module Morphir.IR.Type

open Morphir.IR.AccessControlled
open Morphir.IR.Name
open Morphir.IR.FQName

type Type<'A>
    = Variable of Attributes:'A * Name
    | Reference of Attributes:'A * FQName * Type<'A> list
    | Tuple of Attributes:'A * Type<'A> list
    | Record of Attributes:'A * Field<'A> list
    | ExtensibleRecord of  Attributes:'A * Name * Field<'A> list
    | Function of Attributes:'A * Type<'A> * Type<'A>
    | Unit of Attributes:'A
and Field<'A> =
    { Name: Name
      Type: Type<'A> }
and Constructor<'A>
    = Constructor of Name * (Name * Type<'A>) list
and Constructors<'A>
    = Constructor<'A> list
and Specification<'A>
    = TypeAliasSpecification of Name list * Type<'A>
    | OpaqueTypeSpecification of Name list
    | CustomTypeSpecification of Name list * Constructors<'A>
and Definition<'A>
    = TypeAliasDefinition of Name list * Type<'A>
    | CustomTypeDefinition of Name list * AccessControlled<Constructors<'A>>
