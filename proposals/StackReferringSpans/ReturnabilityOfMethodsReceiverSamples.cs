using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp21
{
    class ReturnabilityOfReceiver
    {
        ref struct MySpanLike<T>
        {
            public Span<T> f;

            public MySpanLike(Span<T> arg)
            {
                f = arg;
            }

            // this should work - just a recursive indexer
            // this should result in a returnable value if caller applies this to a stack allocated span
            public T GetTVal()
            {
                return f[1];
            }

            // this should work - just a recursive ref indexer
            // NOTE: it works because we expect that  Span::[] will not return its own field by reference
            //       therefore we do not need to check the receiver. 
            //
            // caller must check receiver for returnability
            public ref T GetTRef()
            {
                return ref f[1];
            }

            // this should work. Trivial sub-slicing.
            // caller must validate receiver
            MySpanLike<T> Slice(int size)
            {
                // no assumptions about "f" 
                // let the caler to sort this out.
                return new MySpanLike<T>(f.Slice(size));
            }

            // this should work, similar to Slice
            public Span<T> GetSpanVal()
            {
                return f;
            }

            public ref Span<T> GetSpanRef(ref Span<T> arg)
            {
                // this should not work per existing rules for structs.
                // we cannot return our part by reference.
                // if we do, then we have a problem with GetTRef/Slice.
                return ref f;
            }

            // should work. 
            // Otherwise we could force span-likes be "readonly" - not what users want
            // caller must validate receiver
            public void Assign(Span<T> from)
            {
                f = from;
            }
        }

        static int Test1()
        {
            MySpanLike<int> safe = new MySpanLike<int>(new int[10]);
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);

            //valid
            return notsafe.GetTVal();
        }

        static Span<int> Test2()
        {
            MySpanLike<int> safe = new MySpanLike<int>(new int[10]);
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);

            // valid
            return safe.Slice(1)

            // error
            return notsafe.Slice(1);

            // valid
            return safe.GetSpanVal()

            // error
            return notsafe.GetSpanVal();
        }

        static ref int Test3()
        {
            MySpanLike<int> safe = new MySpanLike<int>(new int[10]);
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);

            // valid
            return ref safe.GetTRef();

            // error
            return ref notsafe.GetTRef();
        }

        // existing rules
        static ref Span<int> Test4()
        {
            MySpanLike<int> safe = new MySpanLike<int>(new int[10]);
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);

            // valid  (because calee validates), but not useful either way
            return ref safe.GetSpanRef();

            // error   (not returnable even by value)
            return ref notsafe.GetSpanRef();
        }

        // assigning
        static void Test5()
        {
            MySpanLike<int> safe = new MySpanLike<int>(new int[10]);
            MySpanLike<int> notsafe = new MySpanLike<int>(stackalloc int[10]);

            // valid
            notsafe.Assign(safe);
            notsafe.Assign(notsafe);
            safe.Assign(safe);

            // error   (mixing  "ref safe" and unsafe in the same arg list)
            safe.Assign(notsafe);
        }
    }
}
