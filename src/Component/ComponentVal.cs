#if WASMTIME_DEV

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents a component model value that can be passed to or received from component functions.
    /// </summary>
    public class ComponentVal : IDisposable
    {
        /// <summary>
        /// Gets the kind of this value.
        /// </summary>
        public ComponentValKind Kind { get; }

        private readonly object? value;

        private ComponentVal(ComponentValKind kind, object? value)
        {
            Kind = kind;
            this.value = value;
        }

        /// <summary>Creates a boolean component value.</summary>
        public static ComponentVal FromBool(bool value) => new ComponentVal(ComponentValKind.Bool, value);

        /// <summary>Creates a signed 8-bit integer component value.</summary>
        public static ComponentVal FromS8(sbyte value) => new ComponentVal(ComponentValKind.S8, value);

        /// <summary>Creates an unsigned 8-bit integer component value.</summary>
        public static ComponentVal FromU8(byte value) => new ComponentVal(ComponentValKind.U8, value);

        /// <summary>Creates a signed 16-bit integer component value.</summary>
        public static ComponentVal FromS16(short value) => new ComponentVal(ComponentValKind.S16, value);

        /// <summary>Creates an unsigned 16-bit integer component value.</summary>
        public static ComponentVal FromU16(ushort value) => new ComponentVal(ComponentValKind.U16, value);

        /// <summary>Creates a signed 32-bit integer component value.</summary>
        public static ComponentVal FromS32(int value) => new ComponentVal(ComponentValKind.S32, value);

        /// <summary>Creates an unsigned 32-bit integer component value.</summary>
        public static ComponentVal FromU32(uint value) => new ComponentVal(ComponentValKind.U32, value);

        /// <summary>Creates a signed 64-bit integer component value.</summary>
        public static ComponentVal FromS64(long value) => new ComponentVal(ComponentValKind.S64, value);

        /// <summary>Creates an unsigned 64-bit integer component value.</summary>
        public static ComponentVal FromU64(ulong value) => new ComponentVal(ComponentValKind.U64, value);

        /// <summary>Creates a 32-bit floating-point component value.</summary>
        public static ComponentVal FromF32(float value) => new ComponentVal(ComponentValKind.F32, value);

        /// <summary>Creates a 64-bit floating-point component value.</summary>
        public static ComponentVal FromF64(double value) => new ComponentVal(ComponentValKind.F64, value);

        /// <summary>Creates a Unicode character component value.</summary>
        public static ComponentVal FromChar(char value) => new ComponentVal(ComponentValKind.Char, (uint)value);

        /// <summary>Creates a string component value.</summary>
        public static ComponentVal FromString(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return new ComponentVal(ComponentValKind.String, value);
        }

        /// <summary>Creates a list&lt;string&gt; component value.</summary>
        public static ComponentVal FromStringList(string[] values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            return new ComponentVal(ComponentValKind.List, values);
        }

        /// <summary>Creates a generic list component value.</summary>
        public static ComponentVal FromList(ComponentVal[] values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            return new ComponentVal(ComponentValKind.List, values);
        }

        /// <summary>Gets the boolean value.</summary>
        public bool GetBool()
        {
            if (Kind != ComponentValKind.Bool) throw new InvalidOperationException($"Value is {Kind}, not Bool.");
            return (bool)value!;
        }

        /// <summary>Gets the signed 8-bit integer value.</summary>
        public sbyte GetS8()
        {
            if (Kind != ComponentValKind.S8) throw new InvalidOperationException($"Value is {Kind}, not S8.");
            return (sbyte)value!;
        }

        /// <summary>Gets the unsigned 8-bit integer value.</summary>
        public byte GetU8()
        {
            if (Kind != ComponentValKind.U8) throw new InvalidOperationException($"Value is {Kind}, not U8.");
            return (byte)value!;
        }

        /// <summary>Gets the signed 16-bit integer value.</summary>
        public short GetS16()
        {
            if (Kind != ComponentValKind.S16) throw new InvalidOperationException($"Value is {Kind}, not S16.");
            return (short)value!;
        }

        /// <summary>Gets the unsigned 16-bit integer value.</summary>
        public ushort GetU16()
        {
            if (Kind != ComponentValKind.U16) throw new InvalidOperationException($"Value is {Kind}, not U16.");
            return (ushort)value!;
        }

        /// <summary>Gets the signed 32-bit integer value.</summary>
        public int GetS32()
        {
            if (Kind != ComponentValKind.S32) throw new InvalidOperationException($"Value is {Kind}, not S32.");
            return (int)value!;
        }

        /// <summary>Gets the unsigned 32-bit integer value.</summary>
        public uint GetU32()
        {
            if (Kind != ComponentValKind.U32) throw new InvalidOperationException($"Value is {Kind}, not U32.");
            return (uint)value!;
        }

        /// <summary>Gets the signed 64-bit integer value.</summary>
        public long GetS64()
        {
            if (Kind != ComponentValKind.S64) throw new InvalidOperationException($"Value is {Kind}, not S64.");
            return (long)value!;
        }

        /// <summary>Gets the unsigned 64-bit integer value.</summary>
        public ulong GetU64()
        {
            if (Kind != ComponentValKind.U64) throw new InvalidOperationException($"Value is {Kind}, not U64.");
            return (ulong)value!;
        }

        /// <summary>Gets the 32-bit floating-point value.</summary>
        public float GetF32()
        {
            if (Kind != ComponentValKind.F32) throw new InvalidOperationException($"Value is {Kind}, not F32.");
            return (float)value!;
        }

        /// <summary>Gets the 64-bit floating-point value.</summary>
        public double GetF64()
        {
            if (Kind != ComponentValKind.F64) throw new InvalidOperationException($"Value is {Kind}, not F64.");
            return (double)value!;
        }

        /// <summary>Gets the Unicode character value.</summary>
        public char GetChar()
        {
            if (Kind != ComponentValKind.Char) throw new InvalidOperationException($"Value is {Kind}, not Char.");
            return (char)(uint)value!;
        }

        /// <summary>Gets the string value.</summary>
        public string GetString()
        {
            if (Kind != ComponentValKind.String) throw new InvalidOperationException($"Value is {Kind}, not String.");
            return (string)value!;
        }

        /// <summary>Gets the list&lt;string&gt; value.</summary>
        public string[] GetStringList()
        {
            if (Kind != ComponentValKind.List) throw new InvalidOperationException($"Value is {Kind}, not List.");
            if (value is string[] strings) return strings;
            throw new InvalidOperationException("List does not contain strings. Use GetList() instead.");
        }

        /// <summary>Gets the generic list value.</summary>
        public ComponentVal[] GetList()
        {
            if (Kind != ComponentValKind.List) throw new InvalidOperationException($"Value is {Kind}, not List.");
            if (value is ComponentVal[] vals) return vals;
            // Convert string[] to ComponentVal[] for backwards compatibility
            if (value is string[] strings)
            {
                var result = new ComponentVal[strings.Length];
                for (int i = 0; i < strings.Length; i++)
                {
                    result[i] = FromString(strings[i]);
                }
                return result;
            }
            throw new InvalidOperationException("Unexpected list value type.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <summary>
        /// Converts this managed value to a native representation.
        /// The caller is responsible for cleaning up any allocated native memory.
        /// </summary>
        internal unsafe void ToNative(ref NativeComponentVal native)
        {
            native.kind = (byte)Kind;

            switch (Kind)
            {
                case ComponentValKind.Bool:
                    native.of.boolean = (bool)value! ? (byte)1 : (byte)0;
                    break;

                case ComponentValKind.S8:
                    native.of.s8 = (sbyte)value!;
                    break;

                case ComponentValKind.U8:
                    native.of.u8 = (byte)value!;
                    break;

                case ComponentValKind.S16:
                    native.of.s16 = (short)value!;
                    break;

                case ComponentValKind.U16:
                    native.of.u16 = (ushort)value!;
                    break;

                case ComponentValKind.S32:
                    native.of.s32 = (int)value!;
                    break;

                case ComponentValKind.U32:
                    native.of.u32 = (uint)value!;
                    break;

                case ComponentValKind.S64:
                    native.of.s64 = (long)value!;
                    break;

                case ComponentValKind.U64:
                    native.of.u64 = (ulong)value!;
                    break;

                case ComponentValKind.F32:
                    native.of.f32 = (float)value!;
                    break;

                case ComponentValKind.F64:
                    native.of.f64 = (double)value!;
                    break;

                case ComponentValKind.Char:
                    native.of.character = (uint)value!;
                    break;

                case ComponentValKind.String:
                {
                    var str = (string)value!;
                    var bytes = Encoding.UTF8.GetBytes(str);
                    var ptr = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, ptr, bytes.Length);
                    native.of.str.size = (nuint)bytes.Length;
                    native.of.str.data = (byte*)ptr;
                    break;
                }

                case ComponentValKind.List:
                {
                    ComponentVal[] elements;
                    if (value is string[] strings)
                    {
                        elements = new ComponentVal[strings.Length];
                        for (int i = 0; i < strings.Length; i++)
                        {
                            elements[i] = FromString(strings[i]);
                        }
                    }
                    else
                    {
                        elements = (ComponentVal[])value!;
                    }

                    var elementSize = Marshal.SizeOf<NativeComponentVal>();
                    var listPtr = Marshal.AllocHGlobal(elementSize * elements.Length);

                    for (int i = 0; i < elements.Length; i++)
                    {
                        var elementNative = new NativeComponentVal();
                        elements[i].ToNative(ref elementNative);

                        var dest = IntPtr.Add(listPtr, i * elementSize);
                        unsafe
                        {
                            *(NativeComponentVal*)dest = elementNative;
                        }
                    }

                    native.of.list.size = (nuint)elements.Length;
                    native.of.list.data = listPtr;
                    break;
                }

                default:
                    throw new NotSupportedException($"Converting {Kind} to native is not yet supported.");
            }
        }

        /// <summary>
        /// Creates a managed ComponentVal from a native representation.
        /// </summary>
        internal static unsafe ComponentVal FromNative(ref NativeComponentVal native)
        {
            var kind = (ComponentValKind)native.kind;

            switch (kind)
            {
                case ComponentValKind.Bool:
                    return FromBool(native.of.boolean != 0);

                case ComponentValKind.S8:
                    return FromS8(native.of.s8);

                case ComponentValKind.U8:
                    return FromU8(native.of.u8);

                case ComponentValKind.S16:
                    return FromS16(native.of.s16);

                case ComponentValKind.U16:
                    return FromU16(native.of.u16);

                case ComponentValKind.S32:
                    return FromS32(native.of.s32);

                case ComponentValKind.U32:
                    return FromU32(native.of.u32);

                case ComponentValKind.S64:
                    return FromS64(native.of.s64);

                case ComponentValKind.U64:
                    return FromU64(native.of.u64);

                case ComponentValKind.F32:
                    return FromF32(native.of.f32);

                case ComponentValKind.F64:
                    return FromF64(native.of.f64);

                case ComponentValKind.Char:
                    return FromChar((char)native.of.character);

                case ComponentValKind.String:
                {
                    var size = (int)native.of.str.size;
                    var str = size > 0
                        ? Encoding.UTF8.GetString(native.of.str.data, size)
                        : string.Empty;
                    return FromString(str);
                }

                case ComponentValKind.List:
                {
                    var count = (int)native.of.list.size;
                    var elementSize = Marshal.SizeOf<NativeComponentVal>();

                    // Check if all elements are strings for backward compatibility
                    bool allStrings = true;
                    for (int i = 0; i < count && allStrings; i++)
                    {
                        var elementPtr = IntPtr.Add(native.of.list.data, i * elementSize);
                        ref var element = ref *(NativeComponentVal*)elementPtr;
                        if ((ComponentValKind)element.kind != ComponentValKind.String)
                        {
                            allStrings = false;
                        }
                    }

                    if (allStrings)
                    {
                        var strings = new string[count];
                        for (int i = 0; i < count; i++)
                        {
                            var elementPtr = IntPtr.Add(native.of.list.data, i * elementSize);
                            ref var element = ref *(NativeComponentVal*)elementPtr;
                            var size = (int)element.of.str.size;
                            strings[i] = size > 0
                                ? Encoding.UTF8.GetString(element.of.str.data, size)
                                : string.Empty;
                        }
                        return FromStringList(strings);
                    }
                    else
                    {
                        var elements = new ComponentVal[count];
                        for (int i = 0; i < count; i++)
                        {
                            var elementPtr = IntPtr.Add(native.of.list.data, i * elementSize);
                            ref var element = ref *(NativeComponentVal*)elementPtr;
                            elements[i] = FromNative(ref element);
                        }
                        return FromList(elements);
                    }
                }

                default:
                    throw new NotSupportedException($"Converting native kind {kind} is not yet supported.");
            }
        }

        /// <summary>
        /// Cleans up native memory allocated by ToNative.
        /// </summary>
        internal static unsafe void DeleteNative(ref NativeComponentVal native)
        {
            NativeHelpers.wasmtime_component_val_delete(ref native);
        }

        /// <summary>
        /// Native representation of the wasmtime_component_valunion_t union.
        /// On 64-bit platforms: max member is wasm_name_t = { size_t size; byte* data; } = 16 bytes.
        /// The variant type contains { wasm_name_t discriminant; wasmtime_component_val_t* val; } = 24 bytes.
        /// The result type contains { bool is_ok; wasmtime_component_val_t* val; } = 16 bytes (with alignment).
        /// We use Explicit layout with size 24 to cover the largest member (variant).
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        internal unsafe struct NativeComponentValUnion
        {
            [FieldOffset(0)] public byte boolean;
            [FieldOffset(0)] public sbyte s8;
            [FieldOffset(0)] public byte u8;
            [FieldOffset(0)] public short s16;
            [FieldOffset(0)] public ushort u16;
            [FieldOffset(0)] public int s32;
            [FieldOffset(0)] public uint u32;
            [FieldOffset(0)] public long s64;
            [FieldOffset(0)] public ulong u64;
            [FieldOffset(0)] public float f32;
            [FieldOffset(0)] public double f64;
            [FieldOffset(0)] public uint character;

            // string: wasm_name_t = { size_t size; byte* data; }
            [FieldOffset(0)] public NativeWasmName str;

            // list: wasmtime_component_vallist_t = { size_t size; wasmtime_component_val_t* data; }
            [FieldOffset(0)] public NativeVec list;

            // record: wasmtime_component_valrecord_t = { size_t size; wasmtime_component_valrecord_entry_t* data; }
            [FieldOffset(0)] public NativeVec record;

            // tuple: wasmtime_component_valtuple_t = { size_t size; wasmtime_component_val_t* data; }
            [FieldOffset(0)] public NativeVec tuple;

            // flags: wasmtime_component_valflags_t = { size_t size; wasm_name_t* data; }
            [FieldOffset(0)] public NativeVec flags;

            // variant: { wasm_name_t discriminant; wasmtime_component_val_t* val; }
            [FieldOffset(0)] public NativeWasmName variant_discriminant;
            [FieldOffset(16)] public IntPtr variant_val;

            // enum: wasm_name_t
            [FieldOffset(0)] public NativeWasmName enumeration;

            // option: wasmtime_component_val_t* (nullable)
            [FieldOffset(0)] public IntPtr option;

            // result: { bool is_ok; wasmtime_component_val_t* val; }
            [FieldOffset(0)] public byte result_is_ok;
            // padding to 8 bytes for pointer alignment
            [FieldOffset(8)] public IntPtr result_val;

            // resource: wasmtime_component_resource_any_t*
            [FieldOffset(0)] public IntPtr resource;
        }

        /// <summary>
        /// Native wasm_name_t / wasm_byte_vec_t equivalent: { size_t size; byte* data; }
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct NativeWasmName
        {
            public nuint size;
            public byte* data;
        }

        /// <summary>
        /// Generic native vec: { size_t size; void* data; }
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeVec
        {
            public nuint size;
            public IntPtr data;
        }

        /// <summary>
        /// Native representation of wasmtime_component_val_t.
        /// Layout: { uint8_t kind; [padding]; wasmtime_component_valunion_t of; }
        /// The union starts at offset 8 due to alignment requirements (pointer alignment).
        /// Total size = 8 (kind + padding) + 24 (union) = 32 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        internal struct NativeComponentVal
        {
            [FieldOffset(0)] public byte kind;
            [FieldOffset(8)] public NativeComponentValUnion of;
        }

        private static class NativeHelpers
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_component_val_delete(ref NativeComponentVal value);
        }
    }
}

#endif
