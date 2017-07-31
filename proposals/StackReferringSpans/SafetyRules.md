Safe to return by value. (in addition to the regular rules)

- expressions that are safe to return by value can be returned by value. - obviously, just to state the purpose.
- expressions that are not safe to return by value are also not safe to return by reference. 

- expressions of regular (not ref-like) types are safe to return by value
- stackalloc expression is not safe to return by value
- “this” is safe to return by value
- ref-like typed expression is safe to return by value when _all_ value inputs are safe to return by value and all ref inputs are safe to return by reference.
- receiver of member expressions is considered an input (optional: for ref-returning members receiver is not an input, since “this” is not ref returnable)

- if any of arguments (including receiver) to a call are byref ref-like and safe to return by value, there should not be any arguments that are not safe to return (by value or by reference correspondingly)

- target of an assignment expression is safe to return by value when _any_ of byref inputs are safe to return by value. (optional: for ref-returning members receiver is not an input, since “this” is not ref returnable)

- expressions that are safe to return by value can not be assigned a value that is not safe to return by value

- ternary ref expression of span-like type requires that operands agree on returnability



**Proposed rules to ensure future compatibility with single-element Spans around single locals.**

- locals of ref-like type assume their safe-to-return status at the time of declaration. - If there is an initializer and it is not safe to return, then th elocal is not safe to return. Otherwise it is.
- local variable accesses are considered safe-to-return or not depending on their status. 
- locals that are not safe to return are effectively read-only. They cannot be assigned anything after initialization. 
(optional: safe-to-return values could be assigned)


The motivation for the last rule is that we do not want to allow possibility for the ref-like local to refer to locals from multiple scopes.
 