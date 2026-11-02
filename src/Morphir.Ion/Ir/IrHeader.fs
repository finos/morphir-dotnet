namespace Morphir.Ion

open Morphir.IR
open Morphir.Ion.IonNodes

/// The IR family a format version selects.
type internal IrFamily =
    | ClassicFamily
    | V4Family

/// The kind a header states.
type internal IrKind =
    | LibraryKind
    | SpecsKind
    | ApplicationKind

/// A header the reader accepted.
type internal IrHeader = {
    FormatVersion: string
    Family: IrFamily
    Kind: IrKind
    Package: PackageName
    /// The entry points member, and where it is.
    EntryPoints: Node option
}

/// Header reading, version selection and the critical rule.
module internal IrHeader =
    [<Literal>]
    let private IonContract = "0.1.0-draft.1"

    /// Header members this reader understands. A tree's manifest adds <c>pathBudget</c>.
    let private headerMembers = [
        "critical"
        "dependencies"
        "entryPoints"
        "formatVersion"
        "ionVersion"
        "kind"
        "modules"
        "packageName"
        "pathBudget"
    ]

    let private invalidDocument path message = fail "invalid_document" path message

    /// Reads one release component, which has no sign and no leading zero.
    let private isReleaseComponent (input: string) =
        input
        <> ""
        && input
           |> Seq.forall System.Char.IsAsciiDigit
        && (input.Length = 1
            || input[0]
               <> '0')

    /// Selects the family. The draft names exactly three releases.
    let private selectVersion path (value: IonValue) =
        let release =
            match value.Annotations, value.Data with
            | [], IonData.String content -> content
            | _ -> fail "invalid_format_version_type" path "formatVersion is a release string."

        match release.Split('.') with
        | [| major; minor; patch |] when
            isReleaseComponent major
            && isReleaseComponent minor
            && isReleaseComponent patch
            ->
            match major, minor, patch with
            | "3", ("0" | "1"), "0" -> release, ClassicFamily
            | "4", "0", "0" -> release, V4Family
            | ("3" | "4"), _, _ ->
                fail
                    "unsupported_format_version_minor"
                    path
                    $"This reader implements formatVersion 3.0.0, 3.1.0 and 4.0.0, found {release}."
            | _ ->
                fail
                    "unsupported_format_version_major"
                    path
                    $"This reader implements formatVersion 3.0.0, 3.1.0 and 4.0.0, found {release}."
        | _ -> fail "invalid_format_version_syntax" path $"'{release}' is not an exact release."

    let private checkIonVersion path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.String IonContract -> ()
        | [], IonData.String other ->
            fail
                "unsupported_ion_version"
                path
                $"This reader implements ionVersion {IonContract}, found {other}."
        | _ -> fail "unsupported_ion_version" path "ionVersion is a string."

    let private checkCritical path (present: Members) (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.List names ->
            names
            |> List.iteri (fun i item ->
                let itemPath = itemAt path i

                let name =
                    match item.Annotations, item.Data with
                    | [], IonData.String content -> content
                    | [], IonData.Symbol(IonSymbol.Text content) -> content
                    | _ -> invalidDocument itemPath "critical holds member names."

                // A name no member answers to is no obstacle: only a present member can be misunderstood.
                if
                    not (List.contains name headerMembers)
                    && hasMember name present
                then
                    fail
                        "unknown_critical_member"
                        itemPath
                        $"critical names '{name}', which this reader does not understand."
            )
        | _ -> invalidDocument path "critical is a list of member names."

    let private kindOf path family (value: IonValue) =
        let kind =
            match value.Annotations, value.Data with
            | [], IonData.Symbol(IonSymbol.Text content)
            | [], IonData.String content -> content
            | _ -> invalidDocument path "kind is a symbol."

        match kind, family with
        | "library", _ -> LibraryKind
        | "specs", _ -> SpecsKind
        | "application", V4Family -> ApplicationKind
        | "application", ClassicFamily ->
            fail "unsupported_node" path "A v3 distribution is a library or specs."
        | other, _ -> invalidDocument path $"'{other}' is not a distribution kind."

    /// The header's named members, including those this reader does not understand. A repeat of a member this
    /// reader does not understand is ignored with it.
    let fields path (header: IonValue) : Members =
        let fields =
            match header.Data with
            | IonData.Struct fields ->
                fields
                |> List.choose (fun (field, item) ->
                    match field with
                    | IonSymbol.Text name -> Some(name, item)
                    | IonSymbol.Zero -> None
                )
            | _ -> invalidDocument path "The header is a struct."

        let known =
            fields
            |> List.filter (fun (name, _) -> List.contains name headerMembers)

        match
            known
            |> List.countBy fst
            |> List.tryFind (fun (_, count) -> count > 1)
        with
        | Some("formatVersion", _) ->
            fail
                "duplicate_format_version"
                (memberAt path "formatVersion")
                "The member 'formatVersion' appears more than once."
        | Some(name, _) ->
            invalidDocument (memberAt path name) $"The member '{name}' appears more than once."
        | None -> fields

    let read path (fields: Members) =
        match tryMember "ionVersion" fields with
        | Some value -> checkIonVersion (memberAt path "ionVersion") value
        | None -> ()

        let release, family =
            match tryMember "formatVersion" fields with
            | Some value -> selectVersion (memberAt path "formatVersion") value
            | None -> fail "missing_format_version" path "The header states no formatVersion."

        match tryMember "critical" fields with
        | Some value -> checkCritical (memberAt path "critical") fields value
        | None -> ()

        let kind =
            match tryMember "kind" fields with
            | Some value -> kindOf (memberAt path "kind") family value
            | None -> invalidDocument path "The header states no kind."

        if
            kind = SpecsKind
            && family = ClassicFamily
            && release = "3.0.0"
        then
            fail
                "specs_before_3_1"
                (memberAt path "kind")
                "A v3 specs distribution needs formatVersion 3.1.0."

        let package =
            match tryMember "packageName" fields with
            | Some value ->
                let here = memberAt path "packageName"
                PackageName(pathText here (text here value))
            | None -> invalidDocument path "The header states no packageName."

        let entryPoints =
            tryMember "entryPoints" fields
            |> Option.map (fun value -> memberAt path "entryPoints", value)

        if
            entryPoints.IsSome
            && kind
               <> ApplicationKind
        then
            fail
                "unsupported_node"
                (memberAt path "entryPoints")
                "Only an application has entry points."

        {
            FormatVersion = release
            Family = family
            Kind = kind
            Package = package
            EntryPoints = entryPoints
        }
