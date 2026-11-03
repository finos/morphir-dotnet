"""Morphir.SDK.Maybe: a value that may or may not exist.

`Maybe[A]` is the union of two frozen dataclasses, `Just[A]` and `Nothing`. Use
structural pattern matching to take a `Maybe` apart:

    >>> from morphir.sdk.maybe import Just, Maybe, Nothing
    >>> def describe(m: Maybe[int]) -> str:
    ...     match m:
    ...         case Just(value):
    ...             return f"just {value}"
    ...         case Nothing():
    ...             return "nothing"
    >>> describe(Just(1))
    'just 1'

All functions take their arguments in Elm order, with the `Maybe` last.
"""

from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Callable

__all__ = [
    "Just",
    "Maybe",
    "Nothing",
    "and_then",
    "has_value",
    "map",
    "map2",
    "map3",
    "map4",
    "map5",
    "with_default",
]


@dataclass(frozen=True, slots=True)
class Just[A]:
    """A `Maybe` that holds a value."""

    value: A


@dataclass(frozen=True, slots=True)
class Nothing:
    """A `Maybe` that holds no value. All instances are equal."""


type Maybe[A] = Just[A] | Nothing
"""Either `Just[A]` or `Nothing`."""


def with_default[A](default: A, maybe: Maybe[A]) -> A:
    """Return the value of a `Just`, or the default for `Nothing`."""
    match maybe:
        case Just(value):
            return value
        case Nothing():
            return default


def has_value[A](maybe: Maybe[A]) -> bool:
    """Check that a `Maybe` is a `Just`. This is a Morphir addition to elm/core."""
    return isinstance(maybe, Just)


def map[A, B](f: Callable[[A], B], maybe: Maybe[A]) -> Maybe[B]:
    """Apply a function to the value of a `Just`."""
    if isinstance(maybe, Just):
        return Just(f(maybe.value))
    return Nothing()


def map2[A, B, R](f: Callable[[A, B], R], ma: Maybe[A], mb: Maybe[B]) -> Maybe[R]:
    """Apply a function when both arguments are `Just`."""
    if isinstance(ma, Just) and isinstance(mb, Just):
        return Just(f(ma.value, mb.value))
    return Nothing()


def map3[A, B, C, R](
    f: Callable[[A, B, C], R], ma: Maybe[A], mb: Maybe[B], mc: Maybe[C]
) -> Maybe[R]:
    """Apply a function when all three arguments are `Just`."""
    if isinstance(ma, Just) and isinstance(mb, Just) and isinstance(mc, Just):
        return Just(f(ma.value, mb.value, mc.value))
    return Nothing()


def map4[A, B, C, D, R](
    f: Callable[[A, B, C, D], R],
    ma: Maybe[A],
    mb: Maybe[B],
    mc: Maybe[C],
    md: Maybe[D],
) -> Maybe[R]:
    """Apply a function when all four arguments are `Just`."""
    if (
        isinstance(ma, Just)
        and isinstance(mb, Just)
        and isinstance(mc, Just)
        and isinstance(md, Just)
    ):
        return Just(f(ma.value, mb.value, mc.value, md.value))
    return Nothing()


def map5[A, B, C, D, E, R](
    f: Callable[[A, B, C, D, E], R],
    ma: Maybe[A],
    mb: Maybe[B],
    mc: Maybe[C],
    md: Maybe[D],
    me: Maybe[E],
) -> Maybe[R]:
    """Apply a function when all five arguments are `Just`."""
    if (
        isinstance(ma, Just)
        and isinstance(mb, Just)
        and isinstance(mc, Just)
        and isinstance(md, Just)
        and isinstance(me, Just)
    ):
        return Just(f(ma.value, mb.value, mc.value, md.value, me.value))
    return Nothing()


def and_then[A, B](f: Callable[[A], Maybe[B]], maybe: Maybe[A]) -> Maybe[B]:
    """Chain a computation that can fail onto a `Maybe`."""
    if isinstance(maybe, Just):
        return f(maybe.value)
    return Nothing()
