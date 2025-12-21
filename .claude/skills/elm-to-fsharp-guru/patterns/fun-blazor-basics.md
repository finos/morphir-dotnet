# Fun.Blazor Basics

Converting Elm Architecture UIs to Fun.Blazor.

## The Elm Architecture

```elm
type Model = { count : Int }
type Msg = Increment | Decrement

update : Msg -> Model -> Model
update msg model =
    case msg of
        Increment -> { model | count = model.count + 1 }
        Decrement -> { model | count = model.count - 1 }

view : Model -> Html Msg
view model =
    div []
        [ button [ onClick Decrement ] [ text "-" ]
        , div [] [ text (String.fromInt model.count) ]
        , button [ onClick Increment ] [ text "+" ]
        ]
```

## Fun.Blazor Translation

```fsharp
open Fun.Blazor

type Model = { Count: int }
type Msg = Increment | Decrement

let update (msg: Msg) (model: Model) : Model =
    match msg with
    | Increment -> { model with Count = model.Count + 1 }
    | Decrement -> { model with Count = model.Count - 1 }

let view (model: Model) (dispatch: Msg -> unit) =
    adaptiview() {
        div {
            button {
                onclick (fun _ -> dispatch Decrement)
                "-"
            }
            div { $"Count: {model.Count}" }
            button {
                onclick (fun _ -> dispatch Increment)
                "+"
            }
        }
    }
```

## Key Differences

- `Html Msg` → `adaptiview()` CE
- `onClick` → `onclick` (lowercase)
- Explicit `dispatch` function parameter

## References

- [Fun.Blazor](https://github.com/slaveOftime/Fun.Blazor)
- [Elmish](https://elmish.github.io/elmish/)

## History

**Version:** 1.0  
**Created:** 2025-12-21
