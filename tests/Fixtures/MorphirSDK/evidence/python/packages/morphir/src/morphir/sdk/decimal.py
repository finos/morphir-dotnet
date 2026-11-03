"""Morphir.SDK.Decimal: arbitrary-precision decimal numbers.

A `Decimal` is the standard library `decimal.Decimal`. All arithmetic goes
through a context that is local to this module, with 50 significant digits and
half-up rounding. The module never reads or changes the global decimal context,
so other users of the `decimal` module see no effect, and a change to the global
context has no effect on the results here.

`Decimal` values compare by numeric value: `1.50 == 1.5`, and both have the same
hash.

Some names in this module shadow Python built-ins (`abs`, `round`). Import the
module with an alias, not the names:

    >>> from morphir.sdk import decimal as Decimal
    >>> Decimal.to_string(Decimal.add(Decimal.from_float(0.1), Decimal.bps(5)))
    '0.1005'

Departures from the Elm runtime:

* `from_float` raises `ValueError` for `NaN` and the infinities, which a
  `Decimal` in this module cannot hold.
* `shift_decimal_left` moves the exponent and uses no float. The Elm runtime
  multiplies by the float `10 ^ -n`.
"""

import decimal as _decimal
import math
import re

from morphir.sdk import maybe
from morphir.sdk._compare import Order
from morphir.sdk.maybe import Just, Maybe, Nothing

__all__ = [
    "Decimal",
    "abs",
    "add",
    "bps",
    "compare",
    "div",
    "div_with_default",
    "eq",
    "from_float",
    "from_int",
    "from_string",
    "gt",
    "gte",
    "hundred",
    "hundredth",
    "lt",
    "lte",
    "million",
    "millionth",
    "minus_one",
    "mul",
    "negate",
    "neq",
    "one",
    "round",
    "shift_decimal_left",
    "shift_decimal_right",
    "sub",
    "tenth",
    "thousand",
    "thousandth",
    "to_float",
    "to_string",
    "truncate",
    "zero",
]

Decimal = _decimal.Decimal
"""The standard library decimal type."""

PRECISION = 50
"""The number of significant digits that arithmetic results keep."""

# The exponent limits are as wide as the library allows, so that overflow is not
# a practical concern.
_CONTEXT = _decimal.Context(
    prec=PRECISION,
    rounding=_decimal.ROUND_HALF_UP,
    Emin=_decimal.MIN_EMIN,
    Emax=_decimal.MAX_EMAX,
)

# Elm's grammar: [<sign>]<digits>[.<digits>][e[<sign>]<digits>]. The standard
# library also accepts "Infinity", "NaN", underscores, white space and digits
# that are not ASCII; Elm parses none of these.
_ELM_DECIMAL = re.compile(r"[+-]?[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?")


# Constants

zero: Decimal = Decimal(0)
"""The number 0."""

one: Decimal = Decimal(1)
"""The number 1."""

minus_one: Decimal = Decimal(-1)
"""The number -1."""


# Convert from


def from_int(n: int) -> Decimal:
    """Convert an integer to a `Decimal`. The conversion is exact."""
    return Decimal(n)


def from_float(f: float) -> Decimal:
    """Convert a float to a `Decimal`.

    The result has the shortest digits that identify the float, so
    `from_float(0.1)` is exactly 0.1. Negative zero becomes zero.

    Raises:
        ValueError: If the float is `NaN` or an infinity.
    """
    if not math.isfinite(f):
        raise ValueError(f"from_float: {f!r} is not a finite number")
    return zero if f == 0 else Decimal(repr(f))


def from_string(s: str) -> Maybe[Decimal]:
    """Parse a `Decimal`.

    The accepted form is an optional sign, digits, an optional fraction and an
    optional exponent: `[+-]?digits(.digits)?([eE][+-]?digits)?`. Anything else
    yields `Nothing`. The conversion is exact; it keeps all the digits.
    """
    if not _ELM_DECIMAL.fullmatch(s):
        return Nothing()
    try:
        parsed = Decimal(s)
    except _decimal.InvalidOperation:
        # The exponent is outside the range that the library can hold.
        return Nothing()
    return Just(parsed) if parsed.is_finite() else Nothing()


# Convert from a known exponent


def hundred(n: int) -> Decimal:
    """Return `n` hundreds."""
    return Decimal(n).scaleb(2, _CONTEXT)


