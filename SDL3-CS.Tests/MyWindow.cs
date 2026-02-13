// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SDL.SDL3;

namespace SDL.Tests
{
    public sealed unsafe class MyWindow : IDisposable
    {
        private bool flash;
        private ObjectHandle<MyWindow> objectHandle { get; }
        private SDL_Window* sdlWindowHandle;
        private SDL_Renderer* renderer;
        private readonly bool initSuccess;

        private SDL_Camera* camera;
        private SDL_Texture* texture;
        private SDL_Surface* frame_current;
        private bool texture_updated;
        private SDL_CameraID front_camera;
        private SDL_CameraID back_camera;
        private ulong last_flip;

        private const SDL_InitFlags init_flags = SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_GAMEPAD | SDL_InitFlags.SDL_INIT_CAMERA;


        static unsafe void PrintCameraSpecs(SDL_CameraID camera_id)
        {
            SDL_CameraSpec **specs = SDL_GetCameraSupportedFormats(camera_id, null);
            if (specs != null) {
                int i;

                Console.WriteLine("Available formats:");
                for (i = 0; specs[i] != null; ++i) {
                    SDL_CameraSpec *s = specs[i];
                    Console.WriteLine($"{s->width}:{s->height}:{SDL_GetPixelFormatName(s->format)} FPS:{(float)s->framerate_numerator / s->framerate_denominator}");
                }
                SDL_free(specs);
            }
        }

        void PickCameraSpec(SDL_CameraID camera_id, SDL_CameraSpec* spec)
        {
            SDL_CameraSpec** specs = SDL_GetCameraSupportedFormats(camera_id, null);

            *spec = default;

            if (specs != null)
            {
                int max_size = (int)SDL_GetNumberProperty(SDL_GetRendererProperties(renderer), SDL_PROP_RENDERER_MAX_TEXTURE_SIZE_NUMBER, 0);

                for (int i = 0; specs[i] != null; ++i)
                {
                    SDL_CameraSpec* s = specs[i];

                    if (s->width <= max_size && s->height <= max_size)
                    {
                        *spec = *s;
                        break;
                    }
                }
                SDL_free(specs);
            }
        }

        private bool OpenCamera()
        {
            int devcount = 0;
            SDL_CameraID* devices = SDL_GetCameras(&devcount);
            
            if (devices == null)
            {
                Console.WriteLine($"ERROR: SDL_GetCameras failed: {SDL_GetError()}");
                return false;
            }

            Console.WriteLine($"Saw {devcount} camera devices.");

            if (devcount == 0)
            {
                Console.WriteLine("WARNING: No cameras found on this device!");
                SDL_free(devices);
                return false;
            }

            SDL_CameraID camera_id = 0;

            for (int i = 0; i < devcount; i++)
            {
                SDL_CameraID device = devices[i];
                string? name = SDL_GetCameraName(device);
                SDL_CameraPosition position = SDL_GetCameraPosition(device);
                string posstr = "";

                if (position == SDL_CameraPosition.SDL_CAMERA_POSITION_FRONT_FACING)
                {
                    if (front_camera == 0)
                        front_camera = device;
                    posstr = "[front-facing] ";
                }
                else if (position == SDL_CameraPosition.SDL_CAMERA_POSITION_BACK_FACING)
                {
                    if (back_camera == 0)
                        back_camera = device;
                    posstr = "[back-facing] ";
                }

                Console.WriteLine($"  - Camera #{i}: {posstr}{name}");
                PrintCameraSpecs(device);
            }

            if (front_camera != 0)
                camera_id = front_camera;
            else if (devcount > 0)
                camera_id = devices[0];

            SDL_free(devices);

            if (camera_id == 0)
            {
                Console.WriteLine("ERROR: No cameras available?");
                return false;
            }

            Console.WriteLine($"Selected camera_id: {camera_id}");

            SDL_CameraSpec spec;
            PickCameraSpec(camera_id, &spec);

            Console.WriteLine($"Opening camera with spec: {spec.width}x{spec.height} {(spec.framerate_denominator != 0 ? (float)spec.framerate_numerator / spec.framerate_denominator : 0.0f)} FPS {SDL_GetPixelFormatName(spec.format)}");

            if (spec.width == 0 || spec.height == 0)
            {
                Console.WriteLine("No suitable spec found, letting SDL choose defaults");
                camera = SDL_OpenCamera(camera_id, null);
            }
            else
            {
                camera = SDL_OpenCamera(camera_id, &spec);
            }

            if (camera == null)
            {
                Console.WriteLine($"ERROR: Failed to open camera device: {SDL_GetError()}");
                return false;
            }

            Console.WriteLine("Camera opened successfully!");
            return true;
        }

