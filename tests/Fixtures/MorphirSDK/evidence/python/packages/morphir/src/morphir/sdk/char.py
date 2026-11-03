"""Morphir.SDK.Char: Elm's Char module.

A `Char` is a Python `str` that holds one code point. The classification
functions are ASCII-only, like elm/core: `is_alpha("é")` is false. They yield
false for a string that does not hold exactly one code point.
"""

__all__ = [
    "Char",
    "from_code",
    "is_alpha",
    "is_alpha_num",
    "is_digit",
    "is_hex_digit",
    "is_lower",
    "is_oct_digit",
    "is_upper",
    "to_code",
    "to_locale_lower",
    "to_locale_upper",
    "to_lower",
    "to_upper",
]

type Char = str
"""A string that holds one code point."""

_MAX_CODE_POINT = 0x10FFFF
_REPLACEMENT_CHAR = "\N{REPLACEMENT CHARACTER}"


def _in_range(low: str, high: str, c: Char) -> bool:
    return len(c) == 1 and low <= c <= high


def is_upper(c: Char) -> bool:
    """Check for an ASCII upper case letter, `A` to `Z`."""
    return _in_range("A", "Z", c)


def is_lower(c: Char) -> bool:
    """Check for an ASCII lower case letter, `a` to `z`."""
    return _in_range("a", "z", c)


def is_alpha(c: Char) -> bool:
    """Check for an ASCII letter."""
    return is_lower(c) or is_upper(c)


def is_alpha_num(c: Char) -> bool:
    """Check for an ASCII letter or digit."""
    return is_alpha(c) or is_digit(c)


def is_digit(c: Char) -> bool:
    """Check for an ASCII digit, `0` to `9`."""
    return _in_range("0", "9", c)


def is_oct_digit(c: Char) -> bool:
    """Check for an ASCII octal digit, `0` to `7`."""
    return _in_range("0", "7", c)


def is_hex_digit(c: Char) -> bool:
    """Check for an ASCII hexadecimal digit: `0` to `9`, `a` to `f`, `A` to `F`."""
    return is_digit(c) or _in_range("a", "f", c) or _in_range("A", "F", c)


def to_upper(c: Char) -> Char:
    """Convert to upper case.

    Some characters expand to more than one code point ("ß" gives "SS"), as they
    do in Elm.
    """
    return c.upper()


def to_lower(c: Char) -> Char:
    """Convert to lower case."""
    return c.lower()


def to_locale_upper(c: Char) -> Char:
    """Convert to upper case. Python has no locale rules here; see `to_upper`."""
    return c.upper()


def to_locale_lower(c: Char) -> Char:
    """Convert to lower case. Python has no locale rules here; see `to_lower`."""
    return c.lower()


def to_code(c: Char) -> int:
    """Return the Unicode code point of a character.

    Raises:
        TypeError: If the string does not hold exactly one code point.
    """
    return ord(c)


def from_code(code: int) -> Char:
    """Return the character for a Unicode code point.

    A code outside the Unicode range yields U+FFFD, the replacement character,
    like elm/core.
    """
    return chr(code) if 0 <= code <= _MAX_CODE_POINT else _REPLACEMENT_CHAR
