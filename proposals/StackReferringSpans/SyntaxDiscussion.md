

## Syntax

Syntax for stack allocated spans is basically a target typed stackalloc expression.

Currently stackalloc can be used only as an initializer of a local variable, where it has the type of T*. 

Examples of currently valid uses:
```CS   
            byte* ptr = stackalloc byte[100];
            var ptr = stackalloc byte[100];
```
In particular it is not currently allowed to use stackalloc anywhere else. It is essentially not a general purpose expression that is not allowed anywhere else. That allows us to reuse the syntax. 
We have to make sure that the forms above continue have same meaning though.

The relaxed rules are:

1)	stackalloc T[size]     will be allowed as a general purpose expression
Syntax like “Foo(stackalloc int[100]);” will be generally allowed.
2)	T in T[size] still must be a blittable type. (for now only value type primitives)
3)	stackalloc T[size]     can convert to well-known types   Span<T> / ReadOnlySpan<T>  via an implicit span conversion. 
The underlying implementation actually calls well-known ctor “new Span<T>(void* ptr, int length)” , see 
https://github.com/dotnet/corefx/blob/master/src/System.Memory/src/System/Span.cs#L114 )
4)	stackalloc T[size]     does not have natural type outside of local initializer context.
Example:    the following code will be an error:

```CS
        static T Foo<T>(T arg) => arg;

        static void Main(string[] args)
        {
            var f = Foo(stackalloc int[100]);
        }
```

5)	Within a local initializer context, the natural type of stackalloc stays T* - for compatibility reasons, so that the old syntax means the same as before.

=== Other options considered:

-	Why not      var sp = new Span<int>(stackalloc byte[100]);

It appears more explicit and has its benefits of not making “stackalloc” a general purpose expression. However there are some problems with “magic constructor argument” – what type does it have? 

This is an object creation, so what constructor is called?

If it is Span(void*, int) ctor, than by what language mechanism a stackalloc expression becomes two values? 
Since we explicitly call a ctor that takes a pointer, would the rules of the language make this unsafe or we need another exception from rules?


-	Why not    generalize this to any ref-like struct with appropriate constructor?

What would be the signature of the “appropriate constructor”. 
Is it  MyRefLikeType(void*, int) ?   Seems somewhat limiting, can I have    MyRefLikeType(string dummy, void*, int, void*, int)  to take two stackallocs at the end?

Matching a stackalloc to any general pattern of a pointer and an int seems too loose. What if match is unintentional? Perhaps, if pattern involves constructors that take some aggregate type that packages a pair {pointer, size} instead? 
But then, we already have a way to package a pointer and size using - … tadaaa.. Span<T>.

So, having target typing, if    MyRefLikeType  has a ctor that takes   Span<T>, I already can call it with a  stackalloc.

```CS
        static void Main(string[] args)
        {
            var refStruct = new MyRefStruct<int>(stackalloc int[10]);
        }

        ref struct MyRefStruct<T>
        {
            public MyRefStruct(Span<T> arg)
            {
                // do stuff here, perhaps even get the ptr back through unsafe APIs 
            }
        }
```