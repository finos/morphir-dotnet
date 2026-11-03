"""Morphir.SDK.String: Elm's String module.

A `String` is a Python `str` and a `Char` is a `str` that holds one code point.
All functions take their arguments in Elm order, with the string last. Functions
that yield a list yield a tuple.

Some names in this module shadow Python built-ins (`all`, `any`, `filter`,
`map`, `slice`). Import the module, not the names:

    >>> from morphir.sdk import string
    >>> string.pad(5, ".", "11")
    '..11.'

Departure from Elm: a Python `str` is a sequence of code points, while an Elm
string is a sequence of UTF-16 code units. `length`, `slice`, `left`, `right`,
`drop_left`, `drop_right`, `indexes` and the `pad` functions count code points,
so a character above U+FFFF counts as 1 here and as 2 in Elm. Python never
splits such a character in two.
"""

import math
import re
from decimal import Decimal
from typing import TYPE_CHECKING

from morphir.sdk.maybe import Just, Maybe, Nothing

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

    from morphir.sdk.char import Char

__all__ = [
    "all",
    "any",
    "append",
    "concat",
    "cons",
    "contains",
    "drop_left",
    "drop_right",
    "ends_with",
    "filter",
    "foldl",
    "foldr",
    "from_char",
    "from_float",
    "from_int",
    "from_list",
    "indexes",
    "indices",
    "is_empty",
    "join",
    "left",
    "length",
    "lines",
    "map",
    "pad",
    "pad_left",
    "pad_right",
    "repeat",
    "replace",
    "reverse",
    "right",
    "slice",
    "split",
    "starts_with",
    "to_float",
    "to_int",
    "to_list",
    "to_lower",
    "to_upper",
    "trim",
    "trim_left",
    "trim_right",
    "uncons",
    "words",
]

# The characters that JavaScript `trim` and the regex class `\s` treat as white
# space. Python's `str.strip` and `\s` use a different set.
_JS_SPACE = (
    "\t\n\v\f\r "
    "\N{NO-BREAK SPACE}"
    "\N{OGHAM SPACE MARK}"
    "\N{EN QUAD}\N{EM QUAD}\N{EN SPACE}\N{EM SPACE}"
    "\N{THREE-PER-EM SPACE}\N{FOUR-PER-EM SPACE}\N{SIX-PER-EM SPACE}"
    "\N{FIGURE SPACE}\N{PUNCTUATION SPACE}\N{THIN SPACE}\N{HAIR SPACE}"
    "\N{LINE SEPARATOR}\N{PARAGRAPH SEPARATOR}"
    "\N{NARROW NO-BREAK SPACE}"
    "\N{MEDIUM MATHEMATICAL SPACE}"
    "\N{IDEOGRAPHIC SPACE}"
    "\N{ZERO WIDTH NO-BREAK SPACE}"
)
_JS_SPACE_RUN = re.compile(f"[{re.escape(_JS_SPACE)}]+")
_LINE_BREAK = re.compile(r"\r\n|\r|\n")
_INT = re.compile(r"[+-]?[0-9]+")
_FLOAT_REJECT = re.compile(f"[{re.escape(_JS_SPACE)}xbo]")
_JS_NUMBER = re.compile(r"[+-]?(Infinity|([0-9]+\.?[0-9]*|\.[0-9]+)([eE][+-]?[0-9]+)?)")
# JavaScript prints a number in positional notation when its decimal exponent is
# in this range, and in exponent notation otherwise.
_JS_MIN_POSITIONAL_EXPONENT = -6
_JS_MAX_POSITIONAL_EXPONENT = 21


# Basics


def is_empty(s: str) -> bool:
    """Check that a string is empty."""
    return s == ""


def length(s: str) -> int:
    """Return the number of code points in a string."""
    return len(s)


def reverse(s: str) -> str:
    """Reverse a string."""
    return s[::-1]


def repeat(n: int, s: str) -> str:
    """Repeat a string `n` times. A count below 1 yields the empty string."""
    return s * n if n > 0 else ""


def replace(match: str, replacement: str, s: str) -> str:
    """Replace every occurrence of `match`.

    An empty `match` puts the replacement between the characters, as in Elm.
    """
    if match == "":
        return replacement.join(s)
    return s.replace(match, replacement)


