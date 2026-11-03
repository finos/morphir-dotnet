"""Morphir.SDK.Dict: a mapping from comparable keys to values.

This module follows elm/core 1.0.5. A `Dict[K, V]` is a frozen dataclass that
holds its entries as a tuple of `(key, value)` pairs, sorted in ascending order
by `basics.compare` on the key, with no key twice. Thus:

* `to_list`, `keys` and `values` yield the entries in key order, as in Elm.
* Two dicts with the same entries are equal (`==`) and have the same hash, no
  matter in which order the entries were inserted.
* Keys can be any comparable value: numbers, strings, and tuples or lists of
  comparables. A key that is not comparable raises `TypeError`.

Build a `Dict` with `empty`, `singleton`, `from_list` and `insert`. Do not pass
entries to the `Dict` constructor, because it does not sort them.

All functions take their arguments in Elm order, with the dict last, and yield
a new `Dict`; nothing is mutated. Lookup is a binary search, O(log n). `insert`
and `remove` copy the entries, O(n).

Some names in this module shadow Python built-ins (`filter`, `map`). Import the
module with an alias, not the names:

    >>> from morphir.sdk import dict as Dict
    >>> Dict.get("a", Dict.from_list((("b", 2), ("a", 1))))
    Just(value=1)
"""

from dataclasses import dataclass
from typing import TYPE_CHECKING

from morphir.sdk import _compare
from morphir.sdk._compare import Comparable, Order
from morphir.sdk.maybe import Just, Maybe, Nothing

if TYPE_CHECKING:
    from collections.abc import Callable, Iterator, Sequence

__all__ = [
    "Dict",
    "diff",
    "empty",
    "filter",
    "foldl",
    "foldr",
    "from_list",
    "get",
    "insert",
    "intersect",
    "is_empty",
    "keys",
    "map",
    "member",
    "merge",
    "partition",
    "remove",
    "singleton",
    "size",
    "to_list",
    "union",
    "update",
    "values",
]


@dataclass(frozen=True, slots=True)
class Dict[K: Comparable, V]:
    """An immutable dictionary with its entries sorted by key.

    Use `from_list`, `singleton` or `insert` to build one. The constructor
    accepts only entries that are already sorted by key with no key twice, and
    stores every list inside a key as a tuple.

    Raises:
        ValueError: If the entries are not sorted by key, or a key repeats.
        TypeError: If two keys are not comparable with each other.
    """

    entries: tuple[tuple[K, V], ...] = ()

    def __post_init__(self) -> None:
        entries = tuple((_compare.freeze(key), value) for key, value in self.entries)
        _compare.check_strictly_ascending([key for key, _ in entries], "Dict")
        object.__setattr__(self, "entries", entries)


def _key_of[K: Comparable, V](entry: tuple[K, V]) -> K:
    return entry[0]


def _search[K: Comparable, V](key: K, dict_: Dict[K, V]) -> tuple[bool, int]:
    return _compare.search(key, dict_.entries, _key_of)


# Build


def empty[K: Comparable, V]() -> Dict[K, V]:  # pyright: ignore[reportInvalidTypeVarUse]
    """Create an empty dictionary."""
    return Dict()


def singleton[K: Comparable, V](key: K, value: V) -> Dict[K, V]:
    """Create a dictionary with one entry."""
    return Dict(((key, value),))


def insert[K: Comparable, V](key: K, value: V, dict_: Dict[K, V]) -> Dict[K, V]:
    """Insert an entry. A value that is already there for the key is replaced.

    Raises:
        TypeError: If the key is not comparable with the keys in the dictionary.
    """
    found, index = _search(key, dict_)
    entries = dict_.entries
    after = entries[index + 1 :] if found else entries[index:]
    return Dict((*entries[:index], (key, value), *after))


def update[K: Comparable, V](
    key: K, alter: Callable[[Maybe[V]], Maybe[V]], dict_: Dict[K, V]
) -> Dict[K, V]:
    """Change the value for a key.

    The function gets `Just` the current value, or `Nothing` when the key is not
    there. When it yields `Nothing`, the key is removed.
    """
    match alter(get(key, dict_)):
        case Just(value):
            return insert(key, value, dict_)
        case Nothing():
            return remove(key, dict_)


def remove[K: Comparable, V](key: K, dict_: Dict[K, V]) -> Dict[K, V]:
    """Remove the entry for a key. A key that is not there changes nothing."""
    found, index = _search(key, dict_)
    if not found:
        return dict_
    return Dict((*dict_.entries[:index], *dict_.entries[index + 1 :]))


# Query


def is_empty[K: Comparable, V](dict_: Dict[K, V]) -> bool:
    """Check that a dictionary is empty."""
    return not dict_.entries


def member[K: Comparable, V](key: K, dict_: Dict[K, V]) -> bool:
    """Check that a key is in a dictionary."""
    return _search(key, dict_)[0]


def get[K: Comparable, V](key: K, dict_: Dict[K, V]) -> Maybe[V]:
    """Return the value for a key, or `Nothing` when the key is not there."""
    found, index = _search(key, dict_)
    return Just(dict_.entries[index][1]) if found else Nothing()


def size[K: Comparable, V](dict_: Dict[K, V]) -> int:
    """Return the number of entries in a dictionary."""
    return len(dict_.entries)


# Lists


def keys[K: Comparable, V](dict_: Dict[K, V]) -> tuple[K, ...]:
    """Return the keys, from lowest to highest."""
    return tuple(key for key, _ in dict_.entries)


def values[K: Comparable, V](dict_: Dict[K, V]) -> tuple[V, ...]:
    """Return the values, in the order of their keys."""
    return tuple(value for _, value in dict_.entries)


