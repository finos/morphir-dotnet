Feature: Classic Type Morphir JSON Format Serialization

This feature is about serializing and deserializing the classic Type structure in the Morphir JSON format.

    Rule: Serializing for FormatVersion 2 should include Names serialized as arrays

        Background:
            Given we are using FormatVersion "2"

        Scenario: Deserializing and serializing a Classic Variable Type FormatVersion 2 should support round-tripping
            Given a classic Morphir IR Type encoded as:
            """
            ["Variable",{},["t","var"]]
            """
            When we deserialize it
            And we serialize it back
            Then the result should match the original input

        Scenario: Deserializing and serializing a Classic Unit Type FormatVersion 2 should support round-tripping
            Given a classic Morphir IR Type encoded as:
            """
            ["Unit",{}]
            """
            When we deserialize it
            And we serialize it back
            Then the result should match the original input

        Scenario Outline: Deserializing and serializing a Classic Tuple Type FormatVersion 2 should support round-tripping
            Given a classic Morphir IR Type encoded as JSON text: <TypeJson>
            When we deserialize it
            And we serialize it back
            Then the result should match the original input

            Examples:
            | TypeJson                                 |
            | '["Tuple",{},[["Unit",{}],["Unit",{}]]]' |
            | '["Tuple",{},[["Unit",{}],["Variable",{},["t","result"]],["Variable",{},["t"]]]]' |

        Scenario Outline: Deserializing and serializing a Classic Reference Type FormatVersion 2 should support round-tripping
            Given a classic Morphir IR Type encoded as JSON text: <TypeJson>
            When we deserialize it
            And we serialize it back
            Then the result should match the original input

            Examples:
            | TypeJson                                 |
            | '["Reference",{},[[["morphir"]],[["sdk"],["basics"]],["int"]],[]]' |
            | '["Reference",{},[[["morphir"]],[["sdk"],["list"]],["list"]],[["Reference",{},[[["morphir"]],[["sdk"],["string"]],["string"]],[]]]]' |
            | '["Reference",{},[[["my","company"]],[["core","domain"]],["custom","type"]],[]]' |

        Scenario Outline: Deserializing and serializing a Classic Record Type FormatVersion 2 should support round-tripping
            Given a classic Morphir IR Type encoded as JSON text: <TypeJson>
            When we deserialize it
            And we serialize it back
            Then the result should match the original input

            Examples:
            | TypeJson                                 |
            | '["Record",{},[{"name":["name"],"tpe":["Unit",{}]},{"name":["age"],"tpe":["Unit",{}]}]]' |
            | '["Record",{},[{"name":["id"],"tpe":["Variable",{},["a"]]},{"name":["value"],"tpe":["Variable",{},["b"]]}]]' |
            | '["Record",{},[]]' |

        Scenario Outline: Deserializing and serializing a Classic ExtensibleRecord Type FormatVersion 2 should support round-tripping
            Given a classic Morphir IR Type encoded as JSON text: <TypeJson>
            When we deserialize it
            And we serialize it back
            Then the result should match the original input

            Examples:
            | TypeJson                                 |
            | '["ExtensibleRecord",{},["r"],[{"name":["name"],"tpe":["Unit",{}]}]]' |
            | '["ExtensibleRecord",{},["record","var"],[{"name":["field","one"],"tpe":["Variable",{},["a"]]},{"name":["field","two"],"tpe":["Unit",{}]}]]' |
            | '["ExtensibleRecord",{},["r"],[]]' |

        Scenario Outline: Deserializing and serializing a Classic Function Type FormatVersion 2 should support round-tripping
            Given a classic Morphir IR Type encoded as JSON text: <TypeJson>
            When we deserialize it
            And we serialize it back
            Then the result should match the original input

            Examples:
            | TypeJson                                 |
            | '["Function",{},["Unit",{}],["Unit",{}]]' |
            | '["Function",{},["Variable",{},["a"]],["Variable",{},["b"]]]' |
            | '["Function",{},["Function",{},["Unit",{}],["Variable",{},["s"]]],["Unit",{}]]' |
