namespace Morphir.Configuration

/// A raw source setting and its safe presentation. Canonical write data is kept in Edits.
type MigrationField = {
    RawPath: string list
    CanonicalPath: string list option
    Value: ConfigValue
    Disposition: string
}

/// One missing canonical value. These values may contain secrets and require redaction for display.
type MigrationEdit = {
    KeyPath: string list
    Value: ConfigValue
}

/// Safe source and target values for one blocking canonical conflict.
type MigrationConflict = {
    KeyPath: string list
    SourceValue: ConfigValue
    TargetValue: ConfigValue
    Source: SourceIdentity
    Target: SourceIdentity
}

/// Immutable pure conversion plan. Candidate and Edits are canonical application data;
/// Ledger and Diagnostics are safe presentation data.
type MigrationPlan = {
    Source: SourceIdentity
    Target: SourceIdentity
    Candidate: ConfigValue
    Edits: MigrationEdit list
    Ledger: MigrationField list
    Conflicts: MigrationConflict list
    Diagnostics: ConfigDiagnostic list
    Blocked: bool
}

/// One independently observed value or resolved path difference. Values are redacted.
type MigrationDifference = {
    KeyPath: string list
    Before: ConfigValue option
    After: ConfigValue option
    BeforePath: string option
    AfterPath: string option
}

type MigrationOriginChange = {
    KeyPath: string list
    Before: ValueOrigin option
    After: ValueOrigin option
}

/// Origin changes are expected and reported separately from equivalence failures.
type MigrationVerification = {
    BaseWorkspace: OwningWorkspace option
    Verified: bool
    Before: ConfigValue
    After: ConfigValue
    Differences: MigrationDifference list
    OriginChanges: MigrationOriginChange list
    Diagnostics: ConfigDiagnostic list
}

