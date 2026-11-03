"""Morphir.SDK.Basics: Elm's Basics module.

Operators have the names that the Morphir IR SDK specification gives them
(`add`, `equal`, `append`, ...). Elm's `Int` is a Python `int` and `Float` is a
Python `float`. The names `not`, `and` and `or` are Python keywords, so they are
`not_`, `and_` and `or_` here.

Some names in this module shadow Python built-ins (`abs`, `max`, `min`, `round`).
Import the module, not the names:

    >>> from morphir.sdk import basics
    >>> basics.round(2.5)
    3

Departures from Elm:

* Python integers do not overflow, so `Int` arithmetic is exact at any size.
* `remainder_by(0, x)` raises `ZeroDivisionError`. Elm yields `NaN`, which a
  Python `int` cannot hold.
* `round`, `floor`, `ceiling` and `truncate` raise on `NaN` and the infinities,
  which a Python `int` cannot hold.
* Strings compare by code point. Elm compares by UTF-16 code unit, so the order
  differs only between characters above U+FFFF and those in U+E000..U+FFFF.
"""

import builtins
import math
from typing import TYPE_CHECKING, Any, Never, overload

from morphir.sdk import _compare
from morphir.sdk._compare import Comparable, Order

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

__all__ = [
    "Comparable",
    "Order",
    "abs",
    "acos",
    "add",
    "always",
    "and_",
    "append",
    "asin",
    "atan",
    "atan2",
    "ceiling",
    "clamp",
    "compare",
    "compose_left",
    "compose_right",
    "cos",
    "degrees",
    "divide",
    "e",
    "equal",
    "floor",
    "from_polar",
    "greater_than",
    "greater_than_or_equal",
    "identity",
    "integer_divide",
    "is_infinite",
    "is_nan",
    "less_than",
    "less_than_or_equal",
    "log_base",
    "max",
    "min",
    "mod_by",
    "multiply",
    "negate",
    "never",
    "not_",
    "not_equal",
    "or_",
    "pi",
    "power",
    "radians",
    "remainder_by",
    "round",
    "sin",
    "sqrt",
    "subtract",
    "tan",
    "to_float",
    "to_polar",
    "truncate",
    "turns",
    "xor",
]

# Numbers


def add[N: (int, float)](a: N, b: N) -> N:
    """Add two numbers (Elm `+`)."""
    return a + b


def subtract[N: (int, float)](a: N, b: N) -> N:
    """Subtract the second number from the first (Elm `-`)."""
    return a - b


def multiply[N: (int, float)](a: N, b: N) -> N:
    """Multiply two numbers (Elm `*`)."""
    return a * b


def divide(a: float, b: float) -> float:
    """Divide two floats (Elm `/`).

    A zero divisor follows IEEE 754 like Elm, and does not raise: the result is
    an infinity, or `NaN` for `0 / 0`.
    """
    if b == 0:
        if a == 0 or math.isnan(a):
            return math.nan
        return math.copysign(math.inf, a) * math.copysign(1.0, b)
    return a / b


def integer_divide(a: int, b: int) -> int:
    """Divide two integers (Elm `//`).

    The result is truncated toward zero. A zero divisor yields 0, as in Elm.
    """
    if b == 0:
        return 0
    quotient = builtins.abs(a) // builtins.abs(b)
    return quotient if (a < 0) == (b < 0) else -quotient


def _is_odd_integer(value: float) -> bool:
    return value.is_integer() and value % 2 == 1


def _float_power(base: float, exponent: float) -> float:
    if math.isnan(exponent):
        return math.nan
    if exponent == 0:
        return 1.0
    if math.isnan(base) or (math.isinf(exponent) and abs(base) == 1):
        return math.nan
    negative = math.copysign(1.0, base) < 0 and _is_odd_integer(exponent)
    try:
        return math.pow(base, exponent)
    except ValueError:
        # Zero to a negative power is an infinity. A negative base with an
        # exponent that is not an integer has no real result.
        if base == 0:
            return -math.inf if negative else math.inf
        return math.nan
    except OverflowError:
        return -math.inf if negative else math.inf