        private void FlipCamera()
        {
            if ((SDL_GetTicks() - last_flip) < 3000)
                return;

            if (camera != null)
            {
                SDL_CameraID current = SDL_GetCameraID(camera);
                SDL_CameraID nextcam = 0;

                if (current == front_camera)
                    nextcam = back_camera;
                else if (current == back_camera)
                    nextcam = front_camera;

                if (nextcam != 0)
                {
                    Console.WriteLine("Flip camera!");

                    if (frame_current != null)
                    {
                        SDL_ReleaseCameraFrame(camera, frame_current);
                        frame_current = null;
                    }

                    SDL_CloseCamera(camera);

                    if (texture != null)
                    {
                        SDL_DestroyTexture(texture);
                        texture = null;
                    }

                    SDL_CameraSpec spec;
                    PickCameraSpec(nextcam, &spec);
                    camera = SDL_OpenCamera(nextcam, &spec);

                    if (camera == null)
                    {
                        Console.WriteLine($"Failed to open camera device: {SDL_GetError()}");
                        run = false;
                    }

                    last_flip = SDL_GetTicks();
                }
            }
        }

        public MyWindow()
        {
            if (!SDL_InitSubSystem(init_flags))
                throw new InvalidOperationException($"failed to initialise SDL. Error: {SDL_GetError()}");

            initSuccess = true;

            objectHandle = new ObjectHandle<MyWindow>(this, GCHandleType.Normal);
        }

