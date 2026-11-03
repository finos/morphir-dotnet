"""Morphir.SDK.List: elm/core's List module plus the Morphir joins.

An Elm `List a` is an immutable Python `tuple[A, ...]`. Functions accept any
`Sequence` as input and always yield a tuple; no function mutates its input.
All functions take their arguments in Elm order, with the list last.

Many names in this module shadow Python built-ins (`all`, `any`, `filter`,
`map`, `range`, `sum`). Import the module with an alias, not the names:

    >>> from morphir.sdk import list as List
    >>> List.map(lambda n: n * 2, (1, 2, 3))
    (2, 4, 6)
"""

import builtins
import math
from typing import TYPE_CHECKING

from morphir.sdk import _compare
from morphir.sdk._compare import Comparable, Order
from morphir.sdk.maybe import Just, Maybe, Nothing

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

__all__ = [
    "List",
    "all",
    "any",
    "append",
    "concat",
    "concat_map",
    "cons",
    "drop",
    "filter",
    "filter_map",
    "foldl",
    "foldr",
    "head",
    "indexed_map",
    "inner_join",
    "intersperse",
    "is_empty",
    "left_join",
    "length",
    "map",
    "map2",
    "map3",
    "map4",
    "map5",
    "maximum",
    "member",
    "minimum",
    "partition",
    "product",
    "range",
    "repeat",
    "reverse",
    "singleton",
    "sort",
    "sort_by",
    "sort_with",
    "sum",
    "tail",
    "take",
    "unzip",
]

type List[A] = tuple[A, ...]
"""An immutable list."""


# Create


def singleton[A](a: A) -> List[A]:
    """Create a list with one element."""
    return (a,)


def repeat[A](n: int, a: A) -> List[A]:
    """Create a list with `n` copies of a value. A count below 1 yields `()`."""
    return (a,) * n if n > 0 else ()


def range(low: int, high: int) -> List[int]:
    """Create the list of integers from `low` to `high`, both included."""
    return tuple(builtins.range(low, high + 1))


def cons[A](head: A, tail: Sequence[A]) -> List[A]:
    """Add an element to the front of a list (Elm `::`)."""
    return (head, *tail)


# Transform


def map[A, B](f: Callable[[A], B], xs: Sequence[A]) -> List[B]:
    """Apply a function to every element of a list."""
    return tuple(f(x) for x in xs)


def indexed_map[A, B](f: Callable[[int, A], B], xs: Sequence[A]) -> List[B]:
    """Apply a function to every element and its index, which starts at 0."""
    return tuple(f(index, x) for index, x in enumerate(xs))


def foldl[A, B](f: Callable[[A, B], B], initial: B, xs: Sequence[A]) -> B:
    """Reduce a list from the left. The function gets the element first."""
    accumulator = initial
    for x in xs:
        accumulator = f(x, accumulator)
    return accumulator


def foldr[A, B](f: Callable[[A, B], B], initial: B, xs: Sequence[A]) -> B:
    """Reduce a list from the right. The function gets the element first."""
    accumulator = initial
    for x in reversed(xs):
        accumulator = f(x, accumulator)
    return accumulator


def filter[A](predicate: Callable[[A], bool], xs: Sequence[A]) -> List[A]:
    """Keep the elements that pass the test."""
    return tuple(x for x in xs if predicate(x))


def filter_map[A, B](f: Callable[[A], Maybe[B]], xs: Sequence[A]) -> List[B]:
    """Apply a function to every element and keep the `Just` values."""
    return tuple(m.value for m in (f(x) for x in xs) if isinstance(m, Just))


# Utilities


def length[A](xs: Sequence[A]) -> int:
    """Return the number of elements in a list."""
    return len(xs)


def reverse[A](xs: Sequence[A]) -> List[A]:
    """Reverse a list."""
    return tuple(reversed(xs))


def member[A](a: A, xs: Sequence[A]) -> bool:
    """Check that a list contains a value, by structural equality."""
    return next((True for x in xs if _compare.equal(a, x)), False)


def all[A](predicate: Callable[[A], bool], xs: Sequence[A]) -> bool:
    """Check that every element passes the test."""
    return next((False for x in xs if not predicate(x)), True)


def any[A](predicate: Callable[[A], bool], xs: Sequence[A]) -> bool:
    """Check that at least one element passes the test."""
    return next((True for x in xs if predicate(x)), False)


def _extreme[A: Comparable](wanted: Order, xs: Sequence[A]) -> Maybe[A]:
    if not xs:
        return Nothing()
    best = xs[0]
    for x in xs[1:]:
        if _compare.compare(x, best) is wanted:
            best = x
    return Just(best)


def maximum[A: Comparable](xs: Sequence[A]) -> Maybe[A]:
    """Return the largest element, or `Nothing` for an empty list."""
    return _extreme(Order.GT, xs)


def minimum[A: Comparable](xs: Sequence[A]) -> Maybe[A]:
    """Return the smallest element, or `Nothing` for an empty list."""
    return _extreme(Order.LT, xs)


def sum[N: (int, float)](xs: Sequence[N]) -> N | int:
    """Return the sum of the elements. The sum of an empty list is 0."""
    return builtins.sum(xs)


def product[N: (int, float)](xs: Sequence[N]) -> N | int:
    """Return the product of the elements. The product of an empty list is 1."""
    return math.prod(xs)


# Combine


def append[A](xs: Sequence[A], ys: Sequence[A]) -> List[A]:
    """Put two lists together."""
    return (*xs, *ys)


