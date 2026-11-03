"""Ordering and structural equality shared by the SDK modules.

Elm's `compare` works on the comparable types only: `Int`, `Float`, `Char`,
`String`, and tuples or lists of comparables. The Elm type checker rejects
anything else. Python has no such check, so `compare` raises `TypeError` at run
time instead.
"""

import dataclasses
from enum import Enum
from functools import cmp_to_key
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence


class Order(Enum):
    """The result of a comparison: less than, equal or greater than."""

    LT = "LT"
    EQ = "EQ"
    GT = "GT"


type Comparable = int | float | str | Sequence[Comparable]
"""The Elm comparable types: numbers, strings, and sequences of comparables."""


def _is_number(value: object) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _order(a: Any, b: Any) -> Order:
    if a < b:
        return Order.LT
    if a > b:
        return Order.GT
    return Order.EQ


def compare(a: object, b: object) -> Order:
    """Compare two comparable values.

    Numbers compare by value and strings by code point. Tuples and lists compare
    element by element; when one is a prefix of the other, the shorter one is
    less.

    Args:
        a: The first value.
        b: The second value.

    Returns:
        `Order.LT`, `Order.EQ` or `Order.GT`.

    Raises:
        TypeError: If the values are not comparable, or not of the same kind
            (for example an `int` and a `str`, or two `bool` values).
    """
    if _is_number(a) and _is_number(b):
        return _order(a, b)
    if isinstance(a, str) and isinstance(b, str):
        return _order(a, b)
    if isinstance(a, (tuple, list)) and isinstance(b, (tuple, list)):
        for x, y in zip(a, b, strict=False):
            order = compare(x, y)
            if order is not Order.EQ:
                return order
        return _order(len(a), len(b))
    raise TypeError(
        f"compare: values are not comparable "
        f"({type(a).__name__} and {type(b).__name__})"
    )


def to_int(order: Order) -> int:
    """Convert an `Order` to the -1, 0 or 1 that Python sort functions expect."""
    if order is Order.LT:
        return -1
    if order is Order.GT:
        return 1
    return 0


def sort_key[A](by: Callable[[A, A], Order]) -> Callable[[A], Any]:
    """Build a `key` function for `sorted` from a function that yields an `Order`."""
    return cmp_to_key(lambda a, b: to_int(by(a, b)))


def search[K: Comparable, T](
    key: K, items: Sequence[T], key_of: Callable[[T], K]
) -> tuple[bool, int]:
    """Find a key in a sequence of items sorted by key in ascending order.

    Args:
        key: The key to find.
        items: The sorted items, with no key twice.
        key_of: A function that gives the key of an item.

    Returns:
        A pair `(found, index)`. When `found` is true, `index` is the position
        of the item with the key. Otherwise `index` is the position where the
        key must go to keep the sequence sorted.

    Raises:
        TypeError: If the key is not comparable with the keys in the sequence.
    """
    low = 0
    high = len(items)
    while low < high:
        middle = (low + high) // 2
        order = compare(key_of(items[middle]), key)
        if order is Order.EQ:
            return (True, middle)
        if order is Order.LT:
            low = middle + 1
        else:
            high = middle
    return (False, low)


def freeze(value: Any) -> Any:
    """Turn every list inside a comparable value into a tuple.

    `Dict` and `Set` keep their keys in sorted order. A list key that changed
    after it was stored would break that order, and a list cannot be hashed. A
    stored key is therefore always the tuple form. A value with no list in it is
    returned as it is.
    """
    if isinstance(value, list):
        return tuple(freeze(item) for item in value)
    if isinstance(value, tuple):
        frozen = tuple(freeze(item) for item in value)
        if all(new is old for new, old in zip(frozen, value, strict=True)):
            return value
        return frozen
    return value


def check_strictly_ascending(keys: Sequence[Any], owner: str) -> None:
    """Check that keys are sorted in ascending order and that none repeats.

    Raises:
        ValueError: If a key is not greater than the key before it.
        TypeError: If two keys are not comparable with each other.
    """
    for index in range(1, len(keys)):
        if compare(keys[index - 1], keys[index]) is not Order.LT:
            raise ValueError(
                f"{owner}: entries must be sorted by key with no key twice; "
                f"use from_list to build one from entries in any order"
            )


def _contains_function(value: object) -> bool:
    if isinstance(value, (tuple, list)):
        return any(_contains_function(item) for item in value)
    if dataclasses.is_dataclass(value) and not isinstance(value, type):
        return any(
            _contains_function(getattr(value, field.name))
            for field in dataclasses.fields(value)
        )
    return callable(value) and not isinstance(value, type)


def equal(a: object, b: object) -> bool:
    """Check structural equality, like Elm's `==`.

    Frozen dataclasses, tuples, strings, numbers, `Decimal`, `Dict` and `Set`
    values all compare by value in Python, so this function uses `==`. `NaN` is
    not equal to itself, as in Elm.

    Args:
        a: The first value.
        b: The second value.

    Returns:
        True when the two values are structurally equal.

    Raises:
        TypeError: If a value is a function, or holds a function anywhere
            inside a tuple, a list or an SDK data type such as `Just`, `Ok` or
            `Dict`. Elm's runtime also fails on these values.
    """
    if _contains_function(a) or _contains_function(b):
        raise TypeError("equal: cannot compare functions")
    return bool(a == b)
