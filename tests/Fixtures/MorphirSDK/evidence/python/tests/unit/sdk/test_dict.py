"""Tests for morphir.sdk.dict (Morphir.SDK.Dict)."""

import dataclasses

import pytest

from morphir.sdk import basics
from morphir.sdk import dict as dict_
from morphir.sdk.dict import Dict
from morphir.sdk.maybe import Just, Maybe, Nothing

ABC: Dict[str, int] = dict_.from_list((("b", 2), ("a", 1), ("c", 3)))


def _is_odd_value(_key: str, value: int) -> bool:
    return value % 2 == 1


class TestBuild:
    def test_empty_and_singleton(self) -> None:
        assert dict_.to_list(dict_.empty()) == ()
        assert dict_.is_empty(dict_.empty())
        assert dict_.to_list(dict_.singleton("k", 1)) == (("k", 1),)
        assert not dict_.is_empty(dict_.singleton("k", 1))
        assert isinstance(dict_.empty(), Dict)

    def test_from_list_sorts_and_later_entries_win(self) -> None:
        assert dict_.to_list(dict_.from_list(((2, "b"), (1, "a")))) == (
            (1, "a"),
            (2, "b"),
        )
        assert dict_.to_list(dict_.from_list(((1, "first"), (1, "second")))) == (
            (1, "second"),
        )

    def test_string_keys_sort_by_code_point(self) -> None:
        d = dict_.from_list((("b", 1), ("B", 2), ("a", 3), ("A", 4), ("ab", 5)))
        assert dict_.keys(d) == ("A", "B", "a", "ab", "b")

    def test_tuple_keys(self) -> None:
        d = dict_.from_list((((2, "a"), "x"), ((1, "b"), "y"), ((1, "a"), "z")))
        assert dict_.keys(d) == ((1, "a"), (1, "b"), (2, "a"))
        assert dict_.get((1, "a"), d) == Just("z")
        assert dict_.get((1, "c"), d) == Nothing()
        assert dict_.member((2, "a"), d)

    def test_list_keys(self) -> None:
        d = dict_.from_list((([2, 1], "x"), ([1, 9], "y")))
        # A list key is stored as a tuple, so a later change cannot reorder it.
        assert dict_.keys(d) == ((1, 9), (2, 1))
        assert dict_.get([2, 1], d) == Just("x")

    def test_float_and_int_keys_mix(self) -> None:
        d = dict_.from_list(((1.5, "x"), (1, "y"), (-2, "z")))
        assert dict_.keys(d) == (-2, 1, 1.5)

    def test_keys_that_are_not_comparable_raise(self) -> None:
        with pytest.raises(TypeError):
            dict_.from_list(((1, "x"), ("a", "y")))  # type: ignore[arg-type]
        with pytest.raises(TypeError):
            dict_.insert(1, 0, ABC)  # type: ignore[arg-type]

    def test_insert_replaces_an_existing_key_and_does_not_mutate(self) -> None:
        d1 = dict_.insert("a", 10, ABC)
        assert dict_.to_list(d1) == (("a", 10), ("b", 2), ("c", 3))
        assert dict_.to_list(ABC) == (("a", 1), ("b", 2), ("c", 3))
        assert dict_.to_list(dict_.insert("d", 4, ABC)) == (
            ("a", 1),
            ("b", 2),
            ("c", 3),
            ("d", 4),
        )
        assert dict_.to_list(dict_.insert("0", 0, ABC)) == (
            ("0", 0),
            ("a", 1),
            ("b", 2),
            ("c", 3),
        )

    def test_dict_is_frozen(self) -> None:
        with pytest.raises(dataclasses.FrozenInstanceError):
            ABC.entries = ()  # type: ignore[misc]

    def test_remove(self) -> None:
        assert dict_.to_list(dict_.remove("b", ABC)) == (("a", 1), ("c", 3))
        assert dict_.remove("z", ABC) == ABC
        assert dict_.size(ABC) == 3

    def test_update_inserts_changes_and_removes(self) -> None:
        def inc(m: Maybe[int]) -> Maybe[int]:
            match m:
                case Just(value):
                    return Just(value + 1)
                case Nothing():
                    return Just(0)

        assert dict_.to_list(dict_.update("a", inc, ABC)) == (
            ("a", 2),
            ("b", 2),
            ("c", 3),
        )
        assert dict_.to_list(dict_.update("d", inc, ABC)) == (
            ("a", 1),
            ("b", 2),
            ("c", 3),
            ("d", 0),
        )
        assert dict_.to_list(dict_.update("b", lambda _m: Nothing(), ABC)) == (
            ("a", 1),
            ("c", 3),
        )
        assert dict_.update("z", lambda _m: Nothing(), ABC) == ABC