def power[N: (int, float)](base: N, exponent: N) -> N:
    """Raise a number to a power (Elm `^`).

    Two integers with an exponent of zero or more give an exact integer. Every
    other case follows JavaScript `Math.pow`, as the Elm runtime does, so the
    result is always a real number, `NaN` or an infinity: `power(-1.0, 0.5)` is
    `NaN` and `power(0.0, -1.0)` is infinity. Python `**` gives a complex number
    for the first and raises for the second.

    An integer with a negative exponent gives a float (`power(2, -1)` is 0.5),
    as it does in Elm at run time.
    """
    if (
        isinstance(base, int)
        and isinstance(exponent, int)
        and not isinstance(base, bool)
        and not isinstance(exponent, bool)
        and exponent >= 0
    ):
        exact: N = base**exponent
        return exact
    try:
        real = _float_power(float(base), float(exponent))
    except OverflowError:
        # An integer too large for a float.
        negative = base < 0 and _is_odd_integer(float(exponent))
        real = -math.inf if negative else math.inf
    # The result is a float even when N is int: an integer with a negative
    # exponent has no integer result, in Elm as here.
    result: Any = real
    return result  # type: ignore[no-any-return]


def to_float(a: int) -> float:
    """Convert an integer to a float."""
    return float(a)


def round(a: float) -> int:
    """Round to the nearest integer; a half goes toward positive infinity.

    This is the behaviour of Elm and of JavaScript `Math.round`, not the
    half-to-even rule of the Python built-in: `round(2.5)` is 3 and
    `round(-2.5)` is -2.

    Raises:
        ValueError: If the value is `NaN`.
        OverflowError: If the value is an infinity.
    """
    lower = math.floor(a)
    return lower + 1 if a - lower >= 0.5 else lower


def floor(a: float) -> int:
    """Round down to an integer."""
    return math.floor(a)


def ceiling(a: float) -> int:
    """Round up to an integer."""
    return math.ceil(a)


def truncate(a: float) -> int:
    """Round toward zero to an integer."""
    return math.trunc(a)


def mod_by(modulus: int, a: int) -> int:
    """Perform modular arithmetic; the result has the sign of the modulus.

    Raises:
        ZeroDivisionError: If the modulus is 0. Elm's runtime also fails.
    """
    if modulus == 0:
        raise ZeroDivisionError("mod_by: cannot perform mod 0")
    return a % modulus


def remainder_by(divisor: int, a: int) -> int:
    """Return the remainder of a division; the result has the sign of `a`.

    Raises:
        ZeroDivisionError: If the divisor is 0. Elm yields `NaN` here.
    """
    if divisor == 0:
        raise ZeroDivisionError("remainder_by: cannot divide by 0")
    remainder = builtins.abs(a) % builtins.abs(divisor)
    return -remainder if a < 0 else remainder


def negate[N: (int, float)](a: N) -> N:
    """Change the sign of a number."""
    return -a


def abs[N: (int, float)](a: N) -> N:
    """Return the absolute value of a number."""
    return builtins.abs(a)  # pyright: ignore[reportReturnType]


def clamp[A: Comparable](low: A, high: A, a: A) -> A:
    """Keep a value in the range from `low` to `high`."""
    if less_than(a, low):
        return low
    if greater_than(a, high):
        return high
    return a


def is_nan(a: float) -> bool:
    """Check that a float is `NaN` (Elm `isNaN`)."""
    return math.isnan(a)


def is_infinite(a: float) -> bool:
    """Check that a float is positive or negative infinity."""
    return math.isinf(a)


def sqrt(a: float) -> float:
    """Return the square root. A negative input yields `NaN`, as in Elm."""
    return math.nan if a < 0 else math.sqrt(a)


def _ln(a: float) -> float:
    if a > 0:
        return math.log(a)
    return -math.inf if a == 0 else math.nan


def log_base(base: float, a: float) -> float:
    """Return the logarithm of `a` in the given base.

    Inputs outside the domain yield `NaN` or an infinity, as in Elm.
    """
    return divide(_ln(a), _ln(base))


e: float = math.e
"""Euler's number."""

pi: float = math.pi
"""The ratio of the circumference of a circle to its diameter."""


def cos(a: float) -> float:
    """Return the cosine of an angle in radians."""
    return math.cos(a)


def sin(a: float) -> float:
    """Return the sine of an angle in radians."""
    return math.sin(a)


def tan(a: float) -> float:
    """Return the tangent of an angle in radians."""
    return math.tan(a)


def acos(a: float) -> float:
    """Return the arc cosine. An input outside -1..1 yields `NaN`, as in Elm."""
    return math.acos(a) if -1 <= a <= 1 else math.nan


def asin(a: float) -> float:
    """Return the arc sine. An input outside -1..1 yields `NaN`, as in Elm."""
    return math.asin(a) if -1 <= a <= 1 else math.nan


def atan(a: float) -> float:
    """Return the arc tangent."""
    return math.atan(a)


