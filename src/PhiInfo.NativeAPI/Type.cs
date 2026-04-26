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
public unsafe struct FfiArray<T> where T : unmanaged
{
    public T* Data;
    public nuint Length;

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
            Length = (nuint)array.Length
        };
    }

    public static implicit operator FfiArray<T>(ReadOnlySpan<T> span)
    {
        if (span.Length == 0)
            return default;

        var size = sizeof(T) * span.Length;
        var ptr = (T*)Marshal.AllocHGlobal(size);

        span.CopyTo(new Span<T>(ptr, span.Length));

        return new FfiArray<T>
        {
            Data = ptr,
            Length = (nuint)span.Length
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
    public FfiArray<byte> Mime;
    public FfiArray<byte> Data;

    public static void Free(FfiResponse* ptr)
    {
        FfiArray<byte>.Free(&ptr->Mime);
        FfiArray<byte>.Free(&ptr->Data);
    }

    public static implicit operator FfiResponse(Response resp)
    {
        var code = resp.code;
        FfiArray<byte> mime;
        if (resp.mime == null)
            mime = ""u8;
        else
            mime = Encoding.UTF8.GetBytes(resp.mime);

        FfiArray<byte> bytes = resp.data ?? [];

        return new FfiResponse
        {
            Code = code,
            Mime = mime,
            Data = bytes
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct FfiResult : IFfiFree<FfiResult>
{
    // 等于0表示成功，非0表示失败
    public byte code;
    public FfiArray<byte> messageAndStackTrace;

    public static void Free(FfiResult* ptr)
    {
        FfiArray<byte>.Free(&ptr->messageAndStackTrace);
    }

    public static implicit operator FfiResult(Exception ex)
    {
        var messageAndStackTrace = Encoding.UTF8.GetBytes(ex.Message + "\n" + (ex.StackTrace ?? ""));

        return new FfiResult
        {
            code = 1,
            messageAndStackTrace = messageAndStackTrace
        };
    }

    public static FfiResult OK => default;
}