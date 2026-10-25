using System;
using System.Runtime.InteropServices;

namespace VoiceFlow.Services;

public static class ComputeDeviceService
{
    private static readonly Lazy<bool> Vulkan = new(ProbeVulkan);
    private static readonly Lazy<bool> Cuda = new(ProbeCuda);
    public static bool HasWhisperGpu => Vulkan.Value;
    public static bool HasBarkGpu => Cuda.Value;

    public static string Resolve(string preference, bool bark, bool hasGpu)
    {
        if (preference == "CPU") return "CPU";
        if (preference == "GPU" && !hasGpu)
            throw new InvalidOperationException(bark ? "Bark GPU needs a compatible NVIDIA GPU. Choose Default or CPU." : "No compatible GPU found. Choose Default or CPU.");
        return hasGpu ? "GPU" : "CPU";
    }

    public static string Resolve(string preference, bool bark) => Resolve(preference, bark, bark ? HasBarkGpu : HasWhisperGpu);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CuInit(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CuGetInt(out int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CuDeviceAttribute(out int value, int attribute, int device);
    private static bool ProbeCuda()
    {
        if (!NativeLibrary.TryLoad("nvcuda.dll", out var lib)) return false;
        try
        {
            var init = Marshal.GetDelegateForFunctionPointer<CuInit>(NativeLibrary.GetExport(lib, "cuInit"));
            var count = Marshal.GetDelegateForFunctionPointer<CuGetInt>(NativeLibrary.GetExport(lib, "cuDeviceGetCount"));
            var version = Marshal.GetDelegateForFunctionPointer<CuGetInt>(NativeLibrary.GetExport(lib, "cuDriverGetVersion"));
            var attribute = Marshal.GetDelegateForFunctionPointer<CuDeviceAttribute>(NativeLibrary.GetExport(lib, "cuDeviceGetAttribute"));
            return init(0) == 0 && count(out int devices) == 0 && devices > 0
                && version(out int driver) == 0 && driver >= 12060
                && attribute(out int major, 75, 0) == 0 && major >= 7;
        }
        catch { return false; }
        finally { NativeLibrary.Free(lib); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkInstanceInfo
    {
        public uint Type;
        public IntPtr Next;
        public uint Flags;
        public IntPtr Application;
        public uint LayerCount;
        public IntPtr Layers;
        public uint ExtensionCount;
        public IntPtr Extensions;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int VkCreate(ref VkInstanceInfo info, IntPtr allocator, out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int VkEnumerate(IntPtr instance, ref uint count, IntPtr devices);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void VkProperties(IntPtr device, IntPtr properties);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void VkDestroy(IntPtr instance, IntPtr allocator);
    private static bool ProbeVulkan()
    {
        if (!NativeLibrary.TryLoad("vulkan-1.dll", out var lib)) return false;
        IntPtr instance = IntPtr.Zero, devices = IntPtr.Zero, properties = IntPtr.Zero;
        try
        {
            var create = Marshal.GetDelegateForFunctionPointer<VkCreate>(NativeLibrary.GetExport(lib, "vkCreateInstance"));
            var enumerate = Marshal.GetDelegateForFunctionPointer<VkEnumerate>(NativeLibrary.GetExport(lib, "vkEnumeratePhysicalDevices"));
            var getProperties = Marshal.GetDelegateForFunctionPointer<VkProperties>(NativeLibrary.GetExport(lib, "vkGetPhysicalDeviceProperties"));
            var info = new VkInstanceInfo { Type = 1 };
            if (create(ref info, IntPtr.Zero, out instance) != 0) return false;
            uint count = 0;
            if (enumerate(instance, ref count, IntPtr.Zero) != 0 || count == 0 || count > 64) return false;
            devices = Marshal.AllocHGlobal((int)count * IntPtr.Size);
            properties = Marshal.AllocHGlobal(4096);
            if (enumerate(instance, ref count, devices) != 0) return false;
            for (int i = 0; i < count; i++)
            {
                getProperties(Marshal.ReadIntPtr(devices, i * IntPtr.Size), properties);
                int type = Marshal.ReadInt32(properties, 16);
                if (type is 1 or 2) return true;
            }
            return false;
        }
        catch { return false; }
        finally
        {
            if (instance != IntPtr.Zero)
                Marshal.GetDelegateForFunctionPointer<VkDestroy>(NativeLibrary.GetExport(lib, "vkDestroyInstance"))(instance, IntPtr.Zero);
            if (devices != IntPtr.Zero) Marshal.FreeHGlobal(devices);
            if (properties != IntPtr.Zero) Marshal.FreeHGlobal(properties);
            NativeLibrary.Free(lib);
        }
    }
}
