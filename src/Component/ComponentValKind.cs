#if WASMTIME_DEV

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents the kind of a component model value.
    /// </summary>
    public enum ComponentValKind : byte
    {
        /// <summary>A boolean value.</summary>
        Bool = 0,
        /// <summary>A signed 8-bit integer.</summary>
        S8 = 1,
        /// <summary>An unsigned 8-bit integer.</summary>
        U8 = 2,
        /// <summary>A signed 16-bit integer.</summary>
        S16 = 3,
        /// <summary>An unsigned 16-bit integer.</summary>
        U16 = 4,
        /// <summary>A signed 32-bit integer.</summary>
        S32 = 5,
        /// <summary>An unsigned 32-bit integer.</summary>
        U32 = 6,
        /// <summary>A signed 64-bit integer.</summary>
        S64 = 7,
        /// <summary>An unsigned 64-bit integer.</summary>
        U64 = 8,
        /// <summary>A 32-bit floating-point number.</summary>
        F32 = 9,
        /// <summary>A 64-bit floating-point number.</summary>
        F64 = 10,
        /// <summary>A Unicode character.</summary>
        Char = 11,
        /// <summary>A string value.</summary>
        String = 12,
        /// <summary>A list value.</summary>
        List = 13,
        /// <summary>A record value.</summary>
        Record = 14,
        /// <summary>A tuple value.</summary>
        Tuple = 15,
        /// <summary>A variant value.</summary>
        Variant = 16,
        /// <summary>An enum value.</summary>
        Enum = 17,
        /// <summary>An option value.</summary>
        Option = 18,
        /// <summary>A result value.</summary>
        Result = 19,
        /// <summary>A flags value.</summary>
        Flags = 20,
        /// <summary>A resource value.</summary>
        Resource = 21,
    }
}

#endif
