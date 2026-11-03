///|
/// Typed integer bindings for the polymorphic Morphir Basics operations.
pub fn add_int(a : @integer.Integer, b : @integer.Integer) -> @integer.Integer {
  @integer.add(a, b)
}

///|
pub fn subtract_int(
  a : @integer.Integer,
  b : @integer.Integer,
) -> @integer.Integer {
  @integer.subtract(a, b)
}

///|
pub fn multiply_int(
  a : @integer.Integer,
  b : @integer.Integer,
) -> @integer.Integer {
  @integer.multiply(a, b)
}

///|
pub fn integer_divide(
  a : @integer.Integer,
  b : @integer.Integer,
) -> @integer.Integer raise @sdk.SdkError {
  @integer.divide(a, b)
}

///|
pub fn power_int(
  a : @integer.Integer,
  b : @integer.Integer,
) -> @integer.Integer raise @sdk.SdkError {
  @integer.power(a, b)
}

///|
pub fn mod_by(
  divisor : @integer.Integer,
  value : @integer.Integer,
) -> @integer.Integer raise @sdk.SdkError {
  @integer.mod_by(divisor, value)
}

///|
pub fn remainder_by(
  divisor : @integer.Integer,
  value : @integer.Integer,
) -> @integer.Integer raise @sdk.SdkError {
  @integer.remainder_by(divisor, value)
}

///|
pub fn negate_int(a : @integer.Integer) -> @integer.Integer {
  @integer.negate(a)
}

///|
pub fn abs_int(a : @integer.Integer) -> @integer.Integer {
  @integer.abs(a)
}

///|
pub fn clamp_int(
  low : @integer.Integer,
  high : @integer.Integer,
  value : @integer.Integer,
) -> @integer.Integer {
  if value < low {
    low
  } else if value > high {
    high
  } else {
    value
  }
}

///|
pub fn to_float(value : @integer.Integer) -> Double {
  @integer.to_float(value)
}

///|
pub fn round(value : Double) -> @integer.Integer raise @sdk.SdkError {
  @integer.from_float_truncated(value.round())
}

///|
pub fn floor(value : Double) -> @integer.Integer raise @sdk.SdkError {
  @integer.from_float_truncated(value.floor())
}

///|
pub fn ceiling(value : Double) -> @integer.Integer raise @sdk.SdkError {
  @integer.from_float_truncated(value.ceil())
}

///|
pub fn truncate(value : Double) -> @integer.Integer raise @sdk.SdkError {
  @integer.from_float_truncated(value)
}

///|
pub fn add_float(a : Double, b : Double) -> Double {
  a + b
}

///|
pub fn subtract_float(a : Double, b : Double) -> Double {
  a - b
}

///|
pub fn multiply_float(a : Double, b : Double) -> Double {
  a * b
}

///|
pub fn divide(a : Double, b : Double) -> Double {
  a / b
}

///|
pub fn power_float(a : Double, b : Double) -> Double {
  @math.pow(a, b)
}

///|
pub fn negate_float(value : Double) -> Double {
  -value
}

///|
pub fn abs_float(value : Double) -> Double {
  if value < 0.0 {
    -value
  } else {
    value
  }
}

///|
pub fn clamp_float(low : Double, high : Double, value : Double) -> Double {
  if value < low {
    low
  } else if value > high {
    high
  } else {
    value
  }
}

///|
pub fn is_nan(value : Double) -> Bool {
  value.is_nan()
}

///|
pub fn is_infinite(value : Double) -> Bool {
  value.is_inf()
}

///|
pub fn sqrt(value : Double) -> Double {
  value.sqrt()
}

///|
pub fn log_base(base : Double, value : Double) -> Double {
  @math.ln(value) / @math.ln(base)
}

///|
pub fn e() -> Double {
  @math.exp(1.0)
}

///|
pub fn pi() -> Double {
  @math.PI
}

///|
pub fn cos(value : Double) -> Double {
  @math.cos(value)
}

///|
pub fn sin(value : Double) -> Double {
  @math.sin(value)
}

///|
pub fn tan(value : Double) -> Double {
  @math.tan(value)
}

///|
pub fn acos(value : Double) -> Double {
  @math.acos(value)
}

///|
pub fn asin(value : Double) -> Double {
  @math.asin(value)
}

///|
pub fn atan(value : Double) -> Double {
  @math.atan(value)
}