def to_list[K: Comparable, V](dict_: Dict[K, V]) -> tuple[tuple[K, V], ...]:
    """Return the `(key, value)` pairs, sorted by key."""
    return dict_.entries


def from_list[K: Comparable, V](pairs: Sequence[tuple[K, V]]) -> Dict[K, V]:
    """Create a dictionary from `(key, value)` pairs.

    When a key occurs more than once, the last pair wins, as in Elm.

    Raises:
        TypeError: If the keys are not comparable with each other.
    """
    key_of = _compare.sort_key(_compare.compare)
    entries: list[tuple[K, V]] = []
    # The sort is stable, so the last pair for a key is the last one in its run.
    for pair in sorted(pairs, key=lambda pair: key_of(pair[0])):
        if entries and _compare.compare(entries[-1][0], pair[0]) is Order.EQ:
            entries[-1] = pair
        else:
            entries.append(pair)
    return Dict(tuple(entries))


# Transform


def map[K: Comparable, A, B](f: Callable[[K, A], B], dict_: Dict[K, A]) -> Dict[K, B]:
    """Apply a function to every value. The function gets the key first."""
    return Dict(tuple((key, f(key, value)) for key, value in dict_.entries))


def foldl[K: Comparable, V, R](
    f: Callable[[K, V, R], R], initial: R, dict_: Dict[K, V]
) -> R:
    """Reduce the entries from the lowest key to the highest."""
    accumulator = initial
    for key, value in dict_.entries:
        accumulator = f(key, value, accumulator)
    return accumulator


def foldr[K: Comparable, V, R](
    f: Callable[[K, V, R], R], initial: R, dict_: Dict[K, V]
) -> R:
    """Reduce the entries from the highest key to the lowest."""
    accumulator = initial
    for key, value in reversed(dict_.entries):
        accumulator = f(key, value, accumulator)
    return accumulator


def filter[K: Comparable, V](
    predicate: Callable[[K, V], bool], dict_: Dict[K, V]
) -> Dict[K, V]:
    """Keep the entries that pass the test."""
    return Dict(tuple(entry for entry in dict_.entries if predicate(*entry)))


def partition[K: Comparable, V](
    predicate: Callable[[K, V], bool], dict_: Dict[K, V]
) -> tuple[Dict[K, V], Dict[K, V]]:
    """Split a dictionary into the entries that pass the test and those that fail."""
    passed: list[tuple[K, V]] = []
    failed: list[tuple[K, V]] = []
    for entry in dict_.entries:
        (passed if predicate(*entry) else failed).append(entry)
    return (Dict(tuple(passed)), Dict(tuple(failed)))


# Combine


def _walk[K: Comparable, A, B](
    left: Dict[K, A], right: Dict[K, B]
) -> Iterator[tuple[K, Maybe[A], Maybe[B]]]:
    """Walk two dictionaries in one pass, in ascending key order.

    Each step yields the key with `Just` the value from each side that has it.
    """
    lefts = left.entries
    rights = right.entries
    i = 0
    j = 0
    while i < len(lefts) and j < len(rights):
        order = _compare.compare(lefts[i][0], rights[j][0])
        if order is Order.LT:
            yield (lefts[i][0], Just(lefts[i][1]), Nothing())
            i += 1
        elif order is Order.GT:
            yield (rights[j][0], Nothing(), Just(rights[j][1]))
            j += 1
        else:
            yield (lefts[i][0], Just(lefts[i][1]), Just(rights[j][1]))
            i += 1
            j += 1
    for key, a in lefts[i:]:
        yield (key, Just(a), Nothing())
    for key, b in rights[j:]:
        yield (key, Nothing(), Just(b))


def union[K: Comparable, V](left: Dict[K, V], right: Dict[K, V]) -> Dict[K, V]:
    """Combine two dictionaries. For a key in both, the first dictionary wins."""
    entries: list[tuple[K, V]] = []
    for key, a, b in _walk(left, right):
        if isinstance(a, Just):
            entries.append((key, a.value))
        elif isinstance(b, Just):
            entries.append((key, b.value))
    return Dict(tuple(entries))


def intersect[K: Comparable, V, W](left: Dict[K, V], right: Dict[K, W]) -> Dict[K, V]:
    """Keep the entries of the first dictionary whose keys are in the second."""
    return Dict(
        tuple(
            (key, a.value)
            for key, a, b in _walk(left, right)
            if isinstance(a, Just) and isinstance(b, Just)
        )
    )


def diff[K: Comparable, V, W](left: Dict[K, V], right: Dict[K, W]) -> Dict[K, V]:
    """Keep the entries of the first dictionary whose keys are not in the second."""
    return Dict(
        tuple(
            (key, a.value)
            for key, a, b in _walk(left, right)
            if isinstance(a, Just) and isinstance(b, Nothing)
        )
    )


def merge[K: Comparable, A, B, R](
    left_step: Callable[[K, A, R], R],
    both_step: Callable[[K, A, B, R], R],
    right_step: Callable[[K, B, R], R],
    left: Dict[K, A],
    right: Dict[K, B],
    initial: R,
) -> R:
    """Combine two dictionaries in the most general way.

    The keys are visited from lowest to highest. `left_step` gets the keys that
    are only in the left dictionary, `both_step` the keys in both, and
    `right_step` the keys that are only in the right dictionary.
    """
    accumulator = initial
    for key, a, b in _walk(left, right):
        if isinstance(a, Just) and isinstance(b, Just):
            accumulator = both_step(key, a.value, b.value, accumulator)
        elif isinstance(a, Just):
            accumulator = left_step(key, a.value, accumulator)
        elif isinstance(b, Just):
            accumulator = right_step(key, b.value, accumulator)
    return accumulator
