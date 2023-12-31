﻿// From: https://raw.githubusercontent.com/veldrid/veldrid/master/src/Veldrid.ImGui/ImGuiRenderer.cs

using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.IO;
using Veldrid;

namespace CobaltCoreEditor;

/// <summary>
/// Can render draw lists produced by ImGui.
/// Also provides functions for updating ImGui input.
/// </summary>
public class ImGuiRenderer : IDisposable
{
    private GraphicsDevice _gd;
    private readonly Assembly _assembly;

    // Device objects
    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;
    private DeviceBuffer _projMatrixBuffer;
    private Texture _fontTexture;
    private Shader _vertexShader;
    private Shader _fragmentShader;
    private ResourceLayout _layout;
    private ResourceLayout _textureLayout;
    private Pipeline _pipeline;
    private ResourceSet _mainResourceSet;
    private ResourceSet _fontTextureResourceSet;
    private readonly IntPtr _fontAtlasId = (IntPtr)1;

    public int WindowWidth { get; private set; }
    public int WindowHeight { get; private set; }
    private readonly Vector2 _scaleFactor = Vector2.One;

    // Image trackers
    private readonly Dictionary<TextureView, ResourceSetInfo> _setsByView = new();

    private readonly Dictionary<Texture, TextureView> _autoViewsByTexture = new();

    private readonly Dictionary<IntPtr, ResourceSetInfo> _viewsById = new();
    private readonly List<IDisposable> _ownedResources = new();
    private int _lastAssignedId = 100;
    private bool _frameBegun;