def atan2(y: float, x: float) -> float:
    """Return the angle of the vector `(x, y)`, in the correct quadrant."""
    return math.atan2(y, x)


def degrees(a: float) -> float:
    """Convert degrees to radians."""
    return a * math.pi / 180


def radians(a: float) -> float:
    """Convert radians to radians, which does nothing."""
    return a


def turns(a: float) -> float:
    """Convert turns to radians. One turn is 360 degrees."""
    return a * 2 * math.pi


def to_polar(point: tuple[float, float]) -> tuple[float, float]:
    """Convert Cartesian coordinates `(x, y)` to polar coordinates `(r, theta)`."""
    x, y = point
    return (math.hypot(x, y), math.atan2(y, x))


def from_polar(polar: tuple[float, float]) -> tuple[float, float]:
    """Convert polar coordinates `(r, theta)` to Cartesian coordinates `(x, y)`."""
    r, theta = polar
    return (r * math.cos(theta), r * math.sin(theta))


# Equality and ordering


def equal(a: object, b: object) -> bool:
    """Check structural equality (Elm `==`).

    Raises:
        TypeError: If a value is a function.
    """
    return _compare.equal(a, b)


def not_equal(a: object, b: object) -> bool:
    """Check structural inequality (Elm `/=`).

    Raises:
        TypeError: If a value is a function.
    """
    return not _compare.equal(a, b)


def compare[A: Comparable](a: A, b: A) -> Order:
    """Compare two comparable values.

    Comparable values are numbers, strings, and tuples or lists of comparables.

    Raises:
        TypeError: If the values are not comparable or not of the same kind.
    """
    return _compare.compare(a, b)


def less_than[A: Comparable](a: A, b: A) -> bool:
    """Check `a < b`."""
    return _compare.compare(a, b) is Order.LT


def greater_than[A: Comparable](a: A, b: A) -> bool:
    """Check `a > b`."""
    return _compare.compare(a, b) is Order.GT


def less_than_or_equal[A: Comparable](a: A, b: A) -> bool:
    """Check `a <= b`."""
    return _compare.compare(a, b) is not Order.GT


def greater_than_or_equal[A: Comparable](a: A, b: A) -> bool:
    """Check `a >= b`."""
    return _compare.compare(a, b) is not Order.LT


def max[A: Comparable](a: A, b: A) -> A:
    """Return the larger of two comparable values."""
    return a if greater_than(a, b) else b


def min[A: Comparable](a: A, b: A) -> A:
    """Return the smaller of two comparable values."""
    return a if less_than(a, b) else b


# Booleans


def not_(a: bool) -> bool:
    """Negate a boolean (Elm `not`)."""
    return not a


def and_(a: bool, b: bool) -> bool:
    """Return the logical AND of two booleans (Elm `&&`)."""
    return a and b


def or_(a: bool, b: bool) -> bool:
    """Return the logical OR of two booleans (Elm `||`)."""
    return a or b


def xor(a: bool, b: bool) -> bool:
    """Return the exclusive OR of two booleans."""
    return a != b


# Appendables


@overload
def append(a: str, b: str) -> str: ...  # type: ignore[overload-overlap]  # pyright: ignore[reportOverlappingOverload]
@overload
def append[A](a: Sequence[A], b: Sequence[A]) -> tuple[A, ...]: ...
def append[A](a: str | Sequence[A], b: str | Sequence[A]) -> str | tuple[A, ...]:
    """Append two strings or two lists (Elm `++`).

    Raises:
        TypeError: If one argument is a string and the other is not.
    """
    if isinstance(a, str) and isinstance(b, str):
        return a + b
    if isinstance(a, str) or isinstance(b, str):
        raise TypeError("append: cannot append a string and a list")
    return (*a, *b)


# Functions


def identity[A](a: A) -> A:
    """Return the argument."""
    return a


def always[A](a: A, _b: object) -> A:
    """Return the first argument and ignore the second."""
    return a


def compose_left[A, B, C](g: Callable[[B], C], f: Callable[[A], B]) -> Callable[[A], C]:
    """Compose two functions (Elm `g << f`): the result calls `f`, then `g`."""
    return lambda a: g(f(a))


def compose_right[A, B, C](
    f: Callable[[A], B], g: Callable[[B], C]
) -> Callable[[A], C]:
    """Compose two functions (Elm `f >> g`): the result calls `f`, then `g`."""
    return lambda a: g(f(a))


def never(_a: Never) -> Never:
    """Handle a value of type `Never`. No such value exists, so this raises.

    Raises:
        TypeError: Always.
    """
    raise TypeError("never: a value of type Never cannot exist")
