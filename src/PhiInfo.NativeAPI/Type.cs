using System;
using System.Runtime.InteropServices;
using System.Text;
using PhiInfo.Processing.Type;

namespace PhiInfo.NativeAPI;

public interface IFfiFree<T> where T : unmanaged
{
    static abstract unsafe void Free(T* ptr);
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct FfiString : IFfiFree<FfiString>
{
    public byte* Data;
    public nint Length;

    public static implicit operator FfiString(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);

        var ptr = (byte*)Marshal.AllocHGlobal(bytes.Length);

        for (var i = 0; i < bytes.Length; i++) ptr[i] = bytes[i];

        return new FfiString
        {
            Data = ptr,
            Length = bytes.Length
        };
    }

    public override string ToString()
    {
        if (Data == null || Length == 0)
            return string.Empty;

        return Encoding.UTF8.GetString(Data, (int)Length);
    }

    public static void Free(FfiString* ptr)
    {
        if (ptr->Data is not null)
        {
            Marshal.FreeHGlobal((IntPtr)ptr->Data);
            ptr->Data = null;
            ptr->Length = 0;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct FfiArray<T> where T : unmanaged
{
    public T* Data;
    public nint Length;

    public static implicit operator FfiArray<T>(T[] array)
    {
        if (array.Length == 0)
            return default;

        var size = sizeof(T) * array.Length;

        var ptr = (T*)Marshal.AllocHGlobal(size);

        for (var i = 0; i < array.Length; i++)
            ptr[i] = array[i];

        return new FfiArray<T>
        {
            Data = ptr,
            Length = array.Length
        };
    }

    public Span<T> AsSpan()
    {
        return new Span<T>(Data, (int)Length);
    }

    public static void Free(FfiArray<T>* ptr)
    {
        if (ptr->Data != null)
        {
            Marshal.FreeHGlobal((IntPtr)ptr->Data);
            ptr->Data = null;
            ptr->Length = 0;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct FfiResponse : IFfiFree<FfiResponse>
{
    public ushort Code;
    public FfiString Mime;
    public FfiArray<byte> Data;

    public static void Free(FfiResponse* ptr)
    {
        FfiString.Free(&ptr->Mime);
        FfiArray<byte>.Free(&ptr->Data);
    }

    public static implicit operator FfiResponse(Response resp)
    {
        var code = resp.code;
        FfiString mime = resp.mime ?? string.Empty;
        FfiArray<byte> bytes = resp.data ?? [];

        return new FfiResponse
        {
            Code = code,
            Mime = mime,
            Data = bytes
        };
    }
}