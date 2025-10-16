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