def concat[A](lists: Sequence[Sequence[A]]) -> List[A]:
    """Concatenate many lists into one."""
    return tuple(x for xs in lists for x in xs)


def concat_map[A, B](f: Callable[[A], Sequence[B]], xs: Sequence[A]) -> List[B]:
    """Apply a function to every element and concatenate the lists it yields."""
    return tuple(y for x in xs for y in f(x))


def intersperse[A](separator: A, xs: Sequence[A]) -> List[A]:
    """Put a value between all the elements of a list."""
    result: list[A] = []
    for x in xs:
        if result:
            result.append(separator)
        result.append(x)
    return tuple(result)


def map2[A, B, R](f: Callable[[A, B], R], xs: Sequence[A], ys: Sequence[B]) -> List[R]:
    """Combine two lists element by element; stop at the end of the shortest."""
    return tuple(f(*args) for args in zip(xs, ys, strict=False))


def map3[A, B, C, R](
    f: Callable[[A, B, C], R], xs: Sequence[A], ys: Sequence[B], zs: Sequence[C]
) -> List[R]:
    """Combine three lists element by element; stop at the end of the shortest."""
    return tuple(f(*args) for args in zip(xs, ys, zs, strict=False))


def map4[A, B, C, D, R](
    f: Callable[[A, B, C, D], R],
    ws: Sequence[A],
    xs: Sequence[B],
    ys: Sequence[C],
    zs: Sequence[D],
) -> List[R]:
    """Combine four lists element by element; stop at the end of the shortest."""
    return tuple(f(*args) for args in zip(ws, xs, ys, zs, strict=False))


def map5[A, B, C, D, E, R](
    f: Callable[[A, B, C, D, E], R],
    vs: Sequence[A],
    ws: Sequence[B],
    xs: Sequence[C],
    ys: Sequence[D],
    zs: Sequence[E],
) -> List[R]:
    """Combine five lists element by element; stop at the end of the shortest."""
    return tuple(f(*args) for args in zip(vs, ws, xs, ys, zs, strict=False))


# Sort


def sort[A: Comparable](xs: Sequence[A]) -> List[A]:
    """Sort a list of comparable values from lowest to highest.

    Raises:
        TypeError: If the elements are not comparable with each other.
    """
    return tuple(sorted(xs, key=_compare.sort_key(_compare.compare)))


def sort_by[A, K: Comparable](f: Callable[[A], K], xs: Sequence[A]) -> List[A]:
    """Sort a list by a derived comparable key. The sort is stable.

    Raises:
        TypeError: If the keys are not comparable with each other.
    """
    key = _compare.sort_key(_compare.compare)
    return tuple(sorted(xs, key=lambda x: key(f(x))))


def sort_with[A](f: Callable[[A, A], Order], xs: Sequence[A]) -> List[A]:
    """Sort a list with a custom comparison function. The sort is stable."""
    return tuple(sorted(xs, key=_compare.sort_key(f)))


# Deconstruct


def is_empty[A](xs: Sequence[A]) -> bool:
    """Check that a list is empty."""
    return len(xs) == 0


def head[A](xs: Sequence[A]) -> Maybe[A]:
    """Return the first element, or `Nothing` for an empty list."""
    return Just(xs[0]) if xs else Nothing()


def tail[A](xs: Sequence[A]) -> Maybe[List[A]]:
    """Return all elements but the first, or `Nothing` for an empty list."""
    return Just(tuple(xs[1:])) if xs else Nothing()


def take[A](n: int, xs: Sequence[A]) -> List[A]:
    """Take the first `n` elements of a list."""
    return tuple(xs[:n]) if n > 0 else ()


def drop[A](n: int, xs: Sequence[A]) -> List[A]:
    """Drop the first `n` elements of a list."""
    return tuple(xs[n:]) if n > 0 else tuple(xs)


def partition[A](
    predicate: Callable[[A], bool], xs: Sequence[A]
) -> tuple[List[A], List[A]]:
    """Split a list into the elements that pass the test and those that fail."""
    passed: list[A] = []
    failed: list[A] = []
    for x in xs:
        (passed if predicate(x) else failed).append(x)
    return (tuple(passed), tuple(failed))


def unzip[A, B](pairs: Sequence[tuple[A, B]]) -> tuple[List[A], List[B]]:
    """Split a list of pairs into a pair of lists."""
    return (tuple(a for a, _ in pairs), tuple(b for _, b in pairs))


# Morphir additions to elm/core


def inner_join[A, B](
    list_b: Sequence[B], on: Callable[[A, B], bool], list_a: Sequence[A]
) -> List[tuple[A, B]]:
    """Join two lists like an SQL inner join.

    The result keeps the order of `list_a` and has one pair for each element of
    `list_b` that matches.
    """
    return tuple((a, b) for a in list_a for b in list_b if on(a, b))


def left_join[A, B](
    list_b: Sequence[B], on: Callable[[A, B], bool], list_a: Sequence[A]
) -> List[tuple[A, Maybe[B]]]:
    """Join two lists like an SQL left outer join.

    The result keeps the order of `list_a`. An element with no match in `list_b`
    yields one pair with `Nothing`.
    """
    result: list[tuple[A, Maybe[B]]] = []
    for a in list_a:
        matches = [b for b in list_b if on(a, b)]
        if matches:
            result.extend((a, Just(b)) for b in matches)
        else:
            result.append((a, Nothing()))
    return tuple(result)
