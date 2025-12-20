namespace Morphir.IR

/// <summary>
/// AccessControlled manages visibility of types and values in the Morphir IR.
/// Values can be either publicly accessible or private to their defining scope.
/// </summary>
module AccessControlled =

    /// <summary>
    /// Access represents the accessibility level of a value.
    /// </summary>
    type Access =
        | Public
        | Private

    /// <summary>
    /// AccessControlled wraps a value with access control information.
    /// </summary>
    type AccessControlled<'T> = { Access: Access; Value: 'T }

    /// <summary>
    /// Creates a publicly accessible value.
    /// </summary>
    let public'<'T> (value: 'T) : AccessControlled<'T> =
        { Access = Public; Value = value }

    /// <summary>
    /// Creates a private value.
    /// </summary>
    let private'<'T> (value: 'T) : AccessControlled<'T> =
        { Access = Private; Value = value }

    /// <summary>
    /// Maps the value while preserving the access level.
    /// </summary>
    let map<'T, 'U> (mapper: 'T -> 'U) (accessControlled: AccessControlled<'T>) : AccessControlled<'U> =
        { Access = accessControlled.Access; Value = mapper accessControlled.Value }

    /// <summary>
    /// Gets the value from an AccessControlled wrapper.
    /// This always succeeds regardless of access level.
    /// </summary>
    let withPrivateAccess<'T> (accessControlled: AccessControlled<'T>) : 'T =
        accessControlled.Value

