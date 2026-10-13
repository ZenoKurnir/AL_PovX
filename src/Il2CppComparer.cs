using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;

namespace AL_PovX;

// Compares IL2CPP wrappers by native pointer, since two wrappers can point at the same object.
internal class Il2CppComparer<T> : IEqualityComparer<T> where T : Il2CppObjectBase
{
    public bool Equals(T x, T y)
    {
        if (x == null || y == null)
            return x == null && y == null;
        return x.Pointer == y.Pointer;
    }

    public int GetHashCode(T obj) => obj.Pointer.GetHashCode();
}
