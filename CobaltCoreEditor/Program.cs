//from: https://gist.github.com/mellinoe/5eca1a109a71566f620a05fcb70e74ca

using System.Numerics;
using CobaltCoreEditor;
using ImGuiNET;
using Veldrid;
using Veldrid.StartupUtilities;

VeldridStartup.CreateWindowAndGraphicsDevice(
    new WindowCreateInfo(100, 100, 840, 720, WindowState.Normal, "ITR's Cobalt Core Editor"),
    out var window,
    out var gd
);

var imguiRenderer = new ImGuiRenderer(
    gd,
    gd.MainSwapchain.Framebuffer.OutputDescription,
    (int)gd.MainSwapchain.Framebuffer.Width,
    (int)gd.MainSwapchain.Framebuffer.Height
);
var cl = gd.ResourceFactory.CreateCommandList();
ImGui.GetStyle().WindowRounding = 0.0f;

while (window.Exists)
{
    var input = window.PumpEvents();
    if (!window.Exists)
    {
        break;
    }

    imguiRenderer.Update(1f / 60f, input); // Compute actual value for deltaSeconds.

    imguiRenderer.WindowResized(imguiRenderer.WindowWidth, imguiRenderer.WindowHeight);
    ImGui.SetNextWindowSize(new Vector2(imguiRenderer.WindowWidth, imguiRenderer.WindowHeight));
    ImGui.SetNextWindowPos(new Vector2(0, 0));

    Main.Draw();

    cl.Begin();
    cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
    cl.ClearColorTarget(0, RgbaFloat.Black);
    imguiRenderer.Render(gd, cl);
    cl.End();
    gd.SubmitCommands(cl);
    gd.SwapBuffers(gd.MainSwapchain);
}

Settings.Save();