    /// <summary>
    /// Constructs a new ImGuiRenderer.
    /// </summary>
    /// <param name="gd">The GraphicsDevice used to create and update resources.</param>
    /// <param name="outputDescription">The output format.</param>
    /// <param name="width">The initial width of the rendering target. Can be resized.</param>
    /// <param name="height">The initial height of the rendering target. Can be resized.</param>
    public ImGuiRenderer(
        GraphicsDevice gd,
        OutputDescription outputDescription,
        int width,
        int height
    )
    {
        _gd = gd;
        _assembly = typeof(ImGuiRenderer).GetTypeInfo().Assembly;
        WindowWidth = width;
        WindowHeight = height;

        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        ImGui.GetIO().Fonts.AddFontDefault();
        ImGui.GetIO().Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        CreateDeviceResources(gd, outputDescription);

        SetPerFrameImGuiData(1f / 15f);

        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void WindowResized(int width, int height)
    {
        WindowWidth = width;
        WindowHeight = height;
    }

    public void DestroyDeviceObjects()
    {
        Dispose();
    }

    public void CreateDeviceResources(
        GraphicsDevice gd,
        OutputDescription outputDescription
    )
    {
        _gd = gd;
        var factory = gd.ResourceFactory;
        _vertexBuffer =
            factory.CreateBuffer(new BufferDescription(10000, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _vertexBuffer.Name = "ImGui.NET Vertex Buffer";
        _indexBuffer = factory.CreateBuffer(new BufferDescription(2000, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        _indexBuffer.Name = "ImGui.NET Index Buffer";

        _projMatrixBuffer =
            factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _projMatrixBuffer.Name = "ImGui.NET Projection Buffer";

        var vertexShaderBytes = LoadEmbeddedShaderCode(
            gd.ResourceFactory,
            "imgui-vertex",
            ShaderStages.Vertex
        );
        var fragmentShaderBytes = LoadEmbeddedShaderCode(
            gd.ResourceFactory,
            "imgui-frag",
            ShaderStages.Fragment
        );
        _vertexShader = factory.CreateShader(
            new ShaderDescription(
                ShaderStages.Vertex,
                vertexShaderBytes,
                _gd.BackendType == GraphicsBackend.Vulkan ? "main" : "VS"
            )
        );
        _vertexShader.Name = "ImGui.NET Vertex Shader";
        _fragmentShader = factory.CreateShader(
            new ShaderDescription(
                ShaderStages.Fragment,
                fragmentShaderBytes,
                _gd.BackendType == GraphicsBackend.Vulkan ? "main" : "FS"
            )
        );
        _fragmentShader.Name = "ImGui.NET Fragment Shader";

        var vertexLayouts = new VertexLayoutDescription[]
        {
            new(
                new VertexElementDescription("in_position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                new VertexElementDescription(
                    "in_texCoord",
                    VertexElementSemantic.TextureCoordinate,
                    VertexElementFormat.Float2
                ),
                new VertexElementDescription("in_color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm)
            )
        };

        _layout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(
                    "ProjectionMatrixBuffer",
                    ResourceKind.UniformBuffer,
                    ShaderStages.Vertex
                ),
                new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)
            )
        );
        _layout.Name = "ImGui.NET Resource Layout";
        _textureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
            )
        );
        _textureLayout.Name = "ImGui.NET Texture Layout";

        var pd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(false, false, ComparisonKind.Always),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, true),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                vertexLayouts,
                new[] { _vertexShader, _fragmentShader },
                new[]
                {
                    new SpecializationConstant(0, gd.IsClipSpaceYInverted),
                    new SpecializationConstant(1, false),
                }
            ),
            new ResourceLayout[] { _layout, _textureLayout },
            outputDescription,
            ResourceBindingModel.Default
        );
        _pipeline = factory.CreateGraphicsPipeline(ref pd);
        _pipeline.Name = "ImGui.NET Pipeline";

        _mainResourceSet = factory.CreateResourceSet(
            new ResourceSetDescription(
                _layout,
                _projMatrixBuffer,
                gd.PointSampler
            )
        );
        _mainResourceSet.Name = "ImGui.NET Main Resource Set";

        RecreateFontDeviceTexture(gd);
    }

    /// <summary>
    /// Gets or creates a handle for a texture to be drawn with ImGui.
    /// Pass the returned handle to Image() or ImageButton().
    /// </summary>
    public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView)
    {
        if (!_setsByView.TryGetValue(textureView, out var rsi))
        {
            var resourceSet =
                factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, textureView));
            resourceSet.Name = $"ImGui.NET {textureView.Name} Resource Set";
            rsi = new ResourceSetInfo(GetNextImGuiBindingId(), resourceSet);

            _setsByView.Add(textureView, rsi);
            _viewsById.Add(rsi.ImGuiBinding, rsi);
            _ownedResources.Add(resourceSet);
        }

        return rsi.ImGuiBinding;
    }

    public void RemoveImGuiBinding(TextureView textureView)
    {
        if (_setsByView.TryGetValue(textureView, out var rsi))
        {
            _setsByView.Remove(textureView);
            _viewsById.Remove(rsi.ImGuiBinding);
            _ownedResources.Remove(rsi.ResourceSet);
            rsi.ResourceSet.Dispose();
        }
    }

    private IntPtr GetNextImGuiBindingId()
    {
        var newId = _lastAssignedId++;
        return (IntPtr)newId;
    }

    /// <summary>
    /// Gets or creates a handle for a texture to be drawn with ImGui.
    /// Pass the returned handle to Image() or ImageButton().
    /// </summary>
    public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, Texture texture)
    {
        if (!_autoViewsByTexture.TryGetValue(texture, out var textureView))
        {
            textureView = factory.CreateTextureView(texture);
            textureView.Name = $"ImGui.NET {texture.Name} View";
            _autoViewsByTexture.Add(texture, textureView);
            _ownedResources.Add(textureView);
        }

        return GetOrCreateImGuiBinding(factory, textureView);
    }

    public void RemoveImGuiBinding(Texture texture)
    {
        if (_autoViewsByTexture.TryGetValue(texture, out var textureView))
        {
            _autoViewsByTexture.Remove(texture);
            _ownedResources.Remove(textureView);
            textureView.Dispose();
            RemoveImGuiBinding(textureView);
        }
    }

    /// <summary>
    /// Retrieves the shader texture binding for the given helper handle.
    /// </summary>
    public ResourceSet GetImageResourceSet(IntPtr imGuiBinding)
    {
        if (!_viewsById.TryGetValue(imGuiBinding, out var rsi))
        {
            throw new InvalidOperationException("No registered ImGui binding with id " + imGuiBinding.ToString());
        }

        return rsi.ResourceSet;
    }

    public void ClearCachedImageResources()
    {
        foreach (var resource in _ownedResources)
        {
            resource.Dispose();
        }

        _ownedResources.Clear();
        _setsByView.Clear();
        _viewsById.Clear();
        _autoViewsByTexture.Clear();
        _lastAssignedId = 100;
    }

    private byte[] LoadEmbeddedShaderCode(
        ResourceFactory factory,
        string name,
        ShaderStages stage
    )
    {
        switch (factory.BackendType)
        {
            case GraphicsBackend.Direct3D11:
            {
                var resourceName = "HLSL." + name + ".hlsl";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.OpenGL:
            {
                var resourceName = "GLSL." + name + ".glsl";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.OpenGLES:
            {
                var resourceName = "GLSLES." + name + ".glsles";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.Vulkan:
            {
                var resourceName = "SPIR_V." + name + ".spv";
                return GetEmbeddedResourceBytes(resourceName);
            }
            case GraphicsBackend.Metal:
            {
                var resourceName = "Metal." + name + ".metallib";
                return GetEmbeddedResourceBytes(resourceName);
            }
            default:
                throw new ArgumentException($"Unkown graphics backend {factory.BackendType}", nameof(factory));
        }
    }

    private string GetEmbeddedResourceText(string resourceName)
    {
        using var sr = new StreamReader(_assembly.GetManifestResourceStream(resourceName)!);
        return sr.ReadToEnd();
    }

    private byte[] GetEmbeddedResourceBytes(string resourceName)
    {
        resourceName = "CobaltCoreEditor.Assets." + resourceName;
        using var s = _assembly.GetManifestResourceStream(resourceName);
        if (s == null)
        {
            var names = _assembly.GetManifestResourceNames();
            foreach (var name in names)
            {
                Console.WriteLine(name);
            }

            throw new NullReferenceException($"Failed to find embedded resource at path {resourceName}");
        }

        var ret = new byte[s.Length];
        s.Read(ret, 0, (int)s.Length);
        return ret;
    }

    /// <summary>
    /// Recreates the device texture used to render text.
    /// </summary>
    public unsafe void RecreateFontDeviceTexture() => RecreateFontDeviceTexture(_gd);

    /// <summary>
    /// Recreates the device texture used to render text.
    /// </summary>
    public unsafe void RecreateFontDeviceTexture(GraphicsDevice gd)
    {
        var io = ImGui.GetIO();
        // Build
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out var width, out var height, out var bytesPerPixel);

        // Store our identifier
        io.Fonts.SetTexID(_fontAtlasId);

        _fontTexture?.Dispose();
        _fontTexture = gd.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(
                (uint)width,
                (uint)height,
                1,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled
            )
        );
        _fontTexture.Name = "ImGui.NET Font Texture";
        gd.UpdateTexture(
            _fontTexture,
            (IntPtr)pixels,
            (uint)(bytesPerPixel * width * height),
            0,
            0,
            0,
            (uint)width,
            (uint)height,
            1,
            0,
            0
        );

        _fontTextureResourceSet?.Dispose();
        _fontTextureResourceSet =
            gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_textureLayout, _fontTexture));
        _fontTextureResourceSet.Name = "ImGui.NET Font Texture Resource Set";

        io.Fonts.ClearTexData();
    }

    /// <summary>
    /// Renders the ImGui draw list data.
    /// </summary>
    public unsafe void Render(GraphicsDevice gd, CommandList cl)
    {
        if (_frameBegun)
        {
            _frameBegun = false;
            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData(), gd, cl);
        }
    }

    /// <summary>
    /// Updates ImGui input and IO configuration state.
    /// </summary>
    public void Update(float deltaSeconds, InputSnapshot snapshot)
    {
        BeginUpdate(deltaSeconds);
        UpdateImGuiInput(snapshot);
        EndUpdate();
    }

    /// <summary>
    /// Called before we handle the input in <see cref="Update(float, InputSnapshot)"/>.
    /// This render ImGui and update the state.
    /// </summary>
    protected void BeginUpdate(float deltaSeconds)
    {
        if (_frameBegun)
        {
            ImGui.Render();
        }

        SetPerFrameImGuiData(deltaSeconds);
    }

    /// <summary>
    /// Called at the end of <see cref="Update(float, InputSnapshot)"/>.
    /// This tells ImGui that we are on the next frame.
    /// </summary>
    protected void EndUpdate()
    {
        _frameBegun = true;
        ImGui.NewFrame();
    }

    /// <summary>
    /// Sets per-frame data based on the associated window.
    /// This is called by Update(float).
    /// </summary>
    private unsafe void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(
            WindowWidth / _scaleFactor.X,
            WindowHeight / _scaleFactor.Y
        );
        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
    }

    private bool TryMapKey(Key key, out ImGuiKey result)
    {
        ImGuiKey KeyToImGuiKeyShortcut(Key keyToConvert, Key startKey1, ImGuiKey startKey2)
        {
            var changeFromStart1 = (int)keyToConvert - (int)startKey1;
            return startKey2 + changeFromStart1;
        }

        switch (key)
        {
            case >= Key.F1 and <= Key.F12:
                result = KeyToImGuiKeyShortcut(key, Key.F1, ImGuiKey.F1);
                return true;
            case >= Key.Keypad0 and <= Key.Keypad9:
                result = KeyToImGuiKeyShortcut(key, Key.Keypad0, ImGuiKey.Keypad0);
                return true;
            case >= Key.A and <= Key.Z:
                result = KeyToImGuiKeyShortcut(key, Key.A, ImGuiKey.A);
                return true;
            case >= Key.Number0 and <= Key.Number9:
                result = KeyToImGuiKeyShortcut(key, Key.Number0, ImGuiKey._0);
                return true;
            default:
                switch (key)
                {
                    case Key.ShiftLeft:
                    case Key.ShiftRight:
                        result = ImGuiKey.ModShift;
                        return true;
                    case Key.ControlLeft:
                    case Key.ControlRight:
                        result = ImGuiKey.ModCtrl;
                        return true;
                    case Key.AltLeft:
                    case Key.AltRight:
                        result = ImGuiKey.ModAlt;
                        return true;
                    case Key.WinLeft:
                    case Key.WinRight:
                        result = ImGuiKey.ModSuper;
                        return true;
                    case Key.Menu:
                        result = ImGuiKey.Menu;
                        return true;
                    case Key.Up:
                        result = ImGuiKey.UpArrow;
                        return true;
                    case Key.Down:
                        result = ImGuiKey.DownArrow;
                        return true;
                    case Key.Left:
                        result = ImGuiKey.LeftArrow;
                        return true;
                    case Key.Right:
                        result = ImGuiKey.RightArrow;
                        return true;
                    case Key.Enter:
                        result = ImGuiKey.Enter;
                        return true;
                    case Key.Escape:
                        result = ImGuiKey.Escape;
                        return true;
                    case Key.Space:
                        result = ImGuiKey.Space;
                        return true;
                    case Key.Tab:
                        result = ImGuiKey.Tab;
                        return true;
                    case Key.BackSpace:
                        result = ImGuiKey.Backspace;
                        return true;
                    case Key.Insert:
                        result = ImGuiKey.Insert;
                        return true;
                    case Key.Delete:
                        result = ImGuiKey.Delete;
                        return true;
                    case Key.PageUp:
                        result = ImGuiKey.PageUp;
                        return true;
                    case Key.PageDown:
                        result = ImGuiKey.PageDown;
                        return true;
                    case Key.Home:
                        result = ImGuiKey.Home;
                        return true;
                    case Key.End:
                        result = ImGuiKey.End;
                        return true;
                    case Key.CapsLock:
                        result = ImGuiKey.CapsLock;
                        return true;
                    case Key.ScrollLock:
                        result = ImGuiKey.ScrollLock;
                        return true;
                    case Key.PrintScreen:
                        result = ImGuiKey.PrintScreen;
                        return true;
                    case Key.Pause:
                        result = ImGuiKey.Pause;
                        return true;
                    case Key.NumLock:
                        result = ImGuiKey.NumLock;
                        return true;
                    case Key.KeypadDivide:
                        result = ImGuiKey.KeypadDivide;
                        return true;
                    case Key.KeypadMultiply:
                        result = ImGuiKey.KeypadMultiply;
                        return true;
                    case Key.KeypadSubtract:
                        result = ImGuiKey.KeypadSubtract;
                        return true;
                    case Key.KeypadAdd:
                        result = ImGuiKey.KeypadAdd;
                        return true;
                    case Key.KeypadDecimal:
                        result = ImGuiKey.KeypadDecimal;
                        return true;
                    case Key.KeypadEnter:
                        result = ImGuiKey.KeypadEnter;
                        return true;
                    case Key.Tilde:
                        result = ImGuiKey.GraveAccent;
                        return true;
                    case Key.Minus:
                        result = ImGuiKey.Minus;
                        return true;
                    case Key.Plus:
                        result = ImGuiKey.Equal;
                        return true;
                    case Key.BracketLeft:
                        result = ImGuiKey.LeftBracket;
                        return true;
                    case Key.BracketRight:
                        result = ImGuiKey.RightBracket;
                        return true;
                    case Key.Semicolon:
                        result = ImGuiKey.Semicolon;
                        return true;
                    case Key.Quote:
                        result = ImGuiKey.Apostrophe;
                        return true;
                    case Key.Comma:
                        result = ImGuiKey.Comma;
                        return true;
                    case Key.Period:
                        result = ImGuiKey.Period;
                        return true;
                    case Key.Slash:
                        result = ImGuiKey.Slash;
                        return true;
                    case Key.BackSlash:
                    case Key.NonUSBackSlash:
                        result = ImGuiKey.Backslash;
                        return true;
                    default:
                        result = ImGuiKey.GamepadBack;
                        return false;
                }

                break;
        }
    }

    private unsafe void UpdateImGuiInput(InputSnapshot snapshot)
    {
        var io = ImGui.GetIO();
        io.AddMousePosEvent(snapshot.MousePosition.X, snapshot.MousePosition.Y);
        io.AddMouseButtonEvent(0, snapshot.IsMouseDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, snapshot.IsMouseDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, snapshot.IsMouseDown(MouseButton.Middle));
        io.AddMouseButtonEvent(3, snapshot.IsMouseDown(MouseButton.Button1));
        io.AddMouseButtonEvent(4, snapshot.IsMouseDown(MouseButton.Button2));
        io.AddMouseWheelEvent(0f, snapshot.WheelDelta);

        for (var i = 0; i < snapshot.KeyCharPresses.Count; i++)
        {
            io.AddInputCharacter(snapshot.KeyCharPresses[i]);
        }

        for (var i = 0; i < snapshot.KeyEvents.Count; i++)
        {
            var keyEvent = snapshot.KeyEvents[i];
            if (TryMapKey(keyEvent.Key, out var imguikey))
            {
                io.AddKeyEvent(imguikey, keyEvent.Down);
            }
        }
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawData, GraphicsDevice gd, CommandList cl)
    {
        uint vertexOffsetInVertices = 0;
        uint indexOffsetInElements = 0;

        if (drawData.CmdListsCount == 0)
        {
            return;
        }

        var totalVbSize = (uint)(drawData.TotalVtxCount * sizeof(ImDrawVert));
        if (totalVbSize > _vertexBuffer.SizeInBytes)
        {
            _vertexBuffer.Dispose();
            _vertexBuffer = gd.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(totalVbSize * 1.5f), BufferUsage.VertexBuffer | BufferUsage.Dynamic)
            );
            _vertexBuffer.Name = $"ImGui.NET Vertex Buffer";
        }

        var totalIbSize = (uint)(drawData.TotalIdxCount * sizeof(ushort));
        if (totalIbSize > _indexBuffer.SizeInBytes)
        {
            _indexBuffer.Dispose();
            _indexBuffer = gd.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(totalIbSize * 1.5f), BufferUsage.IndexBuffer | BufferUsage.Dynamic)
            );
            _indexBuffer.Name = $"ImGui.NET Index Buffer";
        }

        for (var i = 0; i < drawData.CmdListsCount; i++)
        {
            var cmdList = drawData.CmdLists[i];

            cl.UpdateBuffer(
                _vertexBuffer,
                vertexOffsetInVertices * (uint)sizeof(ImDrawVert),
                cmdList.VtxBuffer.Data,
                (uint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert))
            );

            cl.UpdateBuffer(
                _indexBuffer,
                indexOffsetInElements * sizeof(ushort),
                cmdList.IdxBuffer.Data,
                (uint)(cmdList.IdxBuffer.Size * sizeof(ushort))
            );

            vertexOffsetInVertices += (uint)cmdList.VtxBuffer.Size;
            indexOffsetInElements += (uint)cmdList.IdxBuffer.Size;
        }

        // Setup orthographic projection matrix into our constant buffer
        {
            var io = ImGui.GetIO();

            var mvp = Matrix4x4.CreateOrthographicOffCenter(
                0f,
                io.DisplaySize.X,
                io.DisplaySize.Y,
                0.0f,
                -1.0f,
                1.0f
            );

            _gd.UpdateBuffer(_projMatrixBuffer, 0, ref mvp);
        }

        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _mainResourceSet);

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        // Render command lists
        var vtxOffset = 0;
        var idxOffset = 0;
        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            for (var cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                var pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    throw new Exception("UserCallback not supported");
                }

                if (pcmd.TextureId != IntPtr.Zero)
                {
                    cl.SetGraphicsResourceSet(
                        1,
                        pcmd.TextureId == _fontAtlasId ? _fontTextureResourceSet : GetImageResourceSet(pcmd.TextureId)
                    );
                }

                cl.SetScissorRect(
                    0,
                    (uint)pcmd.ClipRect.X,
                    (uint)pcmd.ClipRect.Y,
                    (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                    (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y)
                );

                cl.DrawIndexed(
                    pcmd.ElemCount,
                    1,
                    pcmd.IdxOffset + (uint)idxOffset,
                    (int)(pcmd.VtxOffset + vtxOffset),
                    0
                );
            }

            idxOffset += cmdList.IdxBuffer.Size;
            vtxOffset += cmdList.VtxBuffer.Size;
        }
    }

    /// <summary>
    /// Frees all graphics resources used by the renderer.
    /// </summary>
    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _projMatrixBuffer.Dispose();
        _fontTexture.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        _layout.Dispose();
        _textureLayout.Dispose();
        _pipeline.Dispose();
        _mainResourceSet.Dispose();
        _fontTextureResourceSet.Dispose();

        foreach (var resource in _ownedResources)
        {
            resource.Dispose();
        }
    }

    private struct ResourceSetInfo
    {
        public readonly IntPtr ImGuiBinding;
        public readonly ResourceSet ResourceSet;

        public ResourceSetInfo(IntPtr imGuiBinding, ResourceSet resourceSet)
        {
            ImGuiBinding = imGuiBinding;
            ResourceSet = resourceSet;
        }
    }
}