///|
pub fn atan2(y : Double, x : Double) -> Double {
  @math.atan2(y, x)
}

///|
pub fn degrees(value : Double) -> Double {
  value * pi() / 180.0
}

///|
pub fn radians(value : Double) -> Double {
  value
}

///|
pub fn turns(value : Double) -> Double {
  value * 2.0 * pi()
}

///|
pub fn to_polar(point : (Double, Double)) -> (Double, Double) {
  let (x, y) = point
  (@math.hypot(x, y), atan2(y, x))
}

///|
pub fn from_polar(polar : (Double, Double)) -> (Double, Double) {
  let (radius, angle) = polar
  (radius * cos(angle), radius * sin(angle))
}

///|
pub fn[A : Eq] equal(a : A, b : A) -> Bool {
  a == b
}

///|
pub fn[A : Eq] not_equal(a : A, b : A) -> Bool {
  a != b
}

///|
/// For Text values use typed *_string bindings for Elm UTF-16 lexical order.
pub fn[A : Compare] less_than(a : A, b : A) -> Bool {
  a < b
}

///|
pub fn[A : Compare] greater_than(a : A, b : A) -> Bool {
  a > b
}

///|
pub fn[A : Compare] less_than_or_equal(a : A, b : A) -> Bool {
  a <= b
}

///|
pub fn[A : Compare] greater_than_or_equal(a : A, b : A) -> Bool {
  a >= b
}

///|
pub fn[A : Compare] max(a : A, b : A) -> A {
  if a > b {
    a
  } else {
    b
  }
}

///|
pub fn[A : Compare] min(a : A, b : A) -> A {
  if a < b {
    a
  } else {
    b
  }
}

///|
pub fn[A : Compare] compare(a : A, b : A) -> @sdk.Order {
  if a < b {
    @sdk.LT
  } else if a > b {
    @sdk.GT
  } else {
    @sdk.EQ
  }
}

///|
pub fn compare_int(a : @integer.Integer, b : @integer.Integer) -> @sdk.Order {
  @integer.compare(a, b)
}

///|
pub fn compare_string(a : @sdk.Text, b : @sdk.Text) -> @sdk.Order {
  @sdk.text_order(a, b)
}

///|
pub fn compare_float(a : Double, b : Double) -> @sdk.Order raise @sdk.SdkError {
  if a.is_nan() || b.is_nan() {
    raise @sdk.InvalidComparison("NaN")
  }
  @sdk.order(if a < b { -1 } else if a > b { 1 } else { 0 })
}

///|
pub fn less_than_string(a : @sdk.Text, b : @sdk.Text) -> Bool {
  compare_string(a, b) == @sdk.LT
}

///|
pub fn greater_than_string(a : @sdk.Text, b : @sdk.Text) -> Bool {
  compare_string(a, b) == @sdk.GT
}

///|
pub fn max_string(a : @sdk.Text, b : @sdk.Text) -> @sdk.Text {
  if greater_than_string(a, b) {
    a
  } else {
    b
  }
}

///|
pub fn min_string(a : @sdk.Text, b : @sdk.Text) -> @sdk.Text {
  if less_than_string(a, b) {
    a
  } else {
    b
  }
}

///|
pub fn append_string(a : @sdk.Text, b : @sdk.Text) -> @sdk.Text {
  let units = a.to_units()
  for unit in b.to_units() {
    units.push(unit)
  }
  @sdk.Text::from_units(units)
}

///|
pub fn[A] append_list(a : @list.List[A], b : @list.List[A]) -> @list.List[A] {
  @list.append(a, b)
}

///|
pub fn not_(value : Bool) -> Bool {
  !value
}

///|
pub fn and_(a : Bool, b : Bool) -> Bool {
  a && b
}

///|
pub fn or_(a : Bool, b : Bool) -> Bool {
  a || b
}

///|
pub fn xor(a : Bool, b : Bool) -> Bool {
  a != b
}

///|
pub fn[A] identity(value : A) -> A {
  value
}

///|
pub fn[A, B] always(value : A, _ignored : B) -> A {
  value
}

///|
pub fn[A, B, C] compose_left(
  g : (B) -> C raise?,
  f : (A) -> B raise?,
  value : A,
) -> C raise? {
  g(f(value))
}

///|
pub fn[A, B, C] compose_right(
  f : (A) -> B raise?,
  g : (B) -> C raise?,
  value : A,
) -> C raise? {
  g(f(value))
}
