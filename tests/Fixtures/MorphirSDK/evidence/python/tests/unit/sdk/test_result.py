"""Tests for morphir.sdk.result (Morphir.SDK.Result)."""

from morphir.sdk import result
from morphir.sdk.maybe import Just, Nothing
from morphir.sdk.result import Err, Ok, Result


def _add(*args: int) -> int:
    return sum(args)


class TestResultType:
    def test_structural_equality(self) -> None:
        assert Ok(1) == Ok(1)
        assert Err("bad") == Err("bad")
        assert Ok(1) != Err(1)

    def test_pattern_matching(self) -> None:
        def describe(r: Result[str, int]) -> str:
            match r:
                case Ok(value):
                    return f"ok {value}"
                case Err(error):
                    return f"err {error}"

        assert describe(Ok(3)) == "ok 3"
        assert describe(Err("bad")) == "err bad"


class TestResultFunctions:
    def test_with_default(self) -> None:
        assert result.with_default(0, Ok(5)) == 5
        assert result.with_default(0, Err("bad")) == 0

    def test_map(self) -> None:
        assert result.map(lambda n: n + 1, Ok(1)) == Ok(2)
        assert result.map(lambda n: n + 1, Err("bad")) == Err("bad")

    def test_map2_to_map5_yield_the_first_error(self) -> None:
        assert result.map2(_add, Ok(1), Ok(2)) == Ok(3)
        assert result.map2(_add, Err("x"), Err("y")) == Err("x")
        assert result.map2(_add, Ok(1), Err("y")) == Err("y")
        assert result.map3(_add, Ok(1), Ok(2), Ok(3)) == Ok(6)
        assert result.map3(_add, Ok(1), Ok(2), Err("z")) == Err("z")
        assert result.map4(_add, Ok(1), Ok(2), Ok(3), Ok(4)) == Ok(10)
        assert result.map4(_add, Ok(1), Err("b"), Ok(3), Err("d")) == Err("b")
        assert result.map5(_add, Ok(1), Ok(2), Ok(3), Ok(4), Ok(5)) == Ok(15)
        assert result.map5(_add, Ok(1), Ok(2), Ok(3), Ok(4), Err("e")) == Err("e")

    def test_and_then(self) -> None:
        def half(n: int) -> Result[str, int]:
            return Ok(n // 2) if n % 2 == 0 else Err("odd")

        assert result.and_then(half, Ok(4)) == Ok(2)
        assert result.and_then(half, Ok(3)) == Err("odd")
        assert result.and_then(half, Err("bad")) == Err("bad")

    def test_map_error(self) -> None:
        assert result.map_error(str.upper, Err("bad")) == Err("BAD")
        assert result.map_error(str.upper, Ok(1)) == Ok(1)

    def test_to_maybe(self) -> None:
        assert result.to_maybe(Ok(1)) == Just(1)
        assert result.to_maybe(Err("bad")) == Nothing()

    def test_from_maybe(self) -> None:
        assert result.from_maybe("missing", Just(1)) == Ok(1)
        assert result.from_maybe("missing", Nothing()) == Err("missing")


class TestFirstError:
    def test_every_position_can_hold_the_first_error(self) -> None:
        functions = {
            2: result.map2,
            3: result.map3,
            4: result.map4,
            5: result.map5,
        }
        for arity, function in functions.items():
            for position in range(arity):
                args = [Err(f"e{i}") if i >= position else Ok(i) for i in range(arity)]
                assert function(_add, *args) == Err(f"e{position}")