# Building and splitting


def append(s1: str, s2: str) -> str:
    """Append two strings."""
    return s1 + s2


def concat(strings: Sequence[str]) -> str:
    """Concatenate many strings into one."""
    return "".join(strings)


def split(separator: str, s: str) -> tuple[str, ...]:
    """Split a string on a separator.

    An empty separator splits the string into its characters, as in Elm.
    """
    if separator == "":
        return tuple(s)
    return tuple(s.split(separator))


def join(separator: str, strings: Sequence[str]) -> str:
    """Put many strings together with a separator."""
    return separator.join(strings)


def words(s: str) -> tuple[str, ...]:
    """Break a string into words, split on runs of white space.

    As in elm/core, the string is trimmed first, so an empty or all-white-space
    string yields `("",)`.
    """
    return tuple(_JS_SPACE_RUN.split(trim(s)))


def lines(s: str) -> tuple[str, ...]:
    r"""Break a string into lines, split on `\n`, `\r\n` and `\r`."""
    return tuple(_LINE_BREAK.split(s))


# Substrings


def slice(start: int, end: int, s: str) -> str:
    """Take a substring from `start` up to, but not including, `end`.

    A negative index counts from the end of the string, as in Elm.
    """
    return s[start:end]


def left(n: int, s: str) -> str:
    """Take `n` characters from the left side of a string."""
    return "" if n < 1 else s[:n]


def right(n: int, s: str) -> str:
    """Take `n` characters from the right side of a string."""
    return "" if n < 1 else s[-n:]


def drop_left(n: int, s: str) -> str:
    """Drop `n` characters from the left side of a string."""
    return s if n < 1 else s[n:]


def drop_right(n: int, s: str) -> str:
    """Drop `n` characters from the right side of a string."""
    return s if n < 1 else s[:-n]


# Checks


def contains(sub: str, s: str) -> bool:
    """Check that the second string contains the first one."""
    return sub in s


def starts_with(prefix: str, s: str) -> bool:
    """Check that the second string starts with the first one."""
    return s.startswith(prefix)


def ends_with(suffix: str, s: str) -> bool:
    """Check that the second string ends with the first one."""
    return s.endswith(suffix)


def indexes(sub: str, s: str) -> tuple[int, ...]:
    """Return the start positions of a substring in a string.

    Matches do not overlap: `indexes("aa", "aaaa")` is `(0, 2)`, as in elm/core.
    An empty substring yields no positions.
    """
    if sub == "":
        return ()
    found: list[int] = []
    index = s.find(sub)
    while index > -1:
        found.append(index)
        index = s.find(sub, index + len(sub))
    return tuple(found)


indices = indexes
"""Alias of `indexes`."""


# Int conversions


def to_int(s: str) -> Maybe[int]:
    """Parse an integer.

    Elm accepts an optional sign and then ASCII digits only: no white space, no
    decimal point, no exponent, no radix prefix and no underscores.
    """
    return Just(int(s)) if _INT.fullmatch(s) else Nothing()


def from_int(n: int) -> str:
    """Convert an integer to a string."""
    return str(n)


# Float conversions


def to_float(s: str) -> Maybe[float]:
    """Parse a float, like elm/core.

    elm/core rejects the empty string and any string that contains white space
    or one of the letters `x`, `b` and `o`. It then converts the string with
    JavaScript number rules and rejects `NaN`. Thus ".5", "1." and "Infinity"
    are accepted, and " 1", "0x10", "1_000", "inf" and "nan" are not.
    """
    if s == "" or _FLOAT_REJECT.search(s) or not _JS_NUMBER.fullmatch(s):
        return Nothing()
    return Just(float(s))


