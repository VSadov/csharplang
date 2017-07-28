Safe to return by value. (in addition to the regular rules)

- expressions that are safe to return by value can be returned by value. - obviously, just to state the purpose.
- expressions that are not safe to return by value are also not safe to return by reference. 

- expressions of regular (not ref-like) types are safe to return by value
- stackalloc expression is not safe to return by value
- “this” is safe to return by value
- expression is safe to return by value when _all_ inputs are safe to return by value
- receiver of member expressions is considered an input (optional: for ref-returning members receiver is not an input, since “this” is not ref returnable)

- if any of arguments (including receiver) to a call are byref ref-like and safe to return by value, there should not be any arguments that are not safe to return

- target of an assignment expression is safe to return by value when _any_ of byref inputs are safe to return by value. (optional: for ref-returning members receiver is not an input, since “this” is not ref returnable)

- expressions that are safe to return by value can not be assigned a value that is not safe to return by value

- ternary ref expression of span-like type requires that operands agree on returnability