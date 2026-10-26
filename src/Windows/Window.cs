using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Aethermeter.UI;

namespace Aethermeter.Windows;

/// <summary>
/// Base for the regular (titled) windows. Applies the theme before Begin so the frame and title
/// bar share the look, and keeps a draw error from ever leaving ImGui's window stack unbalanced.
/// </summary>
public abstract class Window : IDisposable
{
    protected string WindowName { get; }

    private bool isOpen;

    public bool IsOpen
    {
        get => isOpen;
        set => isOpen = value;
    }

    protected ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;
    protected Vector2? Size { get; set; }
    protected Vector2 SizeMin { get; set; } = new(300, 200);

    /// <summary>Centres the window on the screen each time it appears.</summary>
    protected bool CenterOnAppearing { get; set; }

    /// <summary>Called the frame the user closes the window with its title-bar button.</summary>
    protected virtual void OnClosed()
    {
    }

    protected Window(string name) => WindowName = name;

    public void Draw()
    {
        if (!IsOpen) return;

        try
        {
            using var theme = Theme.Push();
            ImGui.SetNextWindowSizeConstraints(SizeMin, new Vector2(4000, 4000));
            if (Size.HasValue) ImGui.SetNextWindowSize(Size.Value, ImGuiCond.FirstUseEver);
            if (CenterOnAppearing)
                ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            var wasOpen = isOpen;
            if (ImGui.Begin(Title() + "###" + WindowName, ref isOpen, WindowFlags))
            {
                try
                {
                    DrawContents();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Error drawing contents of {WindowName}", WindowName);
                }
            }

            ImGui.End();

            if (wasOpen && !isOpen)
                OnClosed();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error drawing window {WindowName}", WindowName);
        }
    }

    /// <summary>The visible title; the window's identity stays <see cref="WindowName"/>.</summary>
    protected virtual string Title() => WindowName;

    protected abstract void DrawContents();

    public virtual void Dispose()
    {
    }
}