        public void Setup()
        {
            SDL_SetGamepadEventsEnabled(true);
            SDL_SetEventFilter(&nativeFilter, objectHandle.Handle);

            if (OperatingSystem.IsWindows())
                SDL_SetWindowsMessageHook(&wndProc, objectHandle.Handle);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static SDLBool wndProc(IntPtr userdata, MSG* message)
        {
            var handle = new ObjectHandle<MyWindow>(userdata);

            if (handle.GetTarget(out var window))
            {
                Console.WriteLine($"from {window}, message: {message->message}");
            }

            return true;
        }

        // ReSharper disable once UseCollectionExpression
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static SDLBool nativeFilter(IntPtr userdata, SDL_Event* e)
        {
            var handle = new ObjectHandle<MyWindow>(userdata);
            if (handle.GetTarget(out var window))
                return window.handleEventFromFilter(e);

            return true;
        }

        public Action<SDL_Event>? EventFilter;

        private bool handleEventFromFilter(SDL_Event* e)
        {
            switch (e->Type)
            {
                case SDL_EventType.SDL_EVENT_KEY_UP:
                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    handleKeyFromFilter(e->key);
                    break;

                default:
                    EventFilter?.Invoke(*e);
                    break;
            }

            return true;
        }

        private void handleKeyFromFilter(SDL_KeyboardEvent e)
        {
            if (e.key == SDL_Keycode.SDLK_F)
            {
                flash = true;
            }
        }

        public void Create()
        {
            sdlWindowHandle = SDL_CreateWindow("SDL3 Camera Test"u8, 800, 600, SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY);
            renderer = SDL_CreateRenderer(sdlWindowHandle, (Utf8String)null);
            
            if (!OpenCamera())
            {
                Console.WriteLine("Failed to open camera, but continuing...");
            }
        }

        private void handleEvent(SDL_Event e)
        {
            switch (e.Type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                    run = false;
                    break;

                case SDL_EventType.SDL_EVENT_CAMERA_DEVICE_APPROVED:
                {
                    Console.WriteLine("Camera approved!");
                    SDL_CameraSpec camera_spec;
                    if (SDL_GetCameraFormat(camera, &camera_spec))
                    {
                        float fps = camera_spec.framerate_denominator != 0 
                            ? (float)camera_spec.framerate_numerator / camera_spec.framerate_denominator 
                            : 0;
                        Console.WriteLine($"Camera Spec: {camera_spec.width}x{camera_spec.height} {fps} FPS {SDL_GetPixelFormatName(camera_spec.format)}");
                    }
                    break;
                }

                case SDL_EventType.SDL_EVENT_CAMERA_DEVICE_DENIED:
                    Console.WriteLine("Camera denied!");
                    run = false;
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    FlipCamera();
                    break;

                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    switch (e.key.key)
                    {
                        case SDL_Keycode.SDLK_ESCAPE:
                            run = false;
                            break;

                        case SDL_Keycode.SDLK_SPACE:
                            FlipCamera();
                            break;

                        case SDL_Keycode.SDLK_R:
                            bool old = SDL_GetWindowRelativeMouseMode(sdlWindowHandle);
                            SDL_SetWindowRelativeMouseMode(sdlWindowHandle, !old);
                            break;

                        case SDL_Keycode.SDLK_V:
                            string? text = SDL_GetClipboardText();
                            Console.WriteLine($"clipboard: {text}");
                            break;

                        case SDL_Keycode.SDLK_F10:
                            SDL_SetWindowFullscreen(sdlWindowHandle, false);
                            break;

                        case SDL_Keycode.SDLK_F11:
                            SDL_SetWindowFullscreen(sdlWindowHandle, true);
                            break;

                        case SDL_Keycode.SDLK_J:
                        {
                            using var gamepads = SDL_GetGamepads();

                            if (gamepads == null || gamepads.Count == 0)
                                break;

                            var gamepad = SDL_OpenGamepad(gamepads[0]);

                            int count;
                            var bindings = SDL_GetGamepadBindings(gamepad, &count);

                            for (int i = 0; i < count; i++)
                            {
                                var binding = *bindings[i];
                                Console.WriteLine(binding.input_type);
                                Console.WriteLine(binding.output_type);
                                Console.WriteLine();
                            }

                            SDL_CloseGamepad(gamepad);
                            break;
                        }

                        case SDL_Keycode.SDLK_F1:
                            SDL_StartTextInput(sdlWindowHandle);
                            break;

                        case SDL_Keycode.SDLK_F2:
                            SDL_StopTextInput(sdlWindowHandle);
                            break;

                        case SDL_Keycode.SDLK_M:
                            SDL_Keymod mod = e.key.mod;
                            Console.WriteLine(mod);
                            break;

                        case SDL_Keycode.SDLK_E:
                            Console.WriteLine(SDL_GetEventDescription(e));
                            break;
                    }

                    break;

                case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                    Console.WriteLine(e.text.GetText());
                    break;

                case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                    Console.WriteLine($"gamepad added: {e.gdevice.which}");
                    break;

                case SDL_EventType.SDL_EVENT_PEN_PROXIMITY_IN:
                    Console.WriteLine($"pen proximity in: {e.pproximity.which}");
                    break;
            }
        }

        private bool run = true;

        private const int events_per_peep = 64;
        private readonly SDL_Event[] events = new SDL_Event[events_per_peep];

        private void pollEvents()
        {
            SDL_PumpEvents();

            int eventsRead;

            do
            {
                eventsRead = SDL_PeepEvents(events, SDL_EventAction.SDL_GETEVENT, SDL_EventType.SDL_EVENT_FIRST, SDL_EventType.SDL_EVENT_LAST);
                for (int i = 0; i < eventsRead; i++)
                    handleEvent(events[i]);
            } while (eventsRead == events_per_peep);
        }

        public void Run()
        {
            while (run)
            {
                if (flash)
                {
                    flash = false;
                    Console.WriteLine("flash!");
                }

                pollEvents();

                // Clear with gray background
                SDL_SetRenderDrawColor(renderer, 0x99, 0x99, 0x99, 255);
                SDL_RenderClear(renderer);

                // Acquire and render camera frame
                if (camera != null)
                {
                    ulong timestampNS = 0;
                    SDL_Surface* frame_next = SDL_AcquireCameraFrame(camera, &timestampNS);

                    if (frame_next != null)
                    {
                        if (frame_current != null)
                            SDL_ReleaseCameraFrame(camera, frame_current);

                        frame_current = frame_next;
                        texture_updated = false;
                    }

                    if (frame_current != null)
                    {
                        // Create or recreate texture if needed
                        if (texture == null || texture->w != frame_current->w || texture->h != frame_current->h)
                        {
                            SDL_SetWindowSize(sdlWindowHandle, frame_current->w, frame_current->h);

                            if (texture != null)
                                SDL_DestroyTexture(texture);

                            SDL_Colorspace colorspace = SDL_GetSurfaceColorspace(frame_current);

                            SDL_PropertiesID props = SDL_CreateProperties();
                            
                            fixed (byte* formatProp = SDL_PROP_TEXTURE_CREATE_FORMAT_NUMBER)
                            fixed (byte* colorspaceProp = SDL_PROP_TEXTURE_CREATE_COLORSPACE_NUMBER)
                            fixed (byte* accessProp = SDL_PROP_TEXTURE_CREATE_ACCESS_NUMBER)
                            fixed (byte* widthProp = SDL_PROP_TEXTURE_CREATE_WIDTH_NUMBER)
                            fixed (byte* heightProp = SDL_PROP_TEXTURE_CREATE_HEIGHT_NUMBER)
                            {
                                SDL_SetNumberProperty(props, formatProp, (long)frame_current->format);
                                SDL_SetNumberProperty(props, colorspaceProp, (long)colorspace);
                                SDL_SetNumberProperty(props, accessProp, (long)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING);
                                SDL_SetNumberProperty(props, widthProp, (long)frame_current->w);
                                SDL_SetNumberProperty(props, heightProp, (long)frame_current->h);
                            }
                            
                            texture = SDL_CreateTextureWithProperties(renderer, props);
                            SDL_DestroyProperties(props);

                            if (texture == null)
                            {
                                Console.WriteLine($"Couldn't create texture: {SDL_GetError()}");
                                run = false;
                            }
                        }

                        // Update texture with frame data
                        if (texture != null && !texture_updated)
                        {
                            SDL_UpdateTexture(texture, null, frame_current->pixels, frame_current->pitch);
                            texture_updated = true;
                        }

                        // Render the texture with rotation
                        if (texture != null)
                        {
                            float rotation;
                            fixed (byte* rotationProp = "SDL.surface.rotation"u8)
                            {
                                rotation = SDL_GetFloatProperty(SDL_GetSurfaceProperties(frame_current), rotationProp, 0.0f);
                            }
                            
                            int win_w, win_h;
                            SDL_GetRenderOutputSize(renderer, &win_w, &win_h);
                            
                            SDL_FRect d;
                            d.x = (win_w - texture->w) / 2.0f;
                            d.y = (win_h - texture->h) / 2.0f;
                            d.w = texture->w;
                            d.h = texture->h;
                            
                            SDL_RenderTextureRotated(renderer, texture, null, &d, rotation, null, SDL_FlipMode.SDL_FLIP_NONE);
                        }
                    }
                }

                SDL_RenderPresent(renderer);

                Thread.Sleep(10);
            }
        }

        public void Dispose()
        {
            if (frame_current != null && camera != null)
                SDL_ReleaseCameraFrame(camera, frame_current);
            
            if (camera != null)
                SDL_CloseCamera(camera);
            
            if (texture != null)
                SDL_DestroyTexture(texture);
            
            if (renderer != null)
                SDL_DestroyRenderer(renderer);
            
            if (sdlWindowHandle != null)
                SDL_DestroyWindow(sdlWindowHandle);

            if (initSuccess)
                SDL_QuitSubSystem(init_flags);

            objectHandle.Dispose();
        }
    }
}
