namespace Morphir.IR;

/// <summary>
/// Represents a value that is controlled by access permissions.
/// The value can be either publicly accessible or private to its defining scope.
/// </summary>
/// <typeparam name="T">The type of the value being controlled.</typeparam>
/// <remarks>
/// This record is used to encapsulate values that have different access levels.
/// It allows for mapping the contained value to a new type while preserving the access level.
/// </remarks> 
public record AccessControlled<T>(AccessControlled.Access Access, T Value)
{
    public AccessControlled<TResult> Map<TResult>(Func<T, TResult> mapper) =>
        AccessControlled.Map(mapper, this);

    /// <summary>
    /// Gets the value with private access level.
    /// </summary>
    /// <remarks>
    /// This will always return the value.
    /// </remarks>
    /// <returns>The value with private access level.</returns>
    public T WithPrivateAccess() => Value;
}

public static class AccessControlled
{
    
    /// <summary>
    /// Represents the accessibility level of a value wrapped in <see cref="AccessControlled{T}"/>.
    /// </summary>
    public enum Access
    {
       /// <summary>
       /// The value is publicly accessible. It may be referenced and used by any consumer.
       /// </summary>
       Public,

       /// <summary>
       /// The value is private to its defining scope and should not be accessed by external consumers.
       /// </summary>
       Private
    }
    
    /// <summary>
    /// Maps the value of the given <see cref="AccessControlled{T}"/> to a new value of type <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="mapper">A function to map the value from type <typeparamref name="T"/> to type <typeparamref name="TResult"/>.</param>
    /// <param name="accessControlled">The <see cref="AccessControlled{T}"/> to map.</param>
    /// <typeparam name="T">The type of the value to map.</typeparam>
    /// <typeparam name="TResult">The type of the result value.</typeparam>
    /// <returns>A new <see cref="AccessControlled{TResult}"/> with the same access as the given <paramref name="accessControlled"/>, but with the mapped value.</returns>
    public static AccessControlled<TResult> Map<T,TResult>(Func<T,TResult> mapper, AccessControlled<T> accessControlled) =>
        new (accessControlled.Access, mapper(accessControlled.Value));

    public static AccessControlled<T> Public<T>(T value) =>
        new(Access.Public, value);

    public static AccessControlled<T> Private<T>(T value) =>
        new(Access.Private, value);
}
