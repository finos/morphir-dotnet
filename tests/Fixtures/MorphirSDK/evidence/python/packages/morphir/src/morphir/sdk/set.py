"""Morphir.SDK.Set: a collection of unique comparable values.

This module follows elm/core 1.0.5. A `Set[A]` is a frozen dataclass that holds
its members as a tuple, sorted in ascending order by `basics.compare`, with no
member twice. Thus `to_list` yields the members in order, and two sets with the
same members are equal (`==`) and have the same hash, no matter in which order
the members were inserted.

Members can be any comparable value: numbers, strings, and tuples or lists of
comparables. A member that is not comparable raises `TypeError`.

Build a `Set` with `empty`, `singleton`, `from_list` and `insert`. Do not pass
members to the `Set` constructor, because it does not sort them.

Some names in this module shadow Python built-ins (`filter`, `map`). Import the
module with an alias, not the names:

    >>> from morphir.sdk import set as Set
    >>> Set.to_list(Set.from_list((3, 1, 3, 2)))
    (1, 2, 3)
"""

from dataclasses import dataclass
from typing import TYPE_CHECKING

from morphir.sdk import _compare
from morphir.sdk._compare import Comparable, Order

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

__all__ = [
    "Set",
    "diff",
    "empty",
    "filter",
    "foldl",
    "foldr",
    "from_list",
    "insert",
    "intersect",
    "is_empty",
    "map",
    "member",
    "partition",
    "remove",
    "singleton",
    "size",
    "to_list",
    "union",
]


@dataclass(frozen=True, slots=True)
class Set[A: Comparable]:
    """An immutable set with its members sorted.

    Use `from_list`, `singleton` or `insert` to build one. The constructor
    accepts only members that are already sorted with no member twice, and
    stores every list inside a member as a tuple.

    Raises:
        ValueError: If the members are not sorted, or a member repeats.
        TypeError: If two members are not comparable with each other.
    """

    entries: tuple[A, ...] = ()

    def __post_init__(self) -> None:
        entries = tuple(_compare.freeze(member) for member in self.entries)
        _compare.check_strictly_ascending(entries, "Set")
        object.__setattr__(self, "entries", entries)


def _identity[A](value: A) -> A:
    return value


def _search[A: Comparable](value: A, set_: Set[A]) -> tuple[bool, int]:
    return _compare.search(value, set_.entries, _identity)


# Build


def empty[A: Comparable]() -> Set[A]:  # pyright: ignore[reportInvalidTypeVarUse]
    """Create an empty set."""
    return Set()


def singleton[A: Comparable](value: A) -> Set[A]:
    """Create a set with one member."""
    return Set((value,))


def insert[A: Comparable](value: A, set_: Set[A]) -> Set[A]:
    """Insert a value into a set.

    Raises:
        TypeError: If the value is not comparable with the members of the set.
    """
    found, index = _search(value, set_)
    if found:
        return set_
    return Set((*set_.entries[:index], value, *set_.entries[index:]))


def remove[A: Comparable](value: A, set_: Set[A]) -> Set[A]:
    """Remove a value from a set. A value that is not there changes nothing."""
    found, index = _search(value, set_)
    if not found:
        return set_
    return Set((*set_.entries[:index], *set_.entries[index + 1 :]))


# Query


def is_empty[A: Comparable](set_: Set[A]) -> bool:
    """Check that a set is empty."""
    return not set_.entries


def member[A: Comparable](value: A, set_: Set[A]) -> bool:
    """Check that a value is in a set."""
    return _search(value, set_)[0]


def size[A: Comparable](set_: Set[A]) -> int:
    """Return the number of members in a set."""
    return len(set_.entries)


# Lists


def to_list[A: Comparable](set_: Set[A]) -> tuple[A, ...]:
    """Return the members, from lowest to highest."""
    return set_.entries


def from_list[A: Comparable](values: Sequence[A]) -> Set[A]:
    """Create a set from a list of values.

    Raises:
        TypeError: If the values are not comparable with each other.
    """
    entries: list[A] = []
    for value in sorted(values, key=_compare.sort_key(_compare.compare)):
        if not entries or _compare.compare(entries[-1], value) is not Order.EQ:
            entries.append(value)
    return Set(tuple(entries))


# Transform


def map[A: Comparable, B: Comparable](f: Callable[[A], B], set_: Set[A]) -> Set[B]:
    """Apply a function to every member.

    The result is sorted again and has no duplicates, so it can be smaller than
    the input.
    """
    return from_list(tuple(f(value) for value in set_.entries))


def foldl[A: Comparable, R](f: Callable[[A, R], R], initial: R, set_: Set[A]) -> R:
    """Reduce the members from lowest to highest."""
    accumulator = initial
    for value in set_.entries:
        accumulator = f(value, accumulator)
    return accumulator


def foldr[A: Comparable, R](f: Callable[[A, R], R], initial: R, set_: Set[A]) -> R:
    """Reduce the members from highest to lowest."""
    accumulator = initial
    for value in reversed(set_.entries):
        accumulator = f(value, accumulator)
    return accumulator


def filter[A: Comparable](predicate: Callable[[A], bool], set_: Set[A]) -> Set[A]:
    """Keep the members that pass the test."""
    return Set(tuple(value for value in set_.entries if predicate(value)))


def partition[A: Comparable](
    predicate: Callable[[A], bool], set_: Set[A]
) -> tuple[Set[A], Set[A]]:
    """Split a set into the members that pass the test and those that fail."""
    passed: list[A] = []
    failed: list[A] = []
    for value in set_.entries:
        (passed if predicate(value) else failed).append(value)
    return (Set(tuple(passed)), Set(tuple(failed)))


# Combine


def union[A: Comparable](left: Set[A], right: Set[A]) -> Set[A]:
    """Return the values that are in one set or the other."""
    return from_list((*left.entries, *right.entries))


def intersect[A: Comparable](left: Set[A], right: Set[A]) -> Set[A]:
    """Return the values that are in both sets."""
    return Set(tuple(value for value in left.entries if member(value, right)))


def diff[A: Comparable](left: Set[A], right: Set[A]) -> Set[A]:
    """Return the values of the first set that are not in the second."""
    return Set(tuple(value for value in left.entries if not member(value, right)))
