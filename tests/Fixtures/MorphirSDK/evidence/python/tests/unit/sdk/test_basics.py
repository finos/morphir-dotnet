"""Tests for morphir.sdk.basics (Morphir.SDK.Basics)."""

import math

import pytest

from morphir.sdk import basics
from morphir.sdk.basics import Order
from morphir.sdk.maybe import Just, Nothing


class TestArithmetic:
    def test_basic_operators(self) -> None:
        assert basics.add(1, 2) == 3
        assert basics.subtract(5, 2) == 3
        assert basics.multiply(3, 4) == 12
        assert basics.divide(7, 2) == 3.5
        assert basics.power(2, 10) == 1024
        assert basics.negate(3) == -3
        assert basics.abs(-3) == 3
        assert basics.abs(-2.5) == 2.5

    def test_divide_by_zero_follows_ieee(self) -> None:
        assert basics.divide(1, 0) == math.inf
        assert basics.divide(-1, 0) == -math.inf
        assert basics.divide(1, -0.0) == -math.inf
        assert math.isnan(basics.divide(0, 0))

    def test_integer_divide_truncates_toward_zero(self) -> None:
        assert basics.integer_divide(7, 2) == 3
        assert basics.integer_divide(-7, 2) == -3
        assert basics.integer_divide(7, -2) == -3
        assert basics.integer_divide(-7, -2) == 3
        assert basics.integer_divide(7, 0) == 0

    def test_integer_divide_is_exact_for_big_ints(self) -> None:
        big = 10**30 + 1
        assert basics.integer_divide(big, 1) == big
        assert basics.integer_divide(-big, 2) == -(big // 2)

    def test_mod_by(self) -> None:
        assert basics.mod_by(4, -1) == 3
        assert basics.mod_by(4, 5) == 1
        assert basics.mod_by(-4, 1) == -3
        assert basics.mod_by(-4, -1) == -1
        assert basics.mod_by(3, 9) == 0

    def test_mod_by_zero_raises(self) -> None:
        with pytest.raises(ZeroDivisionError):
            basics.mod_by(0, 1)

    def test_remainder_by_takes_the_sign_of_the_dividend(self) -> None:
        assert basics.remainder_by(4, -1) == -1
        assert basics.remainder_by(4, 5) == 1
        assert basics.remainder_by(-4, 5) == 1
        assert basics.remainder_by(-4, -5) == -1
        assert basics.remainder_by(3, 9) == 0

    def test_remainder_by_zero_raises(self) -> None:
        with pytest.raises(ZeroDivisionError):
            basics.remainder_by(0, 1)


class TestRounding:
    def test_round_goes_half_toward_positive_infinity(self) -> None:
        assert basics.round(1.5) == 2
        assert basics.round(2.5) == 3
        assert basics.round(0.5) == 1
        assert basics.round(-0.5) == 0
        assert basics.round(-1.5) == -1
        assert basics.round(-2.5) == -2
        assert basics.round(2.4) == 2
        assert basics.round(-2.6) == -3

    def test_round_does_not_lose_precision_near_a_half(self) -> None:
        assert basics.round(0.49999999999999994) == 0

    def test_round_returns_int(self) -> None:
        assert isinstance(basics.round(1.5), int)

    def test_floor_ceiling_truncate(self) -> None:
        assert basics.floor(-1.2) == -2
        assert basics.ceiling(1.2) == 2
        assert basics.truncate(-1.7) == -1
        assert basics.truncate(1.7) == 1

    def test_to_float(self) -> None:
        result = basics.to_float(3)
        assert result == 3.0
        assert isinstance(result, float)


class TestComparison:
    def test_compare_numbers(self) -> None:
        assert basics.compare(1, 2) is Order.LT
        assert basics.compare(2, 2) is Order.EQ
        assert basics.compare(3, 2) is Order.GT
        assert basics.compare(1, 1.5) is Order.LT

    def test_compare_strings(self) -> None:
        assert basics.compare("a", "b") is Order.LT
        assert basics.compare("B", "a") is Order.LT
        assert basics.compare("abc", "ab") is Order.GT

    def test_compare_tuples_and_lists_lexicographically(self) -> None:
        assert basics.compare((1, "b"), (1, "a")) is Order.GT
        assert basics.compare((1, "a"), (2, "a")) is Order.LT
        assert basics.compare((1, 2), (1, 2, 3)) is Order.LT
        assert basics.compare((), ()) is Order.EQ
        assert basics.compare([1, 2], [1, 3]) is Order.LT
        assert basics.compare(((1, "x"), 2), ((1, "x"), 1)) is Order.GT

    def test_compare_rejects_values_that_are_not_comparable(self) -> None:
        with pytest.raises(TypeError):
            basics.compare(1, "a")  # type: ignore[misc]
        with pytest.raises(TypeError):
            basics.compare({}, 1)  # type: ignore[misc]
        with pytest.raises(TypeError):
            basics.compare(True, False)
        with pytest.raises(TypeError):
            basics.compare(Just(1), Nothing())  # type: ignore[misc]
        with pytest.raises(TypeError):
            basics.compare(len, len)  # type: ignore[misc]

    def test_ordering_predicates(self) -> None:
        assert basics.less_than(1, 2)
        assert not basics.less_than(2, 2)
        assert basics.greater_than("b", "a")
        assert basics.less_than_or_equal((1, "a"), (1, "b"))
        assert basics.greater_than_or_equal(2, 2)
        assert not basics.greater_than_or_equal(1, 2)

    def test_max_min_clamp(self) -> None:
        assert basics.max(1, 2) == 2
        assert basics.min("a", "b") == "a"
        assert basics.clamp(0, 10, 15) == 10
        assert basics.clamp(0, 10, -5) == 0
        assert basics.clamp(0, 10, 5) == 5


class TestEquality:
    def test_equal_is_structural(self) -> None:
        assert basics.equal((1, 2), (1, 2))
        assert basics.equal((1, (2, 3)), (1, (2, 3)))
        assert basics.not_equal((1, 2), (1, 3))
        assert basics.equal(Just(1), Just(1))
        assert not basics.equal(Just(1), Nothing())
        assert basics.equal("a", "a")
        assert not basics.equal(math.nan, math.nan)

    def test_equal_raises_on_functions(self) -> None:
        with pytest.raises(TypeError):
            basics.equal(len, len)
        with pytest.raises(TypeError):
            basics.not_equal((1, len), (1, len))


class TestBooleans:
    def test_operators(self) -> None:
        assert basics.not_(True) is False
        assert basics.and_(True, False) is False
        assert basics.or_(True, False) is True
        assert basics.xor(True, True) is False
        assert basics.xor(True, False) is True


class TestAppend:
    def test_strings(self) -> None:
        assert basics.append("ab", "cd") == "abcd"

    def test_lists(self) -> None:
        assert basics.append((1,), (2, 3)) == (1, 2, 3)
        assert basics.append([1], [2, 3]) == (1, 2, 3)

    def test_mixed_kinds_raise(self) -> None:
        with pytest.raises(TypeError):
            basics.append("ab", (1,))  # type: ignore[call-overload]


class TestFloats:
    def test_predicates(self) -> None:
        assert basics.is_nan(math.nan)
        assert not basics.is_nan(1.0)
        assert basics.is_infinite(-math.inf)
        assert not basics.is_infinite(1.0)

    def test_functions(self) -> None:
        assert basics.sqrt(16) == 4
        assert basics.log_base(10, 1000) == pytest.approx(3)
        assert basics.log_base(2, 256) == pytest.approx(8)
        assert basics.e == math.e
        assert basics.pi == math.pi
        assert basics.cos(0) == 1
        assert basics.sin(0) == 0
        assert basics.tan(0) == 0
        assert basics.acos(1) == 0
        assert basics.asin(0) == 0
        assert basics.atan(0) == 0
        assert basics.atan2(1, 1) == pytest.approx(math.pi / 4)

    def test_out_of_domain_inputs_yield_nan_or_infinity(self) -> None:
        assert math.isnan(basics.sqrt(-1))
        assert math.isnan(basics.acos(2))
        assert math.isnan(basics.asin(-2))
        assert basics.log_base(10, 0) == -math.inf
        assert math.isnan(basics.log_base(10, -1))

    def test_angles(self) -> None:
        assert basics.degrees(180) == pytest.approx(math.pi)
        assert basics.radians(math.pi) == math.pi
        assert basics.turns(0.5) == pytest.approx(math.pi)

    def test_polar(self) -> None:
        r, theta = basics.to_polar((3, 4))
        assert r == 5
        assert theta == pytest.approx(math.atan2(4, 3))
        x, y = basics.from_polar((5, math.atan2(4, 3)))
        assert x == pytest.approx(3)
        assert y == pytest.approx(4)


class TestFunctions:
    def test_identity_and_always(self) -> None:
        assert basics.identity(7) == 7
        assert basics.always(1, "ignored") == 1

    def test_composition(self) -> None:
        def inc(n: int) -> int:
            return n + 1

        def dbl(n: int) -> int:
            return n * 2

        assert basics.compose_left(inc, dbl)(3) == 7
        assert basics.compose_right(inc, dbl)(3) == 8

    def test_never_raises(self) -> None:
        with pytest.raises(TypeError):
            basics.never(None)  # type: ignore[arg-type]
