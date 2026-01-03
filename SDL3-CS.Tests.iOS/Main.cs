using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;

public class Program
{
    [DllImport("__Internal")]
    private static extern IntPtr dlopen(string path, int mode);

    private const int RTLD_NOW = 0x2;
    private const int RTLD_GLOBAL = 0x8;

    public static unsafe void Main(string[] args)
    {
        // Pre-load libraries with RTLD_GLOBAL to ensure symbols are visible and dependencies are resolved correctly.
        // This fixes issues where SDL3_image fails to load or find SDL3 symbols on iOS.
        LoadLibrary("SDL3");
        LoadLibrary("SDL3_image");
        LoadLibrary("SDL3_ttf");
        LoadLibrary("SDL3_mixer");

        DllImportResolver resolver = (name, assembly, path) => {
            Console.WriteLine($"Resolving: {name} for assembly: {assembly.FullName}");
            try {
                string frameworkName = name;
                if (name.StartsWith("SDL3") && !name.Contains(".framework"))
                {
                    frameworkName = $"{name}.framework";
                }
                
                string p = Path.Combine(NSBundle.MainBundle.BundlePath, "Frameworks", frameworkName, name);
                if (File.Exists(p))
                {
                    return NativeLibrary.Load(p, assembly, path);
                }
            } catch (Exception ex) {
                Console.WriteLine($"Resolver failed for {name}: {ex}");
            }
            return IntPtr.Zero;
        };

        NativeLibrary.SetDllImportResolver(typeof(SDL3).Assembly, resolver);
        NativeLibrary.SetDllImportResolver(typeof(SDL3_image).Assembly, resolver);
        NativeLibrary.SetDllImportResolver(typeof(SDL3_ttf).Assembly, resolver);
        NativeLibrary.SetDllImportResolver(typeof(SDL3_mixer).Assembly, resolver);

        SDL3.SDL_RunApp(0, null, &main, IntPtr.Zero);
    }

    private static void LoadLibrary(string name)
    {
        try {
            string path = Path.Combine(NSBundle.MainBundle.BundlePath, "Frameworks", $"{name}.framework", name);
            if (File.Exists(path))
            {
                IntPtr handle = dlopen(path, RTLD_NOW | RTLD_GLOBAL);
                if (handle == IntPtr.Zero)
                {
                    Console.WriteLine($"Warning: Failed to dlopen {name} at {path}");
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error loading {name}: {ex}");
        }
    }

    private static void LoadSystemLibrary(string name)
    {
        try {
            string path = $"/System/Library/Frameworks/{name}.framework/{name}";
            if (File.Exists(path))
            {
                IntPtr handle = dlopen(path, RTLD_NOW | RTLD_GLOBAL);
                if (handle == IntPtr.Zero)
                {
                    Console.WriteLine($"Warning: Failed to dlopen system library {name} at {path}");
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error loading system library {name}: {ex}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe int main(int argc, byte** argv)
    {
        SDL.Tests.Program.Main();
        return 0;
    }
}