/// Single-project configuration conversion. No filesystem or ambient state is consulted.
module ProjectMigration =
    let rec same left right =
        match left, right with
        | ConfigValue.Number left, ConfigValue.Number right ->
            (System.Double.IsNaN left
             && System.Double.IsNaN right)
            || System.BitConverter.DoubleToInt64Bits left = System.BitConverter.DoubleToInt64Bits
                right
        | (ConfigValue.Object left | ConfigValue.SecretReference left),
          (ConfigValue.Object right | ConfigValue.SecretReference right) ->
            left.Count = right.Count
            && Map.forall
                (fun key value ->
                    Map.tryFind key right
                    |> Option.exists (same value)
                )
                left
        | ConfigValue.Array left, ConfigValue.Array right ->
            left.Length = right.Length
            && List.forall2 same left right
        | _ -> left = right

    let private diagnostic source code path = {
        Code = code
        Message = "Project migration diagnostic; consult the code, key and source metadata."
        Severity = DiagnosticSeverity.Error
        Source = source
        KeyPath = path
        Position = None
    }

    let private baseInput (input: ResolutionInput) =
        let clean =
            { input with
                Sources =
                    input.Sources
                    |> List.filter (fun source ->
                        source.Source.Role = SourceRole.RootPrimary
                        || source.Source.Role = SourceRole.MemberPrimary
                    )
                Environment = Map.empty
                Explicit = DocumentDefaults.emptyValue
                Profiles =
                    { input.Profiles with
                        Selection = ProfileSelection.Disabled
                    }
                SuppressWarnings = []
            }

        let owner =
            input.OwningWorkspace
            |> Option.map (fun owner ->
                let root =
                    Resolver.resolve
                        { clean with
                            Sources =
                                clean.Sources
                                |> List.filter (fun source ->
                                    source.Source.Role = SourceRole.RootPrimary
                                )
                            Scope = ProjectScope.Root
                            OwningWorkspace = None
                        }

                let output =
                    ConfigTree.tryFind [ "workspace"; "out_dir" ] root.Effective
                    |> Option.bind (
                        function
                        | ConfigValue.String value -> Some(ProjectPath.join owner.Root value)
                        | _ -> None
                    )

                { owner with OutputDirectory = output }
            )

        { clean with OwningWorkspace = owner }

    let private sourceInput (identity: SourceIdentity) raw = {
        Source = identity
        Content = SourceContent.Available(Normalization.normalize identity raw Map.empty)
        Required = true
    }

    let private withClassic (classic: SourceInput) (input: ResolutionInput) =
        let input = baseInput input

        { input with
            Sources =
                (input.Sources
                 |> List.filter (fun source ->
                     source.Source.Path
                     <> classic.Source.Path
                 ))
                @ [ classic ]
        }

    let private validText (value: string) =
        try
            System.Text.UTF8Encoding(false, true).GetByteCount(value)
            |> ignore

            true
        with :? System.Text.EncoderFallbackException ->
            false

    let rec private representable value =
        match value with
        | ConfigValue.Null -> false
        | ConfigValue.BigInteger value ->
            value
            >= bigint System.Int64.MinValue
            && value
               <= bigint System.Int64.MaxValue
        | ConfigValue.String text -> validText text
        | ConfigValue.Object fields
        | ConfigValue.SecretReference fields ->
            fields
            |> Map.forall (fun key value ->
                validText key
                && representable value
            )
        | ConfigValue.Array items -> List.forall representable items
        | _ -> true

    let rec private leaves path value =
        match value with
        | ConfigValue.Object fields when not fields.IsEmpty ->
            fields
            |> Map.toList
            |> List.collect (fun (key, value) ->
                leaves
                    (path
                     @ [ key ])
                    value
            )
        | _ -> [ path, value ]

    let private emptyLegacy =
        function
        | ConfigValue.Array [] -> true
        | ConfigValue.Object values when values.IsEmpty -> true
        | ConfigValue.String value when System.String.IsNullOrWhiteSpace value -> true
        | _ -> false

    let private legacyMapping =
        Map.ofList [
            "name", [ "project"; "name" ]
            "sourceDirectory", [ "project"; "source_directory" ]
            "exposedModules", [ "project"; "exposed_modules" ]
        ]

    /// Plans a loss-accounted conversion against declared base workspace context.
    /// Machine, personal and activated sources in input do not affect materialized defaults.
    let planWithContext
        (classic: SourceInput)
        (target: SourceIdentity)
        targetRaw
        (input: ResolutionInput)
        =
        let mutable diagnostics = []
        let mutable ledger: MigrationField list = []
        let mutable edits: MigrationEdit list = []
        let mutable conflicts: MigrationConflict list = []
        let mutable candidate = targetRaw

        let addError source code path =
            diagnostics <-
                diagnostics
                @ [ diagnostic source code path ]

        let account rawPath canonicalPath value disposition =
            ledger <-
                ledger
                @ [
                    {
                        RawPath = rawPath
                        CanonicalPath = canonicalPath
                        Value = Editing.redactValue rawPath value
                        Disposition = disposition
                    }
                ]

        let raw =
            match classic.Content with
            | SourceContent.Available document -> document.Raw
            | SourceContent.Invalid errors ->
                diagnostics <-
                    diagnostics
                    @ errors

                DocumentDefaults.emptyValue
            | SourceContent.Missing ->
                addError classic.Source "project.migrate_source_missing" []
                DocumentDefaults.emptyValue

        let normalized = Normalization.normalize classic.Source raw Map.empty

        diagnostics <-
            diagnostics
            @ normalized.Diagnostics

        let recognized =
            legacyMapping
            |> Map.exists (fun key _ ->
                ConfigTree.tryFind [ key ] raw
                |> Option.isSome
            )

        if not recognized then
            addError classic.Source "project.migrate_classic_required" []

        match raw with
        | ConfigValue.Object fields ->
            for KeyValue(key, value) in fields do
                match Map.tryFind key legacyMapping with
                | Some canonical ->
                    let conflict =
                        ConfigTree.tryFind canonical raw
                        |> Option.isSome

                    let blocked =
                        conflict
                        || not (representable value)

                    account
                        [ key ]
                        (Some canonical)
                        value
                        (if blocked then "blocking" else "mapped")

                    if not (representable value) then
                        addError classic.Source "project.migrate_unrepresentable" [ key ]
                | None when
                    key = "localDependencies"
                    || key = "decorations"
                    || (key = "dependencies"
                        && (
                            match value with
                            | ConfigValue.Object _ -> false
                            | _ -> true
                        ))
                    ->
                    let retained =
                        emptyLegacy value
                        && representable value

                    account [ key ] None value (if retained then "retained" else "blocking")

                    if not retained then
                        addError classic.Source "project.migrate_loss" [ key ]
                | None ->
                    for path, child in leaves [ key ] (Normalization.normalizeSecrets [ key ] value) do
                        let retained =
                            List.forall validText path
                            && representable child

                        account path (Some path) child (if retained then "retained" else "blocking")

                        if not retained then
                            addError classic.Source "project.migrate_unrepresentable" path
        | _ -> account [] None raw "blocking"

        let authored =
            normalized.Values
            |> fun values ->
                Validation.validate
                    values
                    (Map.ofList [
                        [],
                        {
                            Source = classic.Source
                            Position = None
                        }
                    ])

        diagnostics <-
            diagnostics
            @ authored

        let before = Resolver.resolve (withClassic (sourceInput classic.Source raw) input)
        let mutable desired = normalized.Values

        for field in normalized.LegacyFields do
            if field.Disposition = "default" then
                match field.CanonicalPath with
                | Some path when
                    ConfigTree.tryFind path desired
                    |> Option.isNone
                    ->
                    let value =
                        ConfigTree.tryFind path before.Effective
                        |> Option.defaultValue field.Value

                    desired <- ConfigTree.set path value desired
                    account [] (Some path) value "derived"
                | _ -> ()
            elif field.Disposition = "derived" then
                account [] field.CanonicalPath field.Value "derived"

        let targetDocument = Normalization.normalize target targetRaw Map.empty

        diagnostics <-
            diagnostics
            @ targetDocument.Diagnostics

        diagnostics <-
            diagnostics
            @ Validation.validate
                targetDocument.Values
                (Map.ofList [ [], { Source = target; Position = None } ])

        let rec merge path wanted current =
            match wanted, current with
            | ConfigValue.Object wanted, ConfigValue.Object current ->
                wanted
                |> Map.fold
                    (fun result key value ->
                        let keyPath =
                            path
                            @ [ key ]

                        match Map.tryFind key result with
                        | Some existing -> Map.add key (merge keyPath value existing) result
                        | None ->
                            // Missing maps are emitted as leaf edits, preserving existing table presentation.
                            for editPath, editValue in leaves keyPath value do
                                edits <-
                                    edits
                                    @ [
                                        {
                                            KeyPath = editPath
                                            Value = editValue
                                        }
                                    ]

                            Map.add key value result
                    )
                    current
                |> ConfigValue.Object
            | _ when same wanted current -> current
            | _ ->
                addError target "project.migrate_conflict" path

                conflicts <-
                    conflicts
                    @ [
                        {
                            KeyPath = path
                            SourceValue = Editing.redactValue path wanted
                            TargetValue = Editing.redactValue path current
                            Source = classic.Source
                            Target = target
                        }
                    ]

                current

        candidate <- merge [] desired targetRaw
        let finalDocument = Normalization.normalize target candidate Map.empty

        diagnostics <-
            diagnostics
            @ finalDocument.Diagnostics

        diagnostics <-
            diagnostics
            @ Validation.validate
                finalDocument.Values
                (Map.ofList [ [], { Source = target; Position = None } ])

        let diagnostics =
            diagnostics
            |> List.distinct
            |> Inspection.safeDiagnostics

        {
            Source = classic.Source
            Target = target
            Candidate = candidate
            Edits = edits
            Ledger = ledger
            Conflicts = conflicts
            Diagnostics = diagnostics
            Blocked =
                diagnostics
                |> List.exists (fun diagnostic -> diagnostic.Severity = DiagnosticSeverity.Error)
        }

    /// Convenience for a standalone project without supplied base workspace context.
    let plan classic target targetRaw =
        planWithContext classic target targetRaw DocumentDefaults.resolutionInput

    /// Safe presentation copy. Apply coordinators must retain the original canonical plan.
    let redactPlan (plan: MigrationPlan) =
        { plan with
            Candidate = Editing.redactValue [] plan.Candidate
            Edits =
                plan.Edits
                |> List.map (fun edit ->
                    { edit with
                        Value = Editing.redactValue edit.KeyPath edit.Value
                    }
                )
            Diagnostics = Inspection.safeDiagnostics plan.Diagnostics
        }

    /// Resolves a candidate independently of the converted classic source. Declared root/member
    /// primaries remain; machine, selected, personal, runtime and activated-profile layers do not.
    /// Candidate accepts the actual parsed TOML raw tree so rendering is part of verification.
    let verify
        (classic: SourceInput)
        (target: SourceIdentity)
        candidate
        (input: ResolutionInput)
        projectRoot
        =
        let classic =
            match classic.Content with
            | SourceContent.Available document -> sourceInput classic.Source document.Raw
            | _ -> classic

        let beforeInput = withClassic classic input

        let afterInput =
            { beforeInput with
                Sources =
                    (beforeInput.Sources
                     |> List.filter (fun source ->
                         source.Source.Path
                         <> classic.Source.Path
                         && source.Source.Path
                            <> target.Path
                     ))
                    @ [ sourceInput target candidate ]
            }

        let before = Resolver.resolve beforeInput
        let after = Resolver.resolve afterInput

        // Resolver deliberately removes profile metadata when activation is disabled.
        // Compare declarations separately so future selection cannot change unnoticed.
        let withControls sources values =
            Profiles.declaredControls sources
            |> Option.map (fun controls -> ConfigTree.set [ "profiles" ] controls values)
            |> Option.defaultValue values

        let beforeValues = withControls beforeInput.Sources before.Effective
        let afterValues = withControls afterInput.Sources after.Effective

        let oldValues =
            leaves [] beforeValues
            |> Map.ofList

        let newValues =
            leaves [] afterValues
            |> Map.ofList

        let keys =
            Set.union
                (oldValues
                 |> Map.keys
                 |> Set.ofSeq)
                (newValues
                 |> Map.keys
                 |> Set.ofSeq)

        let sameOption left right =
            match left, right with
            | Some left, Some right -> same left right
            | None, None -> true
            | _ -> false

        let resolvedPath path =
            function
            | Some(ConfigValue.String value) when path = [ "project"; "source_directory" ] ->
                Some(ProjectPath.join projectRoot value)
            | Some(ConfigValue.String value) when path = [ "workspace"; "out_dir" ] ->
                let root =
                    beforeInput.OwningWorkspace
                    |> Option.map (fun workspace -> workspace.Root)
                    |> Option.defaultValue projectRoot

                Some(ProjectPath.join root value)
            | _ -> None

        let differences =
            keys
            |> Set.toList
            |> List.choose (fun path ->
                let oldValue = Map.tryFind path oldValues
                let newValue = Map.tryFind path newValues
                let oldPath = resolvedPath path oldValue
                let newPath = resolvedPath path newValue

                if
                    sameOption oldValue newValue
                    && oldPath = newPath
                then
                    None
                else
                    Some
                        {
                            KeyPath = path
                            Before =
                                oldValue
                                |> Option.map (Editing.redactValue path)
                            After =
                                newValue
                                |> Option.map (Editing.redactValue path)
                            BeforePath = oldPath
                            AfterPath = newPath
                        }
            )

        let originChanges =
            keys
            |> Set.toList
            |> List.choose (fun path ->
                let oldOrigin = Map.tryFind path before.Origins
                let newOrigin = Map.tryFind path after.Origins

                if oldOrigin = newOrigin then
                    None
                else
                    Some
                        {
                            KeyPath = path
                            Before = oldOrigin
                            After = newOrigin
                        }
            )

        let diagnostics =
            before.Diagnostics
            @ after.Diagnostics
            |> List.filter (fun diagnostic -> diagnostic.Severity = DiagnosticSeverity.Error)
            |> List.distinct
            |> Inspection.safeDiagnostics

        {
            BaseWorkspace = beforeInput.OwningWorkspace
            Verified =
                differences.IsEmpty
                && diagnostics.IsEmpty
            Before = Editing.redactValue [] beforeValues
            After = Editing.redactValue [] afterValues
            Differences = differences
            OriginChanges = originChanges
            Diagnostics = diagnostics
        }
