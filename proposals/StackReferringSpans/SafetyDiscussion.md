## Safety rules for stack allocated spans and related stuff


Stack-referring spans bring a set of interesting challenges to the problem of safety as enforced/guaranteed by the language. Since the stack space is reused by subsequent calls, it is important that when a method returns no references to its locals remain reachable.

To say simply – references to expired locals can result in type safety violations and GC holes. 
We do not want that in code not marked as “unsafe” and not engaging in other known tricks of similar nature. 

The most trivial way to expose a reference to local state via a span is to return a stack allocated span:

```CS
        static void Main(string[] args)
        {
            var danger = Test1();
            CallSomethingElse(danger);
        }

        static Span<int> Test1()
        {
            // leaking a ref to expired frame. Should be an error!!!
            return stackalloc int[10];
        }
```

It is obvious that stackalloc must be tracked as unsafe to return and that property propagated. A most precise solution would be tracking the flow of all the values within a method for the purpose of the return safety. 
That is also the most expensive (if tractable at all) approach. 

An example of code that method-wide flow analysis could resolve:
(but it would not be easy/cheap, especially if more variables are involved)

```CS
        static Span<int> Test1()
        {
            Span<int> notsafe  = stackalloc int[10];
            Span<int> safe = GetSomeSafeValue();

            label:

            try
            {
                SomeMethod();
            }
            catch
            {
                // swap values
                var temp = safe;
                safe = notsafe;
                notsafe = safe;
                goto label;
            }
            finally
            {
                // swap values
                var temp = safe;
                safe = notsafe;
                notsafe = safe;
            }

            // no errors since all paths here result in odd number of value swaps (1, 3, 5, …)
            return notsafe;
        }
```

We generally prefer a simpler approach:

-	Designate certain expressions (stackalloc) as unsafe to return.
-	flow only through individual expressions. (no control flow and in particular no branches going backwards or EH)
-	flow through locals, but require that unsafe-to return property of a local is established when it is initialized. 
If local is not initialized at declaration it defaults to “safe”. 
-	Assignments of unsafe values to safe locals is not allowed.

```CS
        static Span<int> Test1()
        {
            Span<int> notsafe  = stackalloc int[10];

            // an error here
            Span<int> safe = GetSomeSafeValue();
notsafe;

            // no error here since we do only local analysis
            return safe;
        }
```

A case with ref locals may need another example, but there is nothing controversial there. When a ref local is bound to a variable it assumes the safe-to-return properties of that variable.

```CS
        static Span<int> Test1()
        {
            Span<int> notsafe1 = stackalloc int[10];
            Span<int> safe1 = default;

            ref Span<int> notsafe2 = ref notsafe1;
            ref Span<int> safe2 = ref safe1;

            // an error here
            safe2 = notsafe2;

            // no error here since we do only local analysis
            return safe2;
        }
```

There are other ways to assign though. In particular, a callee can assign to ref parameters. As long as the parameters are ordinary refs.

```CS
        static Span<int> Test1()
        {
            Span<int> notsafe = stackalloc int[10];
            Span<int> safe = default;

            // must be an error
            Assign(ref safe, notsafe);

            // no error here since we do only local analysis
            return safe;
        }

        static void Assign<T>(ref Span<T> to, Span<T> from)
        {
            // I cannot not know here if this is unsafe
            to = from;
        }
```

The call to Assign above must be an error, but by what rule?

* Proposal #1 -  no passing Span-likes by ref  (rejected)

We initially proposed that Span cannot be passed by an ordinary (not readonly) reference. That would solve the problem since writeable references would not be able to cross method boundary and thus making all assignments visible to analysis.
The rule had two problems:

1)	Most users would not use stack-referring spans, but still cannot pass them by reference?
2)	Span-containing structs would need to be “readonly”. Turns out most of the existing Span-containing structs in the prototype code are mutable.
```CS
     // is this still an error, why?
          SomeOtherMethod(ref safe);
```

The overwhelming feedback was that when forced to have a choice between passing mutable span and span-like references vs. stackalloc spans, users would chose mutable references.

* Proposal #2   no mixing mutable safe and unsafe (current)

If any of the arguments to an invocation are mutable and safe to return span-likes, the other arguments cannot be unsafe to return span-likes. 