def thousand(n: int) -> Decimal:
    """Return `n` thousands."""
    return Decimal(n).scaleb(3, _CONTEXT)


def million(n: int) -> Decimal:
    """Return `n` millions."""
    return Decimal(n).scaleb(6, _CONTEXT)


def tenth(n: int) -> Decimal:
    """Return `n` tenths, with no float arithmetic."""
    return Decimal(n).scaleb(-1, _CONTEXT)


def hundredth(n: int) -> Decimal:
    """Return `n` hundredths, with no float arithmetic."""
    return Decimal(n).scaleb(-2, _CONTEXT)


def thousandth(n: int) -> Decimal:
    """Return `n` thousandths, with no float arithmetic."""
    return Decimal(n).scaleb(-3, _CONTEXT)


def bps(n: int) -> Decimal:
    """Return `n` basis points (ten-thousandths), with no float arithmetic."""
    return Decimal(n).scaleb(-4, _CONTEXT)


def millionth(n: int) -> Decimal:
    """Return `n` millionths, with no float arithmetic."""
    return Decimal(n).scaleb(-6, _CONTEXT)


# Convert to


def to_string(d: Decimal) -> str:
    """Convert a `Decimal` to a string.

    The result is always in positional notation, never in exponent notation,
    and has no trailing zeros in the fraction.
    """
    text = format(d, "f")
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    return "0" if text in ("-0", "") else text


def to_float(d: Decimal) -> float:
    """Convert a `Decimal` to the nearest float. This can lose precision."""
    return float(d)


# Arithmetic


def add(a: Decimal, b: Decimal) -> Decimal:
    """Add two decimals."""
    return _CONTEXT.add(a, b)


def sub(a: Decimal, b: Decimal) -> Decimal:
    """Subtract the second decimal from the first."""
    return _CONTEXT.subtract(a, b)


def mul(a: Decimal, b: Decimal) -> Decimal:
    """Multiply two decimals."""
    return _CONTEXT.multiply(a, b)


def div(a: Decimal, b: Decimal) -> Maybe[Decimal]:
    """Divide two decimals, or yield `Nothing` when the divisor is zero.

    A quotient that is not exact has 50 significant digits.
    """
    return Nothing() if b.is_zero() else Just(_CONTEXT.divide(a, b))


def div_with_default(default: Decimal, a: Decimal, b: Decimal) -> Decimal:
    """Divide two decimals, or yield the default when the divisor is zero."""
    return maybe.with_default(default, div(a, b))


def shift_decimal_left(n: int, d: Decimal) -> Decimal:
    """Move the decimal point `n` digits to the left, which divides by 10^n."""
    return d.scaleb(-n, _CONTEXT)


def shift_decimal_right(n: int, d: Decimal) -> Decimal:
    """Move the decimal point `n` digits to the right, which multiplies by 10^n."""
    return d.scaleb(n, _CONTEXT)


def negate(d: Decimal) -> Decimal:
    """Change the sign of a decimal. Zero stays zero."""
    return zero if d.is_zero() else d.copy_negate()


def abs(d: Decimal) -> Decimal:
    """Return the absolute value of a decimal."""
    return d.copy_abs()


# Rounding


def truncate(d: Decimal) -> Decimal:
    """Round toward zero to an integer."""
    return d.to_integral_value(rounding=_decimal.ROUND_DOWN, context=_CONTEXT)


def round(d: Decimal) -> Decimal:
    """Round to the nearest integer. A half goes away from zero."""
    return d.to_integral_value(rounding=_decimal.ROUND_HALF_UP, context=_CONTEXT)


# Compare


def compare(a: Decimal, b: Decimal) -> Order:
    """Compare two decimals by numeric value."""
    if a < b:
        return Order.LT
    if a > b:
        return Order.GT
    return Order.EQ


def eq(a: Decimal, b: Decimal) -> bool:
    """Check `a == b` by numeric value, so 1.50 equals 1.5."""
    return a == b


def neq(a: Decimal, b: Decimal) -> bool:
    """Check `a /= b` by numeric value."""
    return a != b


def gt(a: Decimal, b: Decimal) -> bool:
    """Check `a > b`."""
    return a > b


def gte(a: Decimal, b: Decimal) -> bool:
    """Check `a >= b`."""
    return a >= b


def lt(a: Decimal, b: Decimal) -> bool:
    """Check `a < b`."""
    return a < b


def lte(a: Decimal, b: Decimal) -> bool:
    """Check `a <= b`."""
    return a <= b
