namespace Morphir.IR.Pipeline

open System
open System.Collections.Immutable

/// <summary>Identifies a stored value by property name and value type.</summary>
[<Struct; NoComparison>]
type internal PropertyKey = { Name: string; ValueType: Type }

/// <summary>
/// A property paired with a value, without the value type, so that one list can hold values of
/// different types. Create one with <c>property.With</c> or <c>Property.valueOf</c>.
/// </summary>
[<Sealed>]
type PropertyValue internal (key: PropertyKey, value: obj) =
    member internal _.Key = key
    member internal _.Value = value

/// <summary>
/// A named property that carries its value type. Values in a property bag can only be stored and
/// read through a property. Two properties refer to the same value when both their name and value
/// type match. Use a dotted name with a library prefix, such as <c>morphir.ir.version</c>.
/// </summary>
[<Sealed>]
type Property<'T> internal (name: string) =
    /// <summary>The property name.</summary>
    member _.Name = name
    member internal _.Key = { Name = name; ValueType = typeof<'T> }
    /// <summary>Pairs the property with a value.</summary>
    /// <param name="value">The value</param>
    member this.With(value: 'T) : PropertyValue = PropertyValue(this.Key, box value)

/// <summary>
/// An immutable set of typed property values. Bags are equal when they hold the same properties
/// with equal values.
/// </summary>
[<Sealed>]
type PropertyBag internal (items: ImmutableDictionary<PropertyKey, obj>) =
    member internal _.Items = items

    /// <summary>The number of stored values.</summary>
    member _.Count = items.Count

    override _.Equals(other: obj) =
        match other with
        | :? PropertyBag as bag ->
            items.Count = bag.Items.Count
            && items
               |> Seq.forall (fun item ->
                   match bag.Items.TryGetValue item.Key with
                   | true, value -> Object.Equals(item.Value, value)
                   | false, _ -> false)
        | _ -> false

    override _.GetHashCode() =
        // Exclusive or, so the result does not depend on enumeration order.
        items
        |> Seq.fold
            (fun hash item ->
                let valueHash = if isNull item.Value then 0 else item.Value.GetHashCode()
                hash ^^^ HashCode.Combine(item.Key.Name, item.Key.ValueType, valueHash))
            0

/// <summary>Functions for creating properties.</summary>
[<RequireQualifiedAccess>]
module Property =
    /// <summary>Creates a property.</summary>
    /// <param name="name">The property name</param>
    let create<'T> (name: string) : Property<'T> = Property<'T>(name)

    /// <summary>Pairs a property with a value.</summary>
    /// <param name="property">The property</param>
    /// <param name="value">The value</param>
    let valueOf (property: Property<'T>) (value: 'T) : PropertyValue = property.With value

/// <summary>
/// Functions for property bags. The bag is always the last parameter, so calls work with
/// <c>|&gt;</c>, <c>&lt;|</c> and <c>&gt;&gt;</c>. No function changes the bag it is given.
/// </summary>
[<RequireQualifiedAccess>]
module PropertyBag =
    /// <summary>A bag with no values.</summary>
    let empty = PropertyBag(ImmutableDictionary<PropertyKey, obj>.Empty)

    /// <summary>Adds a property value, replacing any value already stored for that property.</summary>
    /// <param name="value">The property value</param>
    /// <param name="bag">The bag to add to</param>
    let add (value: PropertyValue) (bag: PropertyBag) : PropertyBag =
        PropertyBag(bag.Items.SetItem(value.Key, value.Value))

    /// <summary>Creates a bag from property values. The last value for a property wins.</summary>
    /// <param name="values">The property values</param>
    let ofSeq (values: #seq<PropertyValue>) : PropertyBag =
        values |> Seq.fold (fun bag value -> add value bag) empty

    /// <summary>Stores a value, replacing any value already stored for that property.</summary>
    /// <param name="property">The property</param>
    /// <param name="value">The value</param>
    /// <param name="bag">The bag to update</param>
    let set (property: Property<'T>) (value: 'T) (bag: PropertyBag) : PropertyBag =
        add (property.With value) bag

    /// <summary>Removes a property's value, if there is one.</summary>
    /// <param name="property">The property</param>
    /// <param name="bag">The bag to update</param>
    let remove (property: Property<'T>) (bag: PropertyBag) : PropertyBag =
        PropertyBag(bag.Items.Remove property.Key)

    /// <summary>Reads a property's value.</summary>
    /// <param name="property">The property</param>
    /// <param name="bag">The bag to read</param>
    /// <returns>The value, or None if no value is stored.</returns>
    let tryGet (property: Property<'T>) (bag: PropertyBag) : 'T option =
        match bag.Items.TryGetValue property.Key with
        // The key includes the value type, so this cast cannot fail.
        | true, value -> Some(unbox<'T> value)
        | false, _ -> None

    /// <summary>Reads a property's value, or a fallback if no value is stored.</summary>
    /// <param name="property">The property</param>
    /// <param name="fallback">The value to use when none is stored</param>
    /// <param name="bag">The bag to read</param>
    let getOr (property: Property<'T>) (fallback: 'T) (bag: PropertyBag) : 'T =
        tryGet property bag |> Option.defaultValue fallback

    /// <summary>Replaces a property's value using its current value.</summary>
    /// <param name="property">The property</param>
    /// <param name="f">Computes the new value from the current one, which is None if not stored</param>
    /// <param name="bag">The bag to update</param>
    let update (property: Property<'T>) (f: 'T option -> 'T) (bag: PropertyBag) : PropertyBag =
        set property (f (tryGet property bag)) bag

    /// <summary>Copies every value from an overlay bag onto a bag. Overlay values win.</summary>
    /// <param name="overlay">The values to copy</param>
    /// <param name="bag">The bag to update</param>
    let merge (overlay: PropertyBag) (bag: PropertyBag) : PropertyBag =
        PropertyBag(bag.Items.SetItems overlay.Items)

    /// <summary>Checks whether a value is stored for a property.</summary>
    /// <param name="property">The property</param>
    /// <param name="bag">The bag to read</param>
    let contains (property: Property<'T>) (bag: PropertyBag) : bool =
        bag.Items.ContainsKey property.Key
