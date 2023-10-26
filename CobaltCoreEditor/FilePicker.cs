// Based on these gists by mellinoe and prime31
// https://github.com/ImGuiNET/ImGui.NET/issues/22#issuecomment-488557014
// https://gist.githubusercontent.com/prime31/91d1582624eb2635395417393018016e/raw/fb00f3df1665d49dddeba5f191a70003151ce7f0/FilePicker.cs
using ImGuiNET;
using Num = System.Numerics;
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Local

namespace CobaltCoreEditor;

public class FilePicker
{
    private static readonly Dictionary<object, FilePicker> FilePickers = new Dictionary<object, FilePicker>();

    public string RootFolder = "";
    public string CurrentFolder = "";
    public string SelectedFile = "";
    public readonly List<string> AllowedExtensions = new();
    public bool OnlyAllowFolders;

    public static FilePicker GetFolderPicker(object o, string startingPath)
        => GetFilePicker(o, startingPath, null, true);

    public static FilePicker GetFilePicker(
        object o,
        string startingPath,
        string? searchFilter = null,
        bool onlyAllowFolders = false
    )
    {
        if (File.Exists(startingPath))
        {
            startingPath = new FileInfo(startingPath).DirectoryName ?? "";
        }
        else if (string.IsNullOrEmpty(startingPath) || !Directory.Exists(startingPath))
        {
            startingPath = Environment.CurrentDirectory;
            if (string.IsNullOrEmpty(startingPath)) startingPath = AppContext.BaseDirectory;
        }

        if (FilePickers.TryGetValue(o, out var fp)) return fp;
        
        fp = new FilePicker
        {
            RootFolder = startingPath,
            CurrentFolder = startingPath,
            OnlyAllowFolders = onlyAllowFolders
        };

        if (searchFilter != null)
        {
            fp.AllowedExtensions.Clear();

            fp.AllowedExtensions.AddRange(
                searchFilter.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
            );
        }

        FilePickers.Add(o, fp);

        return fp;
    }

    public static void RemoveFilePicker(object o) => FilePickers.Remove(o);

    public bool Draw()
    {
        ImGui.Text("Current Folder: " + Path.GetFileName(RootFolder) + CurrentFolder.Replace(RootFolder, ""));
        var result = false;
        
        if (ImGui.BeginChildFrame(1, new Num.Vector2(400, 400)))
        {
            var di = new DirectoryInfo(CurrentFolder);
            if (di.Exists)
            {
                if (di.Parent != null)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xFF00FFFF);
                    if (ImGui.Selectable("../", false, ImGuiSelectableFlags.DontClosePopups))
                        CurrentFolder = di.Parent.FullName;

                    ImGui.PopStyleColor();
                }

                var fileSystemEntries = GetFileSystemEntries(di.FullName);
                foreach (var fse in fileSystemEntries)
                {
                    if (Directory.Exists(fse))
                    {
                        var name = Path.GetFileName(fse);
                        ImGui.PushStyleColor(ImGuiCol.Text, 0xFF00FFFF);
                        if (ImGui.Selectable(name + "/", false, ImGuiSelectableFlags.DontClosePopups))
                            CurrentFolder = fse;
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        var name = Path.GetFileName(fse);
                        var isSelected = SelectedFile == fse;
                        if (ImGui.Selectable(name, isSelected, ImGuiSelectableFlags.DontClosePopups))
                            SelectedFile = fse;

                        if (ImGui.IsMouseDoubleClicked(0))
                        {
                            result = true;
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }
            }
        }

        ImGui.EndChildFrame();


        if (ImGui.Button("Cancel"))
        {
            result = false;
            ImGui.CloseCurrentPopup();
        }

        if (OnlyAllowFolders)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open"))
            {
                result = true;
                SelectedFile = CurrentFolder;
                ImGui.CloseCurrentPopup();
            }
        }
        else if (SelectedFile != "")
        {
            ImGui.SameLine();
            if (ImGui.Button("Open"))
            {
                result = true;
                ImGui.CloseCurrentPopup();
            }
        }

        return result;
    }

    private static bool TryGetFileInfo(string fileName, out FileInfo? realFile)
    {
        try
        {
            realFile = new FileInfo(fileName);
            return true;
        }
        catch
        {
            realFile = null;
            return false;
        }
    }

    private List<string> GetFileSystemEntries(string fullName)
    {
        var files = new List<string>();
        var dirs = new List<string>();

        foreach (var fse in Directory.GetFileSystemEntries(fullName, ""))
        {
            if (Directory.Exists(fse))
            {
                dirs.Add(fse);
            }
            else if (!OnlyAllowFolders)
            {
                if (AllowedExtensions != null)
                {
                    var ext = Path.GetExtension(fse);
                    if (AllowedExtensions.Contains(ext)) files.Add(fse);
                }
                else
                {
                    files.Add(fse);
                }
            }
        }

        var ret = new List<string>(dirs);
        ret.AddRange(files);

        return ret;
    }
}