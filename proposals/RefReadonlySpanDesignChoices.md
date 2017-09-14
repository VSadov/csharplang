 
# Design decisions made on the way to ref/span features ##

Most of these decisions logically follow from the general directions as discussed in LDM meetings. In many cases they were vetted in LDM meeting by quick checks, in email or in online spec discussions. They are collected here to make sure they are recorded in one place and to make sure once more that all stakeholders are ok with this going forward.
  
This could also be a good resource when writing a testplan or a spec for these features.

## where `readonly ref` variables are allowed
- `ref readonly` parameters and returns are allowed anywhere where a byval parameters are allowed.
This includes indexers, operators (including conversions), delegates, lambdas, local functions.  
- `ref readonly` returns allowed anywhere where `ref` returns are allowed. I.E. indexers, operators (including conversions), delegates, lambdas, local functions, but not in operators.
- There are no `ref readonly` locals.
- There are no warnings on `ref [readonly]` with reference types and primitives.
It may be pointless in general, but in some cases user must/want to pass primitives as `ref readonly`. Examples - overriding a generic method like ` Method(ref readonly T param)` when T was substituted to be `int`, or when having methods like `Volatile.Read(ref readonly int location)`

Some cases where `ref readonly` is not allowed can be allowed in the future if there is a need.

## aliasing in general
- `ref readonly` arguments are not required to be lvalues. When argument is not an lvalue, a temporary is used.
- `ref readonly` parameter may have default values. When not specified by the call site, they are passed via a temporary.
- `ref readonly` arguments may have implicit conversions, including those that do not preserve identity. A temporary is used in those cases.
- the life time of the temporaries matches at least the closest encompassing scope. (in reality could be smaller if no observable difference).   
- for the purpose of lambda/async capturing `in` parameters are still opaque references and as such cannot be lifted. 

## syntax for `ref readonly` arguments
- `ref readonly` arguments look exactly the same as ordinary byval parameters - no modifiers. This includes arguments of operators, and receivers of extension methods.

```cs
void (ref readonly int arg){...};

M1(obj.field);
M1(42);
M1();

public static T1 operator +(ref readonly T1 x, ref readonly T1 y) {...};

a + b;
```

- returns in a `ref readonly` methods must specify `ref` to signify that reference is taken. (Note we never return a ref of a copy)

```cs
ref readonly string M() => ref String.Empty;

// error - needs 'ref'
ref readonly string M() => "qq";   

// error - not an lvalue
ref readonly string M() => ref "qq";   
```

## binding and overload resolution
- since the user provides no modifiers at the call site, the signatures will equally match ordinary byval parameters and `ref readonly` parameters. If both are present, an ambiguity error is reported.
- in fact for the purpose of overload resolution `ref readonly` parameters behave as effectively byval parameters.   
  
## delegate conversions and method type inference
- since both delegates and lambdas can express RefKind of parameters, the parameter RefKinds of successful conversion candidates must match, similarly to how it works with `ref/out` parameters.
- similarly to `ref/out`, `ref readonly` is ignored in the process of method type inference, except for the purposes of variance. 
- for the purpose of variance `ref readonly` is considered non-variant.

```C#
class Program
{
    delegate void DRef<T>(ref T arg);
    delegate void DIn<T>(ref readonly T arg);

    static T Generic<T>(DIn<T> arg1, DIn<T> arg2) { return default; }

    static void Main()
    {
        // error - parameter ref kind must match
        DRef<Exception> d1 = (ref readonly Exception arg) => throw null;

        // error - T inference is nonvariant/exact and therefore ambiguous
        Generic((ref readonly Exception arg) => throw null, (ref readonly object arg) => throw null);
    }
}

``` 

## async and spilling
- async methods cannot have `ref readonly` parameters or returns 
- at call sites, unlike `ref` arguments, `ref readonly` arguments never cause spilling related errors. 
User does not specify `ref` or `out` and as such spilling errors would be an unexpected nuisance. 
- when spilling by reference is possible (fields, array elements), we spill by reference and preserve aliasing.
- when spilling by reference is **not** possible (ref methods, ref ternary), we spill by value. 
- NOTE: in rare cases this may cause an observable difference between calling with `await` in the signature and without. 
Ordinary `ref` in these cases just produces an error.  
```C#
void M1(ref readonly int arg1, ref readonly int arg2){...};
ref int M2() {...};
async Task<int> M3() {...};

// valid. first argument is spilled via a copy
M1(M2(), await M3()){...} 

```
    