```CS
        static Span<int> Test1()
        {
            Span<int> notsafe = stackalloc int[10];
            Span<int> safe = default;

            // an error
            Assign(ref safe, notsafe);

            // not an error
            Assign(ref notsafe, notsafe);

            // not an error
            Assign(ref safe, safe);

            // not an error
            Assign(ref notsafe, safe);

            // no error here since we do only local analysis
            return safe;
        }

        static void Assign<T>(ref Span<T> to, Span<T> from)
        {
            // I cannot not know here if this is unsafe
            to = from;
        }
```

So far it seems that the idea of inferring the safety of the output from expression inputs and “no-mixing” rule are settled. We do not have any better alternatives.

There is a detail to settle though. - What to do with receivers? Are they considered inputs at the call site?


We have 2 choices here – enforce safety at the caller or at the callee.
NOTE, in the case with ordinary refs, we must move the check into callee 
  - because of generics the caller may not know if it deals with a struct, but callee certainly does.
  - it is common for a struct to wrap a heap object and return refs to that, we do not want false positives in this scenario.

That is not a case with ref-likes. 
-	We always know for sure whether the receiver is a ref-like or not.
-	Span-likes cannot be on the heap. What an API on a span-like type returns is very likely to refer to its internal spans.

1)	Enforce at the caller. Or in other words – consider the receiver as a ref input into the call.

```CS
        static Span<int> Test1()
        {
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);
            MySpanLike<int> safe = default;

            // an error here
            // "safe" is a safe ref input + "notsafe" is an unsafe input, we shall not mix
            safe.Assign(notsafe.P1);

            // no error here since we do only local analysis
            return safe.P1;
        }

        ref struct MySpanLike<T>
        {
            private Span<T> f;
            public Span<T> P1 => f;

            public MySpanLike(Span<T> arg)
            {
                // make no assumptions about "this.f"
                f = arg;
            }

            public void Assign(Span<T> from)
            {
                // make no assumptions about "this.f"
                f = from;
            }

            ref T GetTRef()
            {
                // no assumptions about "f" and we normally would not consider the 
                // ref-return safety of the indexer receiver anyways
                return ref f[1];
            }

            MySpanLike<T> MySlice(int size)
            {
                // no assumptions about "f" 
                // let the caler to sort this out.
                return new MySpanLike<T>(f.Slice(size));
            }
        }
```


2)	Enforce at the callee. – Ignore safety of instance at the caller site and treat “this” conservatively when inside of a span-like type.

Two sub choices here – do this for “what comes out” and do this for the “cannot mix mutable safe and unsafe inputs”. 

2a)  do not consider receiver as an input for the purpose of “what comes out” analysis

```CS
        static Span<int> Test1()
        {
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);
            MySpanLike<int> safe = default;

            // no errors here
            // P1 does not receive any formal parameters and “notsafe” is ignored for 
            // the purpose of "what comes out" aanlysis, therefore “notsafe.P1” must be safe
            safe.Assign(notsafe.P1);

            // no error here since we do only local analysis
            return safe.P1;
        }

        ref struct MySpanLike<T>
        {
            private Span<T> f;

            // error here. 
            // "this" is not returnable 
            // (this is why caller can ignore receiver in its analysis)
            public Span<T> P1 => f;

            public MySpanLike(Span<T> arg)
            {
                // make no assumptions about "this.f"
                f = arg;
            }

            public void Assign(Span<T> from)
            {
                // make no assumptions about "this.f"
                f = from;
            }

            ref T this[int index] 
            {
                get
                {
                    // this needs to be an error if we do not validate receiver at caller side, 
                    // otherwise caller will not see the danger.
                    //
                    // but to make this an error we must validate the receiver at the caller side here
                    // it seems a bit inconsistent, this also seems to be breaking a generally useful scenario
                    return ref f[index];
                }
            }

            MySpanLike<T> MySlice(int size)
            {
                // this needs to be an error if we do not validate receiver at caller side, 
                // otherwise caller will not see the danger.
                //
                // but to make this an error we must validate the receiver at the caller side here
                // it seems a bit inconsistent, this also seems to be breaking a generally useful scenario
                return new MySpanLike<T>(f.Slice(size));
            }
        }
```

