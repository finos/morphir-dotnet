"""Morphir.SDK.Result: a computation that may fail.

`Result[E, A]` is the union of two frozen dataclasses, `Ok[A]` and `Err[E]`.
The type parameter order is Elm's: the error type first, then the value type.
All functions take their arguments in Elm order, with the `Result` last.
"""

from dataclasses import dataclass
from typing import TYPE_CHECKING

from morphir.sdk.maybe import Just, Maybe, Nothing

if TYPE_CHECKING:
    from collections.abc import Callable

__all__ = [
    "Err",
    "Ok",
    "Result",
    "and_then",
    "from_maybe",
    "map",
    "map2",
    "map3",
    "map4",
    "map5",
    "map_error",
    "to_maybe",
    "with_default",
]


@dataclass(frozen=True, slots=True)
class Ok[A]:
    """A `Result` that holds a success value."""

    value: A


@dataclass(frozen=True, slots=True)
class Err[E]:
    """A `Result` that holds an error."""

    error: E


type Result[E, A] = Ok[A] | Err[E]
"""Either `Ok[A]` or `Err[E]`."""


def with_default[E, A](default: A, result: Result[E, A]) -> A:
    """Return the value of an `Ok`, or the default for an `Err`."""
    match result:
        case Ok(value):
            return value
        case Err():
            return default


def map[E, A, B](f: Callable[[A], B], result: Result[E, A]) -> Result[E, B]:
    """Apply a function to the value of an `Ok`."""
    if isinstance(result, Ok):
        return Ok(f(result.value))
    return result


def map2[E, A, B, R](
    f: Callable[[A, B], R], ra: Result[E, A], rb: Result[E, B]
) -> Result[E, R]:
    """Apply a function when both arguments are `Ok`; else yield the first `Err`."""
    if isinstance(ra, Err):
        return ra
    if isinstance(rb, Err):
        return rb
    return Ok(f(ra.value, rb.value))


def map3[E, A, B, C, R](
    f: Callable[[A, B, C], R],
    ra: Result[E, A],
    rb: Result[E, B],
    rc: Result[E, C],
) -> Result[E, R]:
    """Apply a function when all arguments are `Ok`; else yield the first `Err`."""
    if isinstance(ra, Err):
        return ra
    if isinstance(rb, Err):
        return rb
    if isinstance(rc, Err):
        return rc
    return Ok(f(ra.value, rb.value, rc.value))


def map4[E, A, B, C, D, R](
    f: Callable[[A, B, C, D], R],
    ra: Result[E, A],
    rb: Result[E, B],
    rc: Result[E, C],
    rd: Result[E, D],
) -> Result[E, R]:
    """Apply a function when all arguments are `Ok`; else yield the first `Err`."""
    if isinstance(ra, Err):
        return ra
    if isinstance(rb, Err):
        return rb
    if isinstance(rc, Err):
        return rc
    if isinstance(rd, Err):
        return rd
    return Ok(f(ra.value, rb.value, rc.value, rd.value))


def map5[E, A, B, C, D, F, R](
    f: Callable[[A, B, C, D, F], R],
    ra: Result[E, A],
    rb: Result[E, B],
    rc: Result[E, C],
    rd: Result[E, D],
    re: Result[E, F],
) -> Result[E, R]:
    """Apply a function when all arguments are `Ok`; else yield the first `Err`."""
    if isinstance(ra, Err):
        return ra
    if isinstance(rb, Err):
        return rb
    if isinstance(rc, Err):
        return rc
    if isinstance(rd, Err):
        return rd
    if isinstance(re, Err):
        return re
    return Ok(f(ra.value, rb.value, rc.value, rd.value, re.value))


def and_then[E, A, B](
    f: Callable[[A], Result[E, B]], result: Result[E, A]
) -> Result[E, B]:
    """Chain a computation that can fail onto a `Result`."""
    if isinstance(result, Ok):
        return f(result.value)
    return result


def map_error[E, F, A](f: Callable[[E], F], result: Result[E, A]) -> Result[F, A]:
    """Apply a function to the error of an `Err`."""
    if isinstance(result, Err):
        return Err(f(result.error))
    return result


def to_maybe[E, A](result: Result[E, A]) -> Maybe[A]:
    """Convert to a `Maybe`, which drops the error."""
    if isinstance(result, Ok):
        return Just(result.value)
    return Nothing()


def from_maybe[E, A](error: E, maybe: Maybe[A]) -> Result[E, A]:
    """Convert from a `Maybe`, with the given error for `Nothing`."""
    if isinstance(maybe, Just):
        return Ok(maybe.value)
    return Err(error)