## ref structs.
- The syntax is `ref struct S1{ . .}` 
- A particular nuance here – `ref` is a **contextual** modifier. For historical reasons `ref` in declarations can also be a ref-type-operator. Therefore it must be contextual to avoid syntactical ambiguities. 
- The disambiguating context here is "immediately preceeding `struct` keyword".
- There is an interaction with another contextual keyword in this space - `partial`. We now allow `partial` to be used before `ref struct`.
```cs
// valid
public unsafe ref struct S1{}  

// also valid
unsafe readonly public partial ref struct S1{}

// not valid - 'ref' must go immediately before 'struct'
unsafe ref readonly public partial struct S1{}
  
```
- NOTE: there is no requirement for modifiers to match between parts of a partial type. That is done to facilitate code-generator scenarios. For the eventual semantic meaning we consider the union of all modifiers across partials. 
`ref` is not an exception from the existing rules.
```cs
// effectively a 'public readonly ref struct S1{..}' 

public partial struct S1{}  
partial ref struct S1{}  
readonly partial struct S1{}  

```    
## readonly structs
- the syntax is `readonly struct`.
- `this` is a `ref readonly` variable in all members except constructors.
- the `readonly` here is a true modifier and can be specified in any order with other modifiers.
- `readonly` structs cannot have writeable fields, autoprops or field-like events

## stack-refering spans 
- the syntax is `Span<int> sp = stackalloc int[100];`
- `stackalloc int[100]` is now treated as a stack-allocated array literal and can be target-typed to `Span<T>` where `T` must match the type of the array.
- we support target typing specifically to a well-known type `System.Span<T>`.
- the StackAllocToSpan conversion is a standard conversion and can "stack" with user defined operators to form user-defined conversion, including implicit conversions. 
The following is valid:
```cs
// Span<T> has implicit conversion to ReadOnlySpan<T>
ReadOnlySpan<int> sp = stackallock int[10]; 

```  
- safety rules for ref-like structs:  
Doc: https://github.com/dotnet/csharplang/blob/master/proposals/span-safety.md

## `ref [readonly]` extension methods

we now support the following 3 cases:
- `T this` – existing case, ok for any kind of receiver type.  
- `ref T this` – ok with structs or with generics constrained to structs  
- `ref readonly T this` – ok with actual structs, but **not** ok with generic type parameter T regardless of constraints.  

The purpose of `ref readonly` is to avoid unnecessary copy, but with generic types, nearly all uses inside the extension will have to be done through interface methods and `ref readonly` receiver will need to be copied every time. As a result the user will actually **increase** implicit copying, possibly dramatically.
It is never a good thing to use `ref readonly` with generics. We do not want this to lead user on wrong path. 

- receiver of a `ref` extension method must be an lvalue. Invoking a `ref` extension on a readonly field or an rvalue is an error.
- `ref` receiver requires that receiver is identity-convertible to the type of `this` parameter. We do not make copies when invoking a method whose purpose is to mutate.
- invoking a `ref readonly` extension on a readonly field or an rvalue is not an error.
- `ref readonly` receiver will permit implicit conversions in the same way as static method would permit.

NOTE: It is always possible to go from an extension method syntax to a static method invocation. This is preexisting design constraint since user could be forced to do the substitution in ambiguous situations.

## metadata representation
- readonly struct in metadata is just a struct decorated with `[IsReadOnly]` attribute
- ref struct is a struct with an [IsByRefLike]` attribute  
- [ref readonly] parameters/returns are byref parameters decorated with `[IsReadOnly]` attributes.
- attributes used by these features are not allowed to be used directly in source. This is breaking in compiler upgrade scenario, but was historically done to not having to rationalize numerous combinations of these attributes and features that use them if such use is allowed.   
- attributes used by these feature are either found in the containing compilation or "embedded" by the compile as a private type in the containing assembly.
- embedded attributes are decorated with `[Embedded]` which itself is always embedded and visible/bindable from the source (so that user could not "embed" source types).     

## metadata poisoning
- `ref readonly` parameters are marked with `modreq(InAttribute)`, except for parameters of methods that cannot be "overriden" with a concrete implementation. This is done to prevent non-enlightened compiler to provide an implementation that does not respect the contract.  
- Delegate/interface methods are considered "overridable" for the purpose above.
- `ref readonly` returns are always marked with `modreq(InAttribute)`. This is done to prevent non-enlightened compiler writing through the reference. 
- `ref structs` are marked with `ObsoleteAttribute(“Types with embedded references are not supported in this version of your compiler.”, error=true)`. This is done to prevent non-enlightened compiler to use the types in unsafe ways.     
- `ref structs` are poisoned conditionally. If a method is already [Obsolete] or [Deprecated]. We honor the attribute supplied by the user and cannot emit ours without a clash. We may consider giving a warning for such cases, but none is given right now. 
- poisoning is optional when importing metadata to simplify the contract. When overriding "unpoisoned" signatures we will keep them "unpoisoned".