2b)  do not consider receiver as an input for the purpose of “cannot mix mutable safe and unsafe inputs” analysis

```CS
        static Span<int> Test1()
        {
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);
            MySpanLike<int> safe = default;

            // no errors here
            // "safe" is ignored as a ref input to "Assign" and otherwise we do not see any mixing
            safe.Assign(notsafe.P1);

            // no error here since we do only local analysis
            return safe.P1;
        }

        ref struct MySpanLike<T>
        {
            private Span<T> f;
            public Span<T> P1 => f;

            public MySpanLike(Span<T> arg)
            {
                // make no assumptions about "this.f"
                f = arg;
            }

            public Span<T> Assign(Span<T> from)
            {
                // this is ok, "default" is returnable
                this = default;

                // error here, for the purpose of "this" assignments 
                // "this" must be conservatively assumed as safe to return, while parameter is not. 
                f = from;

                // this is ok, for the purpose of returning, "from" is returnable
                return from;
            }
        }
```


I believe we should prefer option #1.      Definitely not 2a.

Yes, doing receiver safety at the caller is different form the rules of ref returns. The reasons are:
-	unlike the case of ref returns, the enforcement _can_ happen at the caller (since no generics)
-	it seems like moving enforcement in to the callee is more limiting/confusing than if it is at the caller.


### Spans and assignments to fields/members

Before spans, we did not have to deal with complex LHS of an assignment. 
That was because there are no such things as byref fields and therefore members cannot be a target of a ref assignment in general. 

The only kind of variable that could be ref assigned was a local and it could only be assigned once at the time of initialization where it could not fail due to returnability mismatch.

Ref-like types do have mutable ref-like fields that can be reassigned.

We already have uncontroversial rule that a safe-to-return ref-like variable cannot be assigned an unsafe-to-return value. 
We need to extend that to include not only locals, but also instance members of ref-like types. (They are all structs BTW)

So for the LHS member expression the return safety will be defined by its receiver and it ref parameters if they have ref-like types.

Examples:
```CS
    class Program
    {

        static void Main(string[] args)
        {
            MySpanLike<int> safe = new MySpanLike<int>(new int[10]);
            MySpanLike<int> notsafe = stackalloc int[10];

            // should be an error
            safe.P1 = notsafe.P1;

            // error
            safe.M1(ref safe) = notsafe.P1;

            // error
            // this is a false-positive
            // there is no way for the M1 to ref-return its fields
            safe.M1(ref notsafe) = notsafe.P1;
        }

        ref struct MySpanLike<T>
        {
            private Span<T> f;

            public MySpanLike(Span<T> arg)
            {
                // make no assumptions about "this.f"
                f = arg;
            }

            public Span<T> P1
            {
                get => f;
                set => f = value;
            }

            public ref Span<T> M1(ref MySpanLike<T> other)
            {
                return ref other.f;
            }
        }
    }
```

NOTE: there is a false positive in the case of “safe.M1(ref notsafe) = notsafe.P1;”
We can patch this up by modifying the receiver validation rules as:

-	When a member is used as a target of an assignment or passed by reference its safety is determined by the receiver and byref arguments.
-	When a member is used as a value, its safety its determined by all arguments and the receiver as long as they have ref-like type (regardless if byref or byvalue)
-	When a member returns by reference, its receiver is not considered as an input to its safety. (since “this” is not ref-returnable in a struct)


### Note that there are two metrics of safety:

1)	Can we return by value? 
(not ref-like typed expressions just say “yes”, except few cases like setter-only properties)
2)	Can we return by reference? 

If something cannot be returned by value, it cannot be returned by reference. Indeed – otherwise you would just read the value from the reference

The other way is not true though:
-	An byval parameter is safe to return by value, but not safe to return by reference.
-	An ordinary local of a ref-like type (not stack-allocated) is safe to return by value, but not safe to return by reference.
-	“this” is safe to return by value, but not safe to return by reference.

```CS
            public Span<T> M1()
            {
                var local = new Span<T>(new T[10]);

                // ok
                return local;
            }

            public ref Span<T> M1()
            {
                var local = new Span<T>(new T[10]);

                // error
                return ref local;
            }
```