def from_float(f: float) -> str:
    """Convert a float to a string, like Elm.

    Elm prints a float as JavaScript does: `1.0` prints as "1", `1e21` as
    "1e+21" and `1.5e-7` as "1.5e-7". `NaN` and the infinities print as "NaN",
    "Infinity" and "-Infinity".
    """
    if math.isnan(f):
        return "NaN"
    if math.isinf(f):
        return "Infinity" if f > 0 else "-Infinity"
    if f == 0:
        return "0"
    # `repr` gives the shortest digits that round-trip, as JavaScript does.
    sign, digit_tuple, exponent = Decimal(repr(f)).as_tuple()
    digits = "".join(str(d) for d in digit_tuple).rstrip("0")
    assert isinstance(exponent, int)
    # The decimal point goes after `point` digits: 0.d1d2... * 10 ** point.
    point = len(digit_tuple) + exponent
    count = len(digits)
    if count <= point <= _JS_MAX_POSITIONAL_EXPONENT:
        body = digits + "0" * (point - count)
    elif 0 < point <= _JS_MAX_POSITIONAL_EXPONENT:
        body = f"{digits[:point]}.{digits[point:]}"
    elif _JS_MIN_POSITIONAL_EXPONENT < point <= 0:
        body = "0." + "0" * -point + digits
    else:
        mantissa = digits if count == 1 else f"{digits[0]}.{digits[1:]}"
        body = f"{mantissa}e{'+' if point > 0 else '-'}{abs(point - 1)}"
    return f"-{body}" if sign else body


# Char conversions


def from_char(c: Char) -> str:
    """Create a string from a character."""
    return c


def cons(c: Char, s: str) -> str:
    """Add a character to the start of a string."""
    return c + s


def uncons(s: str) -> Maybe[tuple[Char, str]]:
    """Split a non-empty string into its first character and the remainder."""
    return Just((s[0], s[1:])) if s else Nothing()


def to_list(s: str) -> tuple[Char, ...]:
    """Convert a string to a list of characters."""
    return tuple(s)


def from_list(chars: Sequence[Char]) -> str:
    """Convert a list of characters to a string."""
    return "".join(chars)


# Formatting


def to_upper(s: str) -> str:
    """Convert a string to upper case."""
    return s.upper()


def to_lower(s: str) -> str:
    """Convert a string to lower case."""
    return s.lower()


def pad(n: int, c: Char, s: str) -> str:
    """Pad a string on both sides until it has `n` characters.

    When the padding is odd, the extra character goes on the left, as in
    elm/core: `pad(5, ".", "11")` is "..11.".
    """
    total = n - len(s)
    return repeat(total - total // 2, c) + s + repeat(total // 2, c)


def pad_left(n: int, c: Char, s: str) -> str:
    """Pad a string on the left until it has `n` characters."""
    return repeat(n - len(s), c) + s


def pad_right(n: int, c: Char, s: str) -> str:
    """Pad a string on the right until it has `n` characters."""
    return s + repeat(n - len(s), c)


def trim(s: str) -> str:
    """Remove white space on both sides of a string."""
    return s.strip(_JS_SPACE)


def trim_left(s: str) -> str:
    """Remove white space on the left of a string."""
    return s.lstrip(_JS_SPACE)


def trim_right(s: str) -> str:
    """Remove white space on the right of a string."""
    return s.rstrip(_JS_SPACE)


# Higher-order functions


def map(f: Callable[[Char], Char], s: str) -> str:
    """Transform every character in a string."""
    return "".join(f(c) for c in s)


def filter(predicate: Callable[[Char], bool], s: str) -> str:
    """Keep only the characters that pass the test."""
    return "".join(c for c in s if predicate(c))


def foldl[B](f: Callable[[Char, B], B], initial: B, s: str) -> B:
    """Reduce a string from the left. The function gets the character first."""
    accumulator = initial
    for c in s:
        accumulator = f(c, accumulator)
    return accumulator


def foldr[B](f: Callable[[Char, B], B], initial: B, s: str) -> B:
    """Reduce a string from the right. The function gets the character first."""
    accumulator = initial
    for c in reversed(s):
        accumulator = f(c, accumulator)
    return accumulator


def any(predicate: Callable[[Char], bool], s: str) -> bool:
    """Check that at least one character passes the test."""
    return next((True for c in s if predicate(c)), False)


def all(predicate: Callable[[Char], bool], s: str) -> bool:
    """Check that every character passes the test."""
    return next((False for c in s if not predicate(c)), True)