class TestQuery:
    def test_get_and_member(self) -> None:
        assert dict_.get("b", ABC) == Just(2)
        assert dict_.get("z", ABC) == Nothing()
        assert dict_.get("a", dict_.empty()) == Nothing()
        assert dict_.member("c", ABC)
        assert not dict_.member("z", ABC)

    def test_size_keys_values(self) -> None:
        assert dict_.size(dict_.empty()) == 0
        assert dict_.size(ABC) == 3
        assert dict_.keys(ABC) == ("a", "b", "c")
        assert dict_.values(ABC) == (1, 2, 3)


class TestTransform:
    def test_map(self) -> None:
        assert dict_.to_list(dict_.map(lambda k, v: f"{k}{v}", ABC)) == (
            ("a", "a1"),
            ("b", "b2"),
            ("c", "c3"),
        )

    def test_foldl_ascending_and_foldr_descending(self) -> None:
        assert dict_.foldl(lambda k, _v, acc: acc + k, "", ABC) == "abc"
        assert dict_.foldr(lambda k, _v, acc: acc + k, "", ABC) == "cba"
        assert dict_.foldl(lambda _k, v, acc: acc + v, 0, ABC) == 6

    def test_filter_and_partition(self) -> None:
        assert dict_.to_list(dict_.filter(_is_odd_value, ABC)) == (("a", 1), ("c", 3))
        odd, even = dict_.partition(_is_odd_value, ABC)
        assert dict_.to_list(odd) == (("a", 1), ("c", 3))
        assert dict_.to_list(even) == (("b", 2),)


class TestCombine:
    def test_union_prefers_the_first_dict(self) -> None:
        other = dict_.from_list((("b", 20), ("d", 4)))
        assert dict_.to_list(dict_.union(ABC, other)) == (
            ("a", 1),
            ("b", 2),
            ("c", 3),
            ("d", 4),
        )
        assert dict_.to_list(dict_.union(other, ABC)) == (
            ("a", 1),
            ("b", 20),
            ("c", 3),
            ("d", 4),
        )

    def test_intersect_keeps_values_from_the_first_dict(self) -> None:
        other = dict_.from_list((("b", "x"), ("c", "y"), ("d", "z")))
        assert dict_.to_list(dict_.intersect(ABC, other)) == (("b", 2), ("c", 3))

    def test_diff_removes_keys_found_in_the_second_dict(self) -> None:
        other = dict_.from_list((("b", "x"), ("z", "y")))
        assert dict_.to_list(dict_.diff(ABC, other)) == (("a", 1), ("c", 3))

    def test_merge_visits_keys_in_ascending_order(self) -> None:
        left = dict_.from_list((("a", 1), ("c", 3), ("b", 2)))
        right = dict_.from_list((("b", "B"), ("d", "D"), ("c", "C")))
        result = dict_.merge(
            lambda k, a, acc: (*acc, f"left {k} {a}"),
            lambda k, a, b, acc: (*acc, f"both {k} {a} {b}"),
            lambda k, b, acc: (*acc, f"right {k} {b}"),
            left,
            right,
            (),
        )
        assert result == ("left a 1", "both b 2 B", "both c 3 C", "right d D")


class TestEquality:
    def test_structural_equality_is_independent_of_insertion_order(self) -> None:
        empty: Dict[str, int] = dict_.empty()
        d1 = dict_.insert("c", 3, dict_.insert("a", 1, dict_.insert("b", 2, empty)))
        d2 = dict_.insert("a", 1, dict_.insert("b", 2, dict_.insert("c", 3, empty)))
        assert d1 == d2
        assert basics.equal(d1, d2)
        assert basics.equal(d1, ABC)
        assert not basics.equal(d1, dict_.insert("a", 9, d2))
        assert hash(d1) == hash(d2)
        assert d1.entries == (("a", 1), ("b", 2), ("c", 3))
