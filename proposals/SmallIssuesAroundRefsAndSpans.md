
This is a list of small decisions made in the course of developing features supporting `ref readonly` and spans. In most case they were discussed in some form like email, but may not be included in any particular document.

-	Escape value for ref-like typed locals with no initializers
We currently default to “out of the method” scope. 

-	Accepting “in” parameters in async spiller. Spilling by ref where possible, by val otherwise.
We need to add this to the spec. Tracking bug - https://github.com/dotnet/roslyn/pull/20883

-	poisoning the “ref readonly” for other/old compilers via modreq
We add a modreq when “ref readonly” in any place other than parameters of not overridable. (delegate signatures are considered same as virtual ones)

- ref/in extension methods and struct/generic receivers. 
ref extension methods require that receiver is a struct type
in  extension methods require that receiver is a struct type or a generic type with a struct constraint.

- invoking a “ref” extension on a readonly field/RValue/literal and the like
This is an error. We do not automatically create a copy when invoking extensions whose explicit purpose is to mutate.
This is not an error for "in" extensions as it woudl not be an error if they were used as static methods.

- warnings on “ref readonly” with reference types and primitives
Decided against it. There are legitimate reasons (like VolatileRead) where primitives may be acceptable/desired to be passed as "in". 
There are cases where user will be forced to pass primitives by "in" when overriding a method from generic base type